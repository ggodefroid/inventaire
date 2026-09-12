"""Stockage SQLite : le stock, le cache produit, le journal.

Un *lot* est un groupe d'unites identiques partageant une meme date de
peremption. Trois yaourts achetes ensemble font un lot de trois ; le pot
retrouve au fond du frigo avec une autre date fait un second lot. C'est la
granularite juste pour un frigo : plus fine (une ligne par unite) elle
alourdit l'affichage sur un ecran de 240 pixels sans rien apporter, plus
grossiere (une quantite par code) elle perd la peremption, qui est le sujet.

Les sorties suivent la regle FEFO -- *first expired, first out* : retirer une
unite sort toujours celle qui perime le plus tot. C'est ce que fait un humain
devant son frigo, et ca evite d'avoir a choisir un lot sur le terminal.
"""

from __future__ import annotations

import calendar
import contextlib
import datetime as dt
import sqlite3
import threading
from pathlib import Path

__all__ = ["Base", "BASE_PAR_DEFAUT", "aujourdhui", "jours_restants"]

BASE_PAR_DEFAUT = Path(__file__).resolve().parent.parent / "donnees" / "inventaire.db"

SCHEMA = """
CREATE TABLE IF NOT EXISTS produit (
    code         TEXT PRIMARY KEY,
    nom          TEXT,
    marque       TEXT,
    quantite     TEXT,          -- conditionnement : "400 g", "1 L"
    image_url    TEXT,
    nutriscore   TEXT,          -- a..e
    nova         INTEGER,       -- 1..4, degre de transformation
    ecoscore     TEXT,          -- a..e
    niv_graisses TEXT,          -- low | moderate | high
    niv_satures  TEXT,
    niv_sucres   TEXT,
    niv_sel      TEXT,
    kcal         REAL,          -- pour 100 g/ml
    proteines    REAL,
    glucides     REAL,
    sucres       REAL,
    lipides      REAL,
    satures      REAL,
    sel          REAL,
    fibres       REAL,
    energie_kj   REAL,
    allergenes   TEXT,
    traces       TEXT,
    additifs     TEXT,          -- numeros E
    labels       TEXT,          -- bio, sans gluten, ...
    categories   TEXT,
    portion      TEXT,          -- taille d'une portion
    origine      TEXT,
    ingredients  TEXT,
    source       TEXT,          -- 'openfoodfacts' | 'manuel' | 'inconnu'
    maj          TEXT           -- horodatage de la derniere interrogation
);

CREATE TABLE IF NOT EXISTS lot (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    code       TEXT NOT NULL,
    qte        INTEGER NOT NULL CHECK (qte > 0),
    peremption TEXT,            -- 'YYYY-MM-DD', ou NULL si inconnue
    precision  TEXT,            -- 'jour' | 'mois' : ce que porte l'emballage
    ajoute_le  TEXT NOT NULL,
    note       TEXT
);
CREATE INDEX IF NOT EXISTS lot_code ON lot(code);
CREATE INDEX IF NOT EXISTS lot_perem ON lot(peremption);
-- Deux ajouts du meme produit avec la meme date doivent fusionner, pas
-- s'empiler : sans cet index, bipper six fois un yaourt donne six lignes.
CREATE UNIQUE INDEX IF NOT EXISTS lot_fusion ON lot(code, IFNULL(peremption, ''));

-- Un lecteur mal regle rend toujours le meme code ampute pour un produit
-- donne. Une fois la bonne lecture etablie -- par le calcul ou par le
-- catalogue -- on la retient : les scans suivants n'ont plus rien a deviner
-- ni a demander au reseau.
CREATE TABLE IF NOT EXISTS alias (
    code_lu TEXT PRIMARY KEY,
    code    TEXT NOT NULL,
    pose_le TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS journal (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    ts         TEXT NOT NULL,
    code       TEXT,
    delta      INTEGER,         -- +n ajout, -n retrait
    peremption TEXT,
    action     TEXT,            -- 'ajout' | 'retrait' | 'correction' | 'purge'
    origine    TEXT             -- 'terminal' | 'web' | 'cli'
);
CREATE INDEX IF NOT EXISTS journal_ts ON journal(ts);
CREATE INDEX IF NOT EXISTS journal_code ON journal(code);
"""

CHAMPS_PRODUIT = (
    "nom", "marque", "quantite", "image_url", "nutriscore", "nova", "ecoscore",
    "niv_graisses", "niv_satures", "niv_sucres", "niv_sel",
    "kcal", "proteines", "glucides", "sucres", "lipides", "satures", "sel",
    "fibres", "energie_kj", "allergenes", "traces", "additifs", "labels",
    "categories", "portion", "origine", "ingredients", "source",
)

# Colonnes apparues apres la premiere mise en service. Une base creee par une
# version anterieure ne les a pas, et CREATE TABLE IF NOT EXISTS ne les ajoute
# pas : il faut les greffer une par une.
COLONNES_AJOUTEES = (
    ("energie_kj", "REAL"), ("traces", "TEXT"), ("additifs", "TEXT"),
    ("labels", "TEXT"), ("portion", "TEXT"), ("origine", "TEXT"),
    ("ingredients", "TEXT"),
)

COLONNES_LOT_AJOUTEES = (
    ("precision", "TEXT"),
)

JOUR, MOIS = "jour", "mois"


def fin_de_mois(annee: int, mois: int) -> str:
    """Dernier jour du mois, en ISO. C'est le sens de « a consommer avant fin... »."""
    return dt.date(annee, mois, calendar.monthrange(annee, mois)[1]).isoformat()


def aujourdhui() -> dt.date:
    return dt.date.today()


def _maintenant() -> str:
    return dt.datetime.now().replace(microsecond=0).isoformat(sep=" ")


def jours_restants(peremption: str | None, reference: dt.date | None = None) -> int | None:
    """Jours avant peremption. None si la date est inconnue, negatif si depasse."""
    if not peremption:
        return None
    try:
        echeance = dt.date.fromisoformat(peremption)
    except ValueError:
        return None
    return (echeance - (reference or aujourdhui())).days


class Base:
    """Acces SQLite. Une connexion par thread, les ecritures serialisees.

    Le serveur HTTP est multi-thread et un terminal qui bippe vite peut
    declencher plusieurs ecritures rapprochees ; le verrou les met en file.
    A l'echelle d'un frigo, la contention est nulle.
    """

    def __init__(self, chemin: str | Path = BASE_PAR_DEFAUT) -> None:
        self.chemin = Path(chemin)
        self.chemin.parent.mkdir(parents=True, exist_ok=True)
        self._local = threading.local()
        self._verrou = threading.Lock()
        self._troncatures: int | None = None
        with self._verrou:
            self.cx.executescript(SCHEMA)
            self._greffer_colonnes(self.cx)
            self._assainir(self.cx)

    @staticmethod
    def _greffer_colonnes(cx: sqlite3.Connection) -> list[str]:
        """Ajoute les colonnes produit apparues apres coup, sans perdre le stock.

        Les fiches deja en cache n'ont evidemment pas ces champs. Plutot que
        d'afficher du vide pendant les deux mois de validite du cache, on
        perime les fiches Open Food Facts : chacune sera recompletee au premier
        scan du produit, une requete a la fois, sans rafale.
        """
        presentes = {ligne["name"] for ligne in cx.execute("PRAGMA table_info(produit)")}
        greffees = []
        for nom, type_ in COLONNES_AJOUTEES:
            if nom not in presentes:
                cx.execute(f"ALTER TABLE produit ADD COLUMN {nom} {type_}")
                greffees.append(nom)
        if greffees:
            cx.execute("UPDATE produit SET maj = NULL WHERE source = 'openfoodfacts'")

        sur_lot = {ligne["name"] for ligne in cx.execute("PRAGMA table_info(lot)")}
        for nom, type_ in COLONNES_LOT_AJOUTEES:
            if nom not in sur_lot:
                cx.execute(f"ALTER TABLE lot ADD COLUMN {nom} {type_}")
                greffees.append(nom)
        return greffees

    @staticmethod
    def _assainir(cx: sqlite3.Connection) -> int:
        """Supprime les lots a quantite nulle ou negative.

        Le schema neuf l'interdit par contrainte, mais une base creee par une
        version anterieure a pu en accumuler. Un stock negatif ne veut rien
        dire : il n'y a pas moins que rien dans un frigo.
        """
        curseur = cx.execute("DELETE FROM lot WHERE qte IS NULL OR qte <= 0")
        return curseur.rowcount or 0

    # ------------------------------------------------------------- plomberie

    @property
    def cx(self) -> sqlite3.Connection:
        cx = getattr(self._local, "cx", None)
        if cx is None:
            cx = sqlite3.connect(self.chemin, timeout=15.0, isolation_level=None)
            cx.row_factory = sqlite3.Row
            cx.execute("PRAGMA journal_mode=WAL")
            cx.execute("PRAGMA busy_timeout=15000")
            self._local.cx = cx
        return cx

    @contextlib.contextmanager
    def tx(self):
        with self._verrou:
            cx = self.cx
            cx.execute("BEGIN IMMEDIATE")
            try:
                yield cx
            except Exception:
                cx.execute("ROLLBACK")
                raise
            else:
                cx.execute("COMMIT")

    def fermer(self) -> None:
        cx = getattr(self._local, "cx", None)
        if cx is not None:
            cx.close()
            self._local.cx = None

    # ----------------------------------------------------------------- alias

    def alias(self, code_lu: str) -> str | None:
        """Code canonique deja etabli pour ce code lu, s'il y en a un."""
        ligne = self.cx.execute("SELECT code FROM alias WHERE code_lu = ?",
                                (code_lu,)).fetchone()
        return ligne["code"] if ligne else None

    def poser_alias(self, code_lu: str, code: str) -> None:
        if not code_lu or not code or code_lu == code:
            return
        with self.tx() as cx:
            cx.execute("INSERT INTO alias (code_lu, code, pose_le) VALUES (?, ?, ?) "
                       "ON CONFLICT(code_lu) DO UPDATE SET code = excluded.code",
                       (code_lu, code, _maintenant()))
        self._troncatures = None

    def troncatures(self) -> int:
        """Nombre de codes deja vus ampute d'exactement un chiffre.

        Au-dela de deux, on tient pour acquis que le lecteur ne transmet pas
        les cles de controle, et on essaie la completion en premier.
        """
        if self._troncatures is None:
            ligne = self.cx.execute(
                "SELECT COUNT(*) AS n FROM alias "
                "WHERE LENGTH(code) = LENGTH(code_lu) + 1 "
                "AND SUBSTR(code, 1, LENGTH(code_lu)) = code_lu").fetchone()
            self._troncatures = int(ligne["n"])
        return self._troncatures

    # -------------------------------------------------------------- produits

    def produit(self, code: str) -> sqlite3.Row | None:
        return self.cx.execute("SELECT * FROM produit WHERE code = ?", (code,)).fetchone()

    def enregistrer_produit(self, code: str, **champs) -> None:
        """Ecrit ou met a jour la fiche produit. Les champs absents sont ignores."""
        connus = {k: v for k, v in champs.items() if k in CHAMPS_PRODUIT}
        connus["maj"] = _maintenant()
        colonnes = ", ".join(connus)
        marques = ", ".join("?" * len(connus))
        maj = ", ".join(f"{k}=excluded.{k}" for k in connus)
        with self.tx() as cx:
            cx.execute(
                f"INSERT INTO produit (code, {colonnes}) VALUES (?, {marques}) "
                f"ON CONFLICT(code) DO UPDATE SET {maj}",
                (code, *connus.values()),
            )

    def nommer(self, code: str, nom: str) -> None:
        """Libelle saisi a la main : il prime sur Open Food Facts."""
        self.enregistrer_produit(code, nom=nom.strip()[:120] or None, source="manuel")

    def libelle(self, code: str) -> str:
        ligne = self.produit(code)
        if ligne is None or not ligne["nom"]:
            return code
        return ligne["nom"]

    # ------------------------------------------------------------------ lots

    def lots(self, code: str) -> list[sqlite3.Row]:
        """Lots d'un code, du plus urgent au moins urgent, sans date en dernier."""
        return list(self.cx.execute(
            "SELECT * FROM lot WHERE code = ? AND qte > 0 "
            "ORDER BY peremption IS NULL, peremption, id", (code,)))

    def stock(self, code: str) -> int:
        ligne = self.cx.execute(
            "SELECT IFNULL(SUM(qte), 0) AS n FROM lot WHERE code = ? AND qte > 0",
            (code,)).fetchone()
        return int(ligne["n"])

    def ajouter(self, code: str, qte: int = 1, peremption: str | None = None,
                origine: str = "terminal", precision: str = JOUR) -> int:
        """Ajoute des unites. Fusionne avec le lot de meme date s'il existe."""
        if qte <= 0:
            raise ValueError("quantite a ajouter positive attendue")
        peremption = peremption or None
        precision = MOIS if precision == MOIS and peremption else JOUR
        with self.tx() as cx:
            cx.execute(
                "INSERT INTO lot (code, qte, peremption, precision, ajoute_le) "
                "VALUES (?, ?, ?, ?, ?) "
                "ON CONFLICT(code, IFNULL(peremption, '')) "
                "DO UPDATE SET qte = qte + excluded.qte",
                (code, qte, peremption, precision, _maintenant()))
            cx.execute(
                "INSERT INTO journal (ts, code, delta, peremption, action, origine) "
                "VALUES (?, ?, ?, ?, 'ajout', ?)",
                (_maintenant(), code, qte, peremption, origine))
        return self.stock(code)

    def retirer(self, code: str, qte: int = 1, lot_id: int | None = None,
                origine: str = "terminal") -> tuple[int, int]:
        """Sort des unites en FEFO. Retourne (retirees, stock restant)."""
        if qte <= 0:
            raise ValueError("quantite a retirer positive attendue")
        restant = qte
        with self.tx() as cx:
            if lot_id is not None:
                cibles = list(cx.execute(
                    "SELECT * FROM lot WHERE id = ? AND code = ? AND qte > 0",
                    (lot_id, code)))
            else:
                cibles = list(cx.execute(
                    "SELECT * FROM lot WHERE code = ? AND qte > 0 "
                    "ORDER BY peremption IS NULL, peremption, id", (code,)))
            for lot in cibles:
                if restant <= 0:
                    break
                pris = min(restant, int(lot["qte"]))
                restant -= pris
                nouvelle = int(lot["qte"]) - pris
                if nouvelle > 0:
                    cx.execute("UPDATE lot SET qte = ? WHERE id = ?", (nouvelle, lot["id"]))
                else:
                    cx.execute("DELETE FROM lot WHERE id = ?", (lot["id"],))
                cx.execute(
                    "INSERT INTO journal (ts, code, delta, peremption, action, origine) "
                    "VALUES (?, ?, ?, ?, 'retrait', ?)",
                    (_maintenant(), code, -pris, lot["peremption"], origine))
        return qte - restant, self.stock(code)

    def corriger_lot(self, lot_id: int, qte: int | None = None,
                     peremption: str | None = None, origine: str = "web") -> bool:
        """Corrige un lot depuis le tableau de bord. qte = 0 le supprime."""
        with self.tx() as cx:
            lot = cx.execute("SELECT * FROM lot WHERE id = ?", (lot_id,)).fetchone()
            if lot is None:
                return False
            nouvelle_qte = int(lot["qte"]) if qte is None else max(0, qte)
            nouvelle_date = lot["peremption"] if peremption is None else (peremption or None)
            if nouvelle_qte <= 0:
                cx.execute("DELETE FROM lot WHERE id = ?", (lot_id,))
            else:
                # La fusion peut entrer en conflit si la nouvelle date est
                # celle d'un lot existant : on absorbe alors l'autre lot.
                jumeau = cx.execute(
                    "SELECT id, qte FROM lot WHERE code = ? AND IFNULL(peremption,'') = ? "
                    "AND id <> ?", (lot["code"], nouvelle_date or "", lot_id)).fetchone()
                if jumeau is not None:
                    cx.execute("DELETE FROM lot WHERE id = ?", (jumeau["id"],))
                    nouvelle_qte += int(jumeau["qte"])
                cx.execute("UPDATE lot SET qte = ?, peremption = ? WHERE id = ?",
                           (nouvelle_qte, nouvelle_date, lot_id))
            cx.execute(
                "INSERT INTO journal (ts, code, delta, peremption, action, origine) "
                "VALUES (?, ?, ?, ?, 'correction', ?)",
                (_maintenant(), lot["code"], nouvelle_qte - int(lot["qte"]),
                 nouvelle_date, origine))
        return True

    # ------------------------------------------------------------ inventaire

    def inventaire(self, tri: str = "peremption") -> list[dict]:
        """Un poste par code, avec l'echeance la plus proche et le total."""
        ordre = {
            "peremption": "urgence IS NULL, urgence, nom",
            "nom": "nom, urgence",
            "recent": "dernier DESC",
        }.get(tri, "urgence IS NULL, urgence, nom")
        lignes = self.cx.execute(f"""
            SELECT l.code                          AS code,
                   IFNULL(p.nom, l.code)           AS nom,
                   p.marque                        AS marque,
                   p.quantite                      AS quantite,
                   p.nutriscore                    AS nutriscore,
                   p.nova                          AS nova,
                   p.image_url                     AS image_url,
                   SUM(l.qte)                      AS total,
                   MIN(l.peremption)               AS urgence,
                   (SELECT x.precision FROM lot x WHERE x.code = l.code AND x.qte > 0
                     ORDER BY x.peremption IS NULL, x.peremption, x.id LIMIT 1)
                                                   AS precision,
                   COUNT(*)                        AS nb_lots,
                   MAX(l.ajoute_le)                AS dernier
              FROM lot l LEFT JOIN produit p ON p.code = l.code
             WHERE l.qte > 0
             GROUP BY l.code
             ORDER BY {ordre}
        """)
        return [self._poste(ligne) for ligne in lignes]

    def bientot(self, jours: int = 7) -> list[dict]:
        """Lots perimes ou perimant sous N jours, du plus urgent au moins urgent."""
        limite = (aujourdhui() + dt.timedelta(days=jours)).isoformat()
        lignes = self.cx.execute("""
            SELECT l.*, IFNULL(p.nom, l.code) AS nom, p.marque AS marque,
                   p.nutriscore AS nutriscore
              FROM lot l LEFT JOIN produit p ON p.code = l.code
             WHERE l.qte > 0 AND l.peremption IS NOT NULL AND l.peremption <= ?
             ORDER BY l.peremption, nom
        """, (limite,))
        return [{
            "id": ligne["id"], "code": ligne["code"], "nom": ligne["nom"],
            "marque": ligne["marque"] or "", "qte": ligne["qte"],
            "nutriscore": (ligne["nutriscore"] or "").lower(),
            "peremption": ligne["peremption"] or "",
            "precision": ligne["precision"] or JOUR,
            "jours": jours_restants(ligne["peremption"]),
        } for ligne in lignes]

    @staticmethod
    def _poste(ligne: sqlite3.Row) -> dict:
        return {
            "code": ligne["code"],
            "nom": ligne["nom"],
            "marque": ligne["marque"] or "",
            "quantite": ligne["quantite"] or "",
            "nutriscore": (ligne["nutriscore"] or "").lower(),
            "nova": ligne["nova"] or 0,
            "image": 1 if ligne["image_url"] else 0,
            "total": int(ligne["total"]),
            "nb_lots": int(ligne["nb_lots"]),
            "peremption": ligne["urgence"] or "",
            "precision": ligne["precision"] or JOUR,
            "jours": jours_restants(ligne["urgence"]),
        }

    def duree_habituelle(self, code: str) -> int | None:
        """Duree de conservation deja saisie pour ce code, en jours.

        Bipper le meme yaourt chaque semaine et retaper la meme peremption est
        la corvee de l'inventaire. On propose donc la mediane des durees deja
        saisies pour ce code : sur le terminal, il ne reste qu'a valider.
        """
        lignes = self.cx.execute("""
            SELECT ts, peremption FROM journal
             WHERE code = ? AND action = 'ajout' AND peremption IS NOT NULL
             ORDER BY id DESC LIMIT 12
        """, (code,))
        durees = []
        for ligne in lignes:
            try:
                pose = dt.date.fromisoformat(ligne["ts"][:10])
                echeance = dt.date.fromisoformat(ligne["peremption"])
            except (ValueError, TypeError):
                continue
            delta = (echeance - pose).days
            if 0 <= delta <= 3650:
                durees.append(delta)
        if not durees:
            return None
        durees.sort()
        return durees[len(durees) // 2]

    def compteurs(self) -> dict:
        c = self.cx.execute("""
            SELECT (SELECT IFNULL(SUM(qte),0) FROM lot WHERE qte>0)            AS unites,
                   (SELECT COUNT(DISTINCT code) FROM lot WHERE qte>0)          AS references_,
                   (SELECT COUNT(*) FROM lot WHERE qte>0)                      AS lots,
                   (SELECT COUNT(*) FROM produit WHERE source='openfoodfacts') AS fiches,
                   (SELECT COUNT(*) FROM journal)                              AS mouvements
        """).fetchone()
        perimes = self.cx.execute(
            "SELECT IFNULL(SUM(qte),0) AS n FROM lot "
            "WHERE qte>0 AND peremption IS NOT NULL AND peremption < ?",
            (aujourdhui().isoformat(),)).fetchone()
        return {
            "unites": c["unites"], "references": c["references_"], "lots": c["lots"],
            "fiches": c["fiches"], "mouvements": c["mouvements"], "perimes": perimes["n"],
        }
