"""Acces en lecture seule a la base d'inventaire.

Le site public et le serveur du terminal sont deux processus distincts qui
partagent un seul fichier SQLite. Celui du terminal ecrit, celui-ci ne fait
que lire -- et cette garantie ne repose pas sur la discipline du code.

Deux verrous, poses l'un derriere l'autre :

* la base est ouverte par une URI `mode=ro`, donc le fichier est ouvert en
  lecture seule au niveau du systeme ;
* la connexion porte `PRAGMA query_only`, qui fait echouer toute ecriture au
  niveau de SQLite.

Le premier peut manquer : une base en WAL exige un `-shm` accessible en
ecriture, absent quand personne n'a ouvert la base depuis le dernier arret
propre. On retombe alors sur une ouverture normale, ou seul `query_only`
protege -- ce qui reste un refus de SQLite, pas une convention.

Le rafraichissement temps reel s'appuie sur `PRAGMA data_version` : sa valeur
change des qu'une *autre* connexion valide une transaction. Comme on n'ecrit
jamais, toute variation signale un mouvement du cote du terminal. C'est un
entier a lire, pas un fichier a surveiller ni une notification a configurer.
"""

from __future__ import annotations

import contextlib
import datetime as dt
import sqlite3
import threading
import urllib.parse
from pathlib import Path

__all__ = ["Lecture", "jours_restants", "aujourdhui"]

JOUR, MOIS = "jour", "mois"


def aujourdhui() -> dt.date:
    return dt.date.today()


def jours_restants(peremption: str | None, reference: dt.date | None = None) -> int | None:
    if not peremption:
        return None
    try:
        echeance = dt.date.fromisoformat(peremption)
    except (ValueError, TypeError):
        return None
    return (echeance - (reference or aujourdhui())).days


class Lecture:
    """Une base d'inventaire, vue depuis le site public."""

    def __init__(self, chemin: str | Path) -> None:
        self.chemin = Path(chemin)
        self.uri = "file:" + urllib.parse.quote(str(self.chemin.resolve())) + "?mode=ro"
        self.seulement_ro = True
        self._persistante: sqlite3.Connection | None = None
        self._verrou = threading.Lock()

    # ------------------------------------------------------------ connexions

    def _ouvrir(self, partagee: bool = False) -> sqlite3.Connection:
        """Une connexion en lecture seule.

        `partagee` leve le garde-fou de thread de sqlite3 : la connexion de
        veille est ouverte par le thread qui la cree et relue par celui qui
        surveille. Le verrou de l'objet serialise les acces, et SQLite lui-meme
        est compile en mode serialise.
        """
        try:
            cx = sqlite3.connect(self.uri, uri=True, timeout=5.0,
                                 check_same_thread=not partagee)
            cx.row_factory = sqlite3.Row
            # connect() est paresseux : il faut une vraie requete pour savoir
            # si le fichier s'ouvre.
            cx.execute("SELECT 1 FROM sqlite_master LIMIT 1")
        except sqlite3.Error:
            self.seulement_ro = False
            cx = sqlite3.connect(self.chemin, timeout=5.0,
                                 check_same_thread=not partagee)
            cx.row_factory = sqlite3.Row
        cx.execute("PRAGMA query_only = 1")
        cx.execute("PRAGMA busy_timeout = 5000")
        return cx

    @contextlib.contextmanager
    def cx(self):
        """Une connexion le temps d'une requete HTTP, puis refermee.

        Le serveur cree un thread par requete : une connexion gardee dans un
        thread-local ne serait jamais rendue. Ouvrir un fichier SQLite coute
        quelques dizaines de microsecondes, largement moins que le JSON qu'on
        s'apprete a serialiser.
        """
        cx = self._ouvrir()
        try:
            yield cx
        finally:
            cx.close()

    def version_donnees(self) -> int:
        """Compteur de commits des autres connexions. Voir l'en-tete du module.

        La valeur n'a de sens que compare a la precedente **sur la meme
        connexion** : celle-ci est donc gardee ouverte pour toute la duree du
        processus. Une base momentanement illisible rend -1, que l'appelant
        traite comme « rien de neuf » plutot que comme un changement.
        """
        with self._verrou:
            if self._persistante is None:
                try:
                    self._persistante = self._ouvrir(partagee=True)
                except sqlite3.Error:
                    return -1
            try:
                return int(self._persistante.execute("PRAGMA data_version").fetchone()[0])
            except sqlite3.Error:
                with contextlib.suppress(sqlite3.Error):
                    self._persistante.close()
                self._persistante = None
                return -1

    def fermer(self) -> None:
        with self._verrou:
            if self._persistante is not None:
                with contextlib.suppress(sqlite3.Error):
                    self._persistante.close()
                self._persistante = None

    def existe(self) -> bool:
        return self.chemin.is_file()

    def poids_fichier(self) -> int:
        total = 0
        for suffixe in ("", "-wal", "-shm"):
            fichier = Path(str(self.chemin) + suffixe)
            if fichier.is_file():
                total += fichier.stat().st_size
        return total

    # --------------------------------------------------------------- lecture

    def postes(self) -> list[dict]:
        """Un poste par reference en stock, fiche produit jointe.

        Tout ce que le site affiche part d'ici : le tableau de bord agrege ces
        lignes, l'inventaire les liste, le frigo les dessine. Une seule
        requete, une seule verite.
        """
        requete = """
            SELECT l.code                         AS code,
                   SUM(l.qte)                     AS total,
                   COUNT(*)                       AS nb_lots,
                   MIN(l.peremption)              AS urgence,
                   MAX(l.peremption)              AS lointaine,
                   MIN(l.ajoute_le)               AS premier_ajout,
                   MAX(l.ajoute_le)               AS dernier_ajout,
                   p.nom, p.marque, p.quantite, p.image_url, p.nutriscore,
                   p.nova, p.ecoscore, p.niv_graisses, p.niv_satures,
                   p.niv_sucres, p.niv_sel, p.kcal, p.proteines, p.glucides,
                   p.sucres, p.lipides, p.satures, p.sel, p.fibres,
                   p.energie_kj, p.allergenes, p.traces, p.additifs, p.labels,
                   p.categories, p.portion, p.origine, p.ingredients,
                   p.source, p.maj
              FROM lot l LEFT JOIN produit p ON p.code = l.code
             WHERE l.qte > 0
             GROUP BY l.code
        """
        with self.cx() as cx:
            lignes = list(cx.execute(requete))
            lots = list(cx.execute(
                "SELECT * FROM lot WHERE qte > 0 "
                "ORDER BY peremption IS NULL, peremption, id"))
        par_code: dict[str, list[dict]] = {}
        for lot in lots:
            par_code.setdefault(lot["code"], []).append({
                "id": lot["id"],
                "qte": int(lot["qte"]),
                "peremption": lot["peremption"] or "",
                "precision": lot["precision"] or JOUR,
                "ajoute_le": lot["ajoute_le"] or "",
                "jours": jours_restants(lot["peremption"]),
            })
        return [self._poste(ligne, par_code.get(ligne["code"], [])) for ligne in lignes]

    @staticmethod
    def _poste(ligne: sqlite3.Row, lots: list[dict]) -> dict:
        cles = ligne.keys()
        fiche = {cle: ligne[cle] for cle in cles}
        urgence = fiche.get("urgence")
        return {
            "code": fiche["code"],
            "nom": fiche.get("nom") or "",
            "marque": fiche.get("marque") or "",
            "contenance": fiche.get("quantite") or "",
            "portion": fiche.get("portion") or "",
            "image": 1 if fiche.get("image_url") else 0,
            "nutriscore": (fiche.get("nutriscore") or "").lower(),
            "nova": int(fiche["nova"]) if fiche.get("nova") else 0,
            "ecoscore": (fiche.get("ecoscore") or "").lower(),
            "niveaux": {
                "graisses": fiche.get("niv_graisses") or "",
                "satures": fiche.get("niv_satures") or "",
                "sucres": fiche.get("niv_sucres") or "",
                "sel": fiche.get("niv_sel") or "",
            },
            "nutrition": {
                "kcal": fiche.get("kcal"), "kj": fiche.get("energie_kj"),
                "proteines": fiche.get("proteines"), "glucides": fiche.get("glucides"),
                "sucres": fiche.get("sucres"), "lipides": fiche.get("lipides"),
                "satures": fiche.get("satures"), "sel": fiche.get("sel"),
                "fibres": fiche.get("fibres"),
            },
            "allergenes": fiche.get("allergenes") or "",
            "traces": fiche.get("traces") or "",
            "additifs": fiche.get("additifs") or "",
            "labels": fiche.get("labels") or "",
            "categories": fiche.get("categories") or "",
            "origine": fiche.get("origine") or "",
            "ingredients": fiche.get("ingredients") or "",
            "source": fiche.get("source") or "inconnu",
            "maj": fiche.get("maj") or "",
            "total": int(fiche["total"] or 0),
            "nb_lots": int(fiche["nb_lots"] or 0),
            "peremption": urgence or "",
            "lointaine": fiche.get("lointaine") or "",
            "precision": lots[0]["precision"] if lots else JOUR,
            "jours": jours_restants(urgence),
            "premier_ajout": fiche.get("premier_ajout") or "",
            "dernier_ajout": fiche.get("dernier_ajout") or "",
            "lots": lots,
        }

    def journal(self, limite: int = 400) -> list[dict]:
        with self.cx() as cx:
            lignes = cx.execute("""
                SELECT j.ts, j.code, j.delta, j.peremption, j.action, j.origine,
                       IFNULL(p.nom, j.code) AS nom, p.nutriscore AS nutriscore
                  FROM journal j LEFT JOIN produit p ON p.code = j.code
                 ORDER BY j.id DESC LIMIT ?
            """, (max(1, min(limite, 5000)),))
            return [{
                "ts": ligne["ts"] or "",
                "code": ligne["code"] or "",
                "nom": ligne["nom"] or ligne["code"] or "",
                "delta": int(ligne["delta"] or 0),
                "action": ligne["action"] or "",
                "origine": ligne["origine"] or "",
                "peremption": ligne["peremption"] or "",
                "nutriscore": (ligne["nutriscore"] or "").lower(),
            } for ligne in lignes]

    def journal_complet(self) -> list[tuple[str, int, str]]:
        """(horodatage, delta, action) de tout le journal, du plus ancien au plus recent."""
        with self.cx() as cx:
            return [(ligne["ts"] or "", int(ligne["delta"] or 0), ligne["action"] or "")
                    for ligne in cx.execute(
                        "SELECT ts, delta, action FROM journal ORDER BY id")]

    def produit(self, code: str) -> dict | None:
        with self.cx() as cx:
            ligne = cx.execute("SELECT * FROM produit WHERE code = ?", (code,)).fetchone()
        return dict(ligne) if ligne is not None else None

    def article(self, code: str) -> dict | None:
        """Tout ce que la base sait d'une reference : fiche, lots, historique."""
        fiche = self.produit(code)
        with self.cx() as cx:
            lots = list(cx.execute(
                "SELECT * FROM lot WHERE code = ? AND qte > 0 "
                "ORDER BY peremption IS NULL, peremption, id", (code,)))
            mouvements = list(cx.execute(
                "SELECT ts, delta, action, origine, peremption FROM journal "
                "WHERE code = ? ORDER BY id DESC LIMIT 120", (code,)))
            alias = list(cx.execute(
                "SELECT code_lu, pose_le FROM alias WHERE code = ? AND code_lu <> code",
                (code,)))
        if fiche is None and not lots and not mouvements:
            return None
        ligne = {"code": code, **(fiche or {})}
        detail = self._poste(
            _FausseLigne({
                **ligne,
                "total": sum(int(lot["qte"]) for lot in lots),
                "nb_lots": len(lots),
                "urgence": lots[0]["peremption"] if lots else None,
                "lointaine": max((lot["peremption"] or "" for lot in lots), default=""),
                "premier_ajout": min((lot["ajoute_le"] or "" for lot in lots), default=""),
                "dernier_ajout": max((lot["ajoute_le"] or "" for lot in lots), default=""),
            }),
            [{
                "id": lot["id"], "qte": int(lot["qte"]),
                "peremption": lot["peremption"] or "",
                "precision": lot["precision"] or JOUR,
                "ajoute_le": lot["ajoute_le"] or "",
                "jours": jours_restants(lot["peremption"]),
            } for lot in lots])
        detail["mouvements"] = [{
            "ts": m["ts"] or "", "delta": int(m["delta"] or 0),
            "action": m["action"] or "", "origine": m["origine"] or "",
            "peremption": m["peremption"] or "",
        } for m in mouvements]
        detail["alias"] = [{"code_lu": a["code_lu"], "pose_le": a["pose_le"] or ""}
                           for a in alias]
        return detail

    def codes_connus(self) -> set[str]:
        with self.cx() as cx:
            return {ligne["code"] for ligne in cx.execute("SELECT code FROM produit")}

    def image_url(self, code: str) -> str | None:
        with self.cx() as cx:
            ligne = cx.execute("SELECT image_url FROM produit WHERE code = ?",
                               (code,)).fetchone()
        return (ligne["image_url"] or None) if ligne is not None else None

    def compteurs(self) -> dict:
        with self.cx() as cx:
            c = cx.execute("""
                SELECT (SELECT IFNULL(SUM(qte),0) FROM lot WHERE qte>0)            AS unites,
                       (SELECT COUNT(DISTINCT code) FROM lot WHERE qte>0)          AS refs,
                       (SELECT COUNT(*) FROM lot WHERE qte>0)                      AS lots,
                       (SELECT COUNT(*) FROM produit)                              AS produits,
                       (SELECT COUNT(*) FROM produit WHERE source='openfoodfacts') AS fiches,
                       (SELECT COUNT(*) FROM produit WHERE source='manuel')        AS manuels,
                       (SELECT COUNT(*) FROM journal)                              AS mouvements,
                       (SELECT COUNT(*) FROM alias)                                AS alias,
                       (SELECT MIN(ts) FROM journal)                               AS premier,
                       (SELECT MAX(ts) FROM journal)                               AS dernier
            """).fetchone()
            perimes = cx.execute(
                "SELECT IFNULL(SUM(qte),0) AS n FROM lot "
                "WHERE qte>0 AND peremption IS NOT NULL AND peremption < ?",
                (aujourdhui().isoformat(),)).fetchone()
        return {
            "unites": int(c["unites"]), "references": int(c["refs"]),
            "lots": int(c["lots"]), "produits": int(c["produits"]),
            "fiches": int(c["fiches"]), "manuels": int(c["manuels"]),
            "mouvements": int(c["mouvements"]), "alias": int(c["alias"]),
            "perimes": int(perimes["n"]),
            "premier_mouvement": c["premier"] or "", "dernier_mouvement": c["dernier"] or "",
        }


class _FausseLigne:
    """Adaptateur : _poste attend un sqlite3.Row, on lui donne un dictionnaire."""

    def __init__(self, donnees: dict) -> None:
        self._donnees = donnees

    def keys(self):
        return list(self._donnees)

    def __getitem__(self, cle):
        return self._donnees.get(cle)
