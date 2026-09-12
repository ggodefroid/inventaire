"""Couche HTTP : routage, parametres, reponses.

Deux clients tres inegaux consomment cette API.

Le terminal, d'abord, pour qui tout est fait : il demande `fmt=kv`, il ne lit
que des codes HTTP 200, et il attend des reponses courtes. Une erreur metier
-- code inconnu, stock insuffisant -- revient donc en 200 avec `ok=0`, jamais
en 4xx : sous Compact Framework 2.0, un code d'erreur leve une WebException,
et distinguer « le serveur dit non » de « le serveur est tombe » demanderait
du code fragile sur le terminal. Les 4xx sont reserves aux vraies erreurs de
protocole (route inconnue, parametre absent).

Le navigateur, ensuite, qui recoit du JSON indente et le tableau de bord.

Chaque reponse porte `aujourdhui` : l'horloge d'un terminal Windows CE derive
et se remet a zero au *cold boot*. Le client cale donc son calendrier sur celle
du serveur plutot que sur la sienne, et la saisie « J+7 » reste juste meme
quand le terminal se croit en 2005.
"""

from __future__ import annotations

import datetime as dt
import http.server
import logging
import socketserver
import urllib.parse
from pathlib import Path

from . import VERSION, codebarres, off, web
from .db import JOUR, MOIS, Base, aujourdhui, fin_de_mois, jours_restants
from .images import PILLOW_DISPONIBLE, Vignettes
from .rendu import rendre

log = logging.getLogger("inventaire.api")

LARGEUR_VIGNETTE = 88
HAUTEUR_VIGNETTE = 88


class Erreur(Exception):
    """Erreur metier : rendue en 200 avec ok=0, le terminal sait la lire."""


class Application:
    """Le comportement du serveur, independamment du transport HTTP."""

    # Fichiers que le terminal peut retirer lui-meme, par son navigateur.
    # Liste blanche explicite : le dossier dist/ n'est pas une racine web.
    LIVRABLES = ("Inventaire.exe", "inventaire.ini", "version.txt")

    def __init__(self, base: Base, vignettes: Vignettes, *, en_ligne: bool = True,
                 agent: str = off.AGENT, delai_off: float = 8.0,
                 dist: Path | None = None) -> None:
        self.base = base
        self.vignettes = vignettes
        self.en_ligne = en_ligne
        self.agent = agent
        self.delai_off = delai_off
        self.dist = Path(dist) if dist else None
        self.routes = {
            "/api/ping": self.ping,
            "/api/maj": self.maj,
            "/api/scan": self.scan,
            "/api/detail": self.detail,
            "/api/ajouter": self.ajouter,
            "/api/retirer": self.retirer,
            "/api/lot": self.lot,
            "/api/nommer": self.nommer,
            "/api/inventaire": self.inventaire,
            "/api/bientot": self.bientot,
        }

    # ------------------------------------------------------------- outillage

    def _codes(self, p: dict) -> tuple[str, str]:
        """(ce que le terminal a envoye, le code canonique a utiliser).

        Sans reseau : correspondance deja etablie, sinon forme deja connue en
        base, sinon arithmetique. Les ecritures -- ajout, retrait -- ne doivent
        jamais dependre du catalogue.
        """
        lu = "".join(c for c in p.get("code", "") if c.isdigit() or c.isalpha())
        if not lu:
            raise Erreur("code-barres absent")
        if len(lu) > 32:
            raise Erreur("code-barres trop long")

        connu = self.base.alias(lu)
        if connu:
            return lu, connu
        for essai in codebarres.variantes(lu, self.base.troncatures() >= 2):
            fiche = self.base.produit(essai)
            if self.base.stock(essai) > 0 or (fiche is not None and fiche["nom"]):
                return lu, essai        # cette forme-la existe deja chez nous
        return lu, codebarres.reparer(lu)

    def _code(self, p: dict) -> str:
        return self._codes(p)[1]

    def _resoudre(self, lu: str, forcer: bool = False) -> tuple[dict, str]:
        """Fiche et code canonique, en retenant la correspondance trouvee."""
        fiche, code = off.resoudre(self.base, lu, en_ligne=self.en_ligne,
                                   forcer=forcer, delai=self.delai_off,
                                   agent=self.agent)
        self.base.poser_alias(lu, code)
        return fiche, code

    @staticmethod
    def _entier(p: dict, cle: str, defaut: int, mini: int, maxi: int) -> int:
        brut = (p.get(cle) or "").strip()
        if not brut:
            return defaut
        try:
            valeur = int(brut)
        except ValueError:
            raise Erreur(f"{cle} : nombre attendu") from None
        return max(mini, min(valeur, maxi))

    @staticmethod
    def _peremption(p: dict) -> tuple[str | None, str]:
        """(date ISO, precision). Accepte trois formes, plus 'jours=N'.

        Beaucoup d'emballages ne portent qu'un mois : « a consommer de
        preference avant fin 10/2026 ». Forcer un jour dans ce cas serait
        inventer une precision que le produit n'a pas. On accepte donc
        'AAAA-MM', qu'on range au dernier jour du mois -- c'est bien ce que
        la mention veut dire -- en retenant que seul le mois etait connu.
        """
        brut = (p.get("peremption") or "").strip()
        if brut:
            if len(brut) == 7 and brut[4] == "-":
                try:
                    annee, mois = int(brut[:4]), int(brut[5:])
                    if not 1 <= mois <= 12 or not 1970 <= annee <= 2999:
                        raise ValueError
                    return fin_de_mois(annee, mois), MOIS
                except ValueError:
                    raise Erreur("peremption : AAAA-MM invalide") from None
            try:
                return dt.date.fromisoformat(brut).isoformat(), JOUR
            except ValueError:
                raise Erreur("peremption : AAAA-MM-JJ ou AAAA-MM attendu") from None
        relatif = (p.get("jours") or "").strip()
        if relatif:
            try:
                delta = int(relatif)
            except ValueError:
                raise Erreur("jours : nombre attendu") from None
            if not -3650 <= delta <= 3650:
                raise Erreur("jours hors bornes")
            return (aujourdhui() + dt.timedelta(days=delta)).isoformat(), JOUR
        return None, JOUR

    def _enveloppe(self, contenu: dict) -> dict:
        maintenant = dt.datetime.now()
        return {
            "ok": 1,
            "aujourdhui": maintenant.date().isoformat(),
            # Le terminal affiche cette heure-la, pas la sienne : l'horloge d'un
            # Windows CE derive et repart a zero au cold boot. Avec les
            # secondes, il peut extrapoler entre deux appels sans deriver.
            "heure": maintenant.strftime("%H:%M:%S"),
            **contenu,
        }

    # ---------------------------------------------------------------- routes

    def ping(self, p: dict) -> dict:
        return self._enveloppe({
            "version": VERSION,
            "en_ligne": self.en_ligne,
            "images": PILLOW_DISPONIBLE,
            "compteurs": self.base.compteurs(),
            "cache": self.vignettes.memoire.compteurs(),
        })

    def maj(self, p: dict) -> dict:
        """Ce que dist/ contient, pour que le terminal sache s'il est a jour.

        L'empreinte est celle des sources du client, calculee a la
        compilation : reconstruire sans avoir rien change ne declenche donc
        aucune mise a jour inutile.
        """
        version, taille = "", 0
        if self.dist is not None:
            marque = self.dist / "version.txt"
            if marque.is_file():
                version = marque.read_text(encoding="utf-8").strip()[:40]
            binaire = self.dist / "Inventaire.exe"
            if binaire.is_file():
                taille = binaire.stat().st_size
        return self._enveloppe({
            "version": version,
            "taille": taille,
            "nom": "Inventaire.exe",
            "disponible": 1 if version and taille else 0,
        })

    def scan(self, p: dict) -> dict:
        """Tout ce que le terminal doit afficher apres un bip, en un aller-retour."""
        lu, _ = self._codes(p)
        fiche, code = self._resoudre(lu, p.get("forcer") == "1")
        vue = self._vue(code, fiche)
        vue["code_lu"] = lu
        return self._enveloppe(vue)

    def detail(self, p: dict) -> dict:
        """Tout ce qu'Open Food Facts dit du produit.

        Route separee du scan a dessein : la liste d'ingredients pese a elle
        seule plus que toute la reponse de scan, et le terminal n'en a besoin
        que si l'utilisateur demande la fiche detaillee. Le chemin chaud reste
        court.
        """
        lu, _ = self._codes(p)
        fiche, code = self._resoudre(lu)
        return self._enveloppe({
            "code": code,
            "connu": 1 if fiche.get("nom") else 0,
            "source": fiche.get("source") or "inconnu",
            "nom": fiche.get("nom") or "",
            "marque": fiche.get("marque") or "",
            "contenance": fiche.get("quantite") or "",
            "portion": fiche.get("portion") or "",
            "nutriscore": (fiche.get("nutriscore") or "").lower(),
            "nova": fiche.get("nova") or 0,
            "ecoscore": (fiche.get("ecoscore") or "").lower(),
            "niveaux": {
                "graisses": fiche.get("niv_graisses") or "",
                "satures": fiche.get("niv_satures") or "",
                "sucres": fiche.get("niv_sucres") or "",
                "sel": fiche.get("niv_sel") or "",
            },
            "nutrition": {
                "kcal": fiche.get("kcal"), "kj": fiche.get("energie_kj"),
                "lipides": fiche.get("lipides"), "satures": fiche.get("satures"),
                "glucides": fiche.get("glucides"), "sucres": fiche.get("sucres"),
                "fibres": fiche.get("fibres"), "proteines": fiche.get("proteines"),
                "sel": fiche.get("sel"),
            },
            "allergenes": fiche.get("allergenes") or "",
            "traces": fiche.get("traces") or "",
            "additifs": fiche.get("additifs") or "",
            "labels": fiche.get("labels") or "",
            "categories": fiche.get("categories") or "",
            "origine": fiche.get("origine") or "",
            "ingredients": fiche.get("ingredients") or "",
        })

    def _vue(self, code: str, fiche: dict | None = None) -> dict:
        if fiche is None:
            fiche = dict(self.base.produit(code) or {"source": "inconnu"})
        lots = self.base.lots(code)
        urgence = lots[0]["peremption"] if lots else None
        source = fiche.get("source") or "inconnu"
        return {
            "code": code,
            "connu": 1 if fiche.get("nom") else 0,
            "source": source,
            "nom": fiche.get("nom") or "",
            "marque": fiche.get("marque") or "",
            "contenance": fiche.get("quantite") or "",
            "image": 1 if (fiche.get("image_url") and PILLOW_DISPONIBLE) else 0,
            "nutriscore": (fiche.get("nutriscore") or "").lower(),
            "nova": fiche.get("nova") or 0,
            "ecoscore": (fiche.get("ecoscore") or "").lower(),
            "niveaux": {
                "graisses": fiche.get("niv_graisses") or "",
                "satures": fiche.get("niv_satures") or "",
                "sucres": fiche.get("niv_sucres") or "",
                "sel": fiche.get("niv_sel") or "",
            },
            "nutrition": {
                "kcal": fiche.get("kcal"), "lipides": fiche.get("lipides"),
                "satures": fiche.get("satures"), "glucides": fiche.get("glucides"),
                "sucres": fiche.get("sucres"), "proteines": fiche.get("proteines"),
                "sel": fiche.get("sel"), "fibres": fiche.get("fibres"),
            },
            "allergenes": fiche.get("allergenes") or "",
            "traces": fiche.get("traces") or "",
            "labels": fiche.get("labels") or "",
            "stock": self.base.stock(code),
            "peremption": urgence or "",
            "jours": jours_restants(urgence),
            "suggestion": self.base.duree_habituelle(code),
            "precision": (lots[0]["precision"] or JOUR) if lots else JOUR,
            "lots": [{
                "id": lot["id"], "qte": lot["qte"],
                "peremption": lot["peremption"] or "",
                "precision": lot["precision"] or JOUR,
                "jours": jours_restants(lot["peremption"]),
            } for lot in lots],
        }

    def ajouter(self, p: dict) -> dict:
        lu, _ = self._codes(p)
        qte = self._entier(p, "qte", 1, 1, 999)
        peremption, precision = self._peremption(p)
        # Etablir le code avant d'ecrire : sans cela, un produit dont la bonne
        # lecture n'est pas encore connue irait grossir un stock fantome.
        # resoudre ne leve jamais et retombe sur l'arithmetique hors ligne,
        # donc ranger une course ne depend pas du reseau.
        fiche, code = self._resoudre(lu)
        self.base.ajouter(code, qte, peremption, origine=p.get("origine", "terminal"),
                          precision=precision)
        vue = self._vue(code, fiche)
        vue["code_lu"] = lu
        vue["message"] = f"+{qte} {vue['nom'] or code}"
        return self._enveloppe(vue)

    def retirer(self, p: dict) -> dict:
        lu, code = self._codes(p)
        qte = self._entier(p, "qte", 1, 1, 999)
        lot_id = self._entier(p, "lot", 0, 0, 2 ** 31) or None
        if self.base.stock(code) == 0:
            raise Erreur("rien en stock pour ce code")
        retires, _ = self.base.retirer(code, qte, lot_id,
                                       origine=p.get("origine", "terminal"))
        if retires == 0:
            raise Erreur("aucune unite retiree")
        vue = self._vue(code)
        vue["code_lu"] = lu
        vue["retires"] = retires
        vue["message"] = f"-{retires} {vue['nom'] or code}"
        return self._enveloppe(vue)

    def lot(self, p: dict) -> dict:
        lot_id = self._entier(p, "lot", 0, 1, 2 ** 31)
        qte = None if (p.get("qte") or "") == "" else self._entier(p, "qte", 0, 0, 999)
        peremption = None
        if "peremption" in p or "jours" in p:
            peremption = self._peremption(p)[0] or ""
        if not self.base.corriger_lot(lot_id, qte, peremption,
                                      origine=p.get("origine", "web")):
            raise Erreur("lot introuvable")
        code = (p.get("code") or "").strip()
        return self._enveloppe(self._vue(code) if code else {"lot": lot_id})

    def nommer(self, p: dict) -> dict:
        code = self._code(p)
        nom = (p.get("nom") or "").strip()
        if not nom:
            raise Erreur("nom absent")
        self.base.nommer(code, nom)
        return self._enveloppe(self._vue(code))

    def inventaire(self, p: dict) -> dict:
        postes = self.base.inventaire(p.get("tri", "peremption"))
        limite = self._entier(p, "limite", 0, 0, 5000)
        depuis = self._entier(p, "depuis", 0, 0, 100000)
        total = len(postes)
        tranche = postes[depuis:depuis + limite] if limite else postes[depuis:]
        return self._enveloppe({
            "total": total, "depuis": depuis,
            "compteurs": self.base.compteurs(), "postes": tranche,
        })

    def bientot(self, p: dict) -> dict:
        jours = self._entier(p, "jours", 7, -365, 3650)
        lignes = self.base.bientot(jours)
        return self._enveloppe({"jours": jours, "lignes": lignes})

    # ----------------------------------------------------------- livraison

    def livrable(self, nom: str) -> tuple[bytes, str] | None:
        """Contenu d'un fichier de dist/, pour le navigateur du terminal.

        Une fois le terminal sur le Wi-Fi, c'est la voie la plus simple pour
        lui pousser une nouvelle version : plus de carte memoire a sortir.
        """
        if self.dist is None or nom not in self.LIVRABLES:
            return None
        fichier = self.dist / nom
        if not fichier.is_file():
            return None
        mime = ("application/octet-stream" if nom.endswith(".exe")
                else "text/plain; charset=utf-8")
        return fichier.read_bytes(), mime

    def inventaire_livrables(self) -> list[dict]:
        resultat = []
        for nom in self.LIVRABLES:
            fichier = self.dist / nom if self.dist else None
            if fichier is not None and fichier.is_file():
                resultat.append({"nom": nom, "taille": fichier.stat().st_size})
        return resultat

    # -------------------------------------------------------------- vignette

    def image(self, p: dict) -> tuple[bytes, str] | None:
        code = self._code(p)
        fiche = self.base.produit(code)
        if fiche is None or not fiche["image_url"]:
            lu, _ = self._codes(p)
            fiche, code = self._resoudre(lu)
            url = fiche.get("image_url")
        else:
            url = fiche["image_url"]
        if not url:
            return None
        return self.vignettes.obtenir(
            code, url,
            self._entier(p, "l", LARGEUR_VIGNETTE, 16, 480),
            self._entier(p, "h", HAUTEUR_VIGNETTE, 16, 480),
            (p.get("fmt_image") or p.get("img") or "bmp").lower())


class Gestionnaire(http.server.BaseHTTPRequestHandler):
    """Traduction HTTP <-> Application. Rien de metier ici."""

    protocol_version = "HTTP/1.1"
    server_version = "inventaire-frigo/" + VERSION
    sys_version = ""

    def version_string(self) -> str:
        return self.server_version          # sans le " " + sys_version de la base
    application: Application = None                  # pose par faire_serveur

    # ------------------------------------------------------------- reponses

    def _envoyer(self, statut: int, corps: bytes, mime: str,
                 entetes: dict | None = None) -> None:
        self.send_response(statut)
        self.send_header("Content-Type", mime)
        self.send_header("Content-Length", str(len(corps)))
        entetes = entetes or {}
        # Les reponses metier ne se gardent jamais ; une vignette, si. Le
        # defaut ne s'applique donc que si l'appelant n'a rien dit.
        if "Cache-Control" not in entetes:
            self.send_header("Cache-Control", "no-store")
        for cle, valeur in entetes.items():
            self.send_header(cle, valeur)
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(corps)

    def _donnees(self, objet, format_: str, statut: int = 200) -> None:
        corps, mime = rendre(objet, format_)
        self._envoyer(statut, corps, mime)

    # -------------------------------------------------------------- entrees

    def _parametres(self) -> tuple[str, dict]:
        decoupe = urllib.parse.urlsplit(self.path)
        params = {cle: valeurs[-1] for cle, valeurs
                  in urllib.parse.parse_qs(decoupe.query, keep_blank_values=True).items()}
        if self.command == "POST":
            taille = int(self.headers.get("Content-Length") or 0)
            if 0 < taille <= 64 * 1024:
                brut = self.rfile.read(taille).decode("utf-8", "replace")
                params.update({cle: valeurs[-1] for cle, valeurs
                               in urllib.parse.parse_qs(brut, keep_blank_values=True).items()})
        return urllib.parse.unquote(decoupe.path), params

    def _traiter(self) -> None:
        chemin, params = self._parametres()
        app = self.application
        # Le terminal demande kv ; tout le reste recoit du JSON.
        format_ = "kv" if params.get("fmt") == "kv" else "json"

        if chemin in ("/", "/index.html"):
            corps = web.page(app).encode("utf-8")
            return self._envoyer(200, corps, "text/html; charset=utf-8")
        if chemin == "/favicon.ico":
            return self._envoyer(200, web.FAVICON, "image/svg+xml")
        if chemin == "/telecharger":
            corps = web.page_telechargement(app).encode("utf-8")
            return self._envoyer(200, corps, "text/html; charset=utf-8")
        if chemin.startswith("/telecharger/"):
            fichier = app.livrable(chemin[len("/telecharger/"):])
            if fichier is None:
                return self._envoyer(404, b"fichier absent de dist/",
                                     "text/plain; charset=utf-8")
            corps, mime = fichier
            return self._envoyer(200, corps, mime, {
                "Content-Disposition": "attachment"})
        if chemin == "/api/image":
            try:
                resultat = app.image(params)
            except Erreur as exc:
                return self._donnees({"ok": 0, "erreur": str(exc)}, format_)
            if resultat is None:
                return self._envoyer(404, b"pas d'image", "text/plain; charset=utf-8")
            corps, mime = resultat
            # Une vignette ne change pas pour un couple (code, taille) donne :
            # autant laisser le navigateur la garder. Le terminal, lui, tient
            # son propre cache en memoire.
            return self._envoyer(200, corps, mime,
                                 {"Cache-Control": "public, max-age=86400"})

        route = app.routes.get(chemin)
        if route is None:
            return self._donnees({"ok": 0, "erreur": "route inconnue : " + chemin},
                                 format_, 404)
        try:
            return self._donnees(route(params), format_)
        except Erreur as exc:
            # 200 volontaire : cf. l'en-tete du module.
            return self._donnees({"ok": 0, "erreur": str(exc),
                                  "aujourdhui": aujourdhui().isoformat()}, format_)
        except Exception:
            log.exception("echec sur %s", chemin)
            return self._donnees({"ok": 0, "erreur": "erreur interne du serveur"},
                                 format_, 500)

    def do_GET(self) -> None:
        self._traiter()

    def do_HEAD(self) -> None:
        self._traiter()

    def do_POST(self) -> None:
        self._traiter()

    # ------------------------------------------------------------- journal

    def log_message(self, format_: str, *args) -> None:
        log.info("%s %s", self.address_string(), format_ % args)

    def log_error(self, format_: str, *args) -> None:
        log.warning("%s %s", self.address_string(), format_ % args)


class Serveur(socketserver.ThreadingMixIn, http.server.HTTPServer):
    daemon_threads = True
    allow_reuse_address = True
    # Le terminal peut mourir en plein appel (batterie, sortie de portee) :
    # une requete a moitie lue ne doit pas immobiliser un thread.
    timeout = 30


def faire_serveur(adresse: str, port: int, application: Application) -> Serveur:
    gestionnaire = type("GestionnaireLie", (Gestionnaire,), {"application": application})
    return Serveur((adresse, port), gestionnaire)
