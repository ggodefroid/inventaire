"""Les routes du site public : Flask pour les pages, flask-sock pour le flux.

**Ce processus n'ecrit jamais dans la base.** Elle est ouverte en `mode=ro` et
la connexion porte `PRAGMA query_only` : ce qui est expose sur Internet ne peut
pas vider un frigo, meme en cas de faute de frappe dans une route. Un test
parcourt la table de routage et verifie qu'aucune route sortant de
`/api/courses/` n'accepte autre chose qu'un GET.

La liste de courses est la seule exception, et elle ne dement pas la regle :
cocher un article sur son telephone en faisant les courses est precisement ce
a quoi sert cet onglet, mais l'ecriture n'a pas lieu ici. La route relaie au
serveur du terminal, qui reste l'unique ecrivain de la base -- meme invariant,
meme fichier, un seul processus qui y touche. Si le serveur est injoignable, la
liste reste consultable et l'interface le dit.

Le stock, lui, n'est joignable par aucune route de ce processus : le site
public ne peut ni ajouter ni retirer une unite du frigo.
"""

from __future__ import annotations

import hashlib
import json
import logging
import os
import queue
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

from flask import Flask, Response, jsonify, render_template, request
from flask_sock import Sock

from .. import VERSION
from ..images import PILLOW_DISPONIBLE, Vignettes
from .lecture import Lecture
from .mesures import masse_unitaire
from .veille import Veille

__all__ = ["creer_app"]

log = logging.getLogger("vitrine")

RACINE = Path(__file__).resolve().parent.parent.parent
BASE_PAR_DEFAUT = RACINE / "donnees" / "inventaire.db"
CACHE_PAR_DEFAUT = RACINE / "donnees" / "cache"
AGENT = f"inventaire-frigo-vitrine/{VERSION} (site public, usage domestique)"

# Une page publique n'a aucune raison de charger quoi que ce soit d'ailleurs :
# tout le CSS et tout le JavaScript sont servis par ce processus.
#
# `style-src` accepte l'inline, et lui seul. Une longueur de jauge, une couleur
# de pastille ou une largeur de barre dependent de la donnee : elles sont
# ecrites dans l'attribut `style`, que `style-src 'self'` refuserait
# silencieusement -- les couleurs disparaissent, sans la moindre erreur
# visible. Ce qui protege vraiment, c'est `script-src 'self'`, qui reste strict.
CSP = ("default-src 'self'; img-src 'self' data:; "
       "style-src 'self' 'unsafe-inline'; script-src 'self'; "
       "connect-src 'self' ws: wss:; frame-ancestors 'none'; "
       "base-uri 'none'; form-action 'none'")

TAILLES_PHOTO = (64, 96, 128, 192, 256, 320)

# Le serveur du terminal, seul ecrivain. Sous compose, `serveur` est le nom du
# service ; hors conteneur, c'est la boucle locale.
SERVEUR_PAR_DEFAUT = "http://127.0.0.1:8080"

# Liste blanche stricte : une action, et les parametres qu'elle accepte. Rien
# d'autre ne franchit le relais -- surtout pas une chaine de requete recopiee
# telle quelle.
ACTIONS_COURSES = {
    "ajouter": ("code", "libelle", "qte", "note"),
    "cocher": ("id", "pris"),
    "retirer": ("id", "qte"),
    "vider": ("tout",),
}
DELAI_RELAIS = 6.0


def creer_app(*, db=None, cache=None, agent: str = AGENT,
              journal_max: int = 120) -> Flask:
    app = Flask(__name__)
    app.config["SEND_FILE_MAX_AGE_DEFAULT"] = 3600

    chemin = Path(db or os.environ.get("FRIGO_DB") or BASE_PAR_DEFAUT)
    dossier_cache = Path(cache or os.environ.get("FRIGO_CACHE") or CACHE_PAR_DEFAUT)

    lecture = Lecture(chemin)
    vignettes = Vignettes(dossier_cache, agent=agent, delai=6.0)
    veille = Veille(lecture)
    veille.demarrer()

    app.extensions["frigo"] = {"lecture": lecture, "veille": veille,
                               "vignettes": vignettes, "journal_max": journal_max}
    sock = Sock(app)

    # ------------------------------------------------------------- en-tetes

    @app.after_request
    def entetes(reponse: Response) -> Response:
        reponse.headers.setdefault("Content-Security-Policy", CSP)
        reponse.headers.setdefault("X-Content-Type-Options", "nosniff")
        reponse.headers.setdefault("Referrer-Policy", "no-referrer")
        reponse.headers.setdefault("X-Frame-Options", "DENY")
        return reponse

    # --------------------------------------------------------------- pages

    @app.get("/")
    def accueil():
        etat = veille.etat()
        return render_template(
            "index.html", version=VERSION,
            unites=etat["compteurs"]["unites"],
            references=etat["compteurs"]["references"],
            maj=etat["horloge"]["iso"])

    @app.get("/sante")
    def sante():
        return jsonify({
            "ok": lecture.existe(),
            "version": VERSION,
            "lecture_seule": lecture.seulement_ro,
            "revision": veille.revision,
            "clients": veille.clients(),
            "photos": PILLOW_DISPONIBLE,
        })

    # ----------------------------------------------------------------- api

    @app.get("/api/etat")
    def api_etat():
        return jsonify(veille.etat())

    @app.get("/api/article/<code>")
    def api_article(code: str):
        propre = "".join(c for c in code if c.isalnum())[:32]
        article = lecture.article(propre)
        if article is None:
            return jsonify({"ok": 0, "erreur": "code inconnu"}), 404
        # La fiche detaillee affiche la nutrition par unite et pour tout le
        # stock : il lui faut la masse, que seul le conditionnement porte.
        grammes, liquide = masse_unitaire(article["contenance"])
        article["masse_unitaire"] = grammes
        article["liquide"] = liquide
        article["ok"] = 1
        return jsonify(article)

    @app.get("/api/journal")
    def api_journal():
        limite = request.args.get("limite", type=int) or journal_max
        return jsonify({"lignes": lecture.journal(max(1, min(limite, 2000)))})

    # ------------------------------------------------------------- courses

    serveur = (os.environ.get("FRIGO_SERVEUR") or SERVEUR_PAR_DEFAUT).rstrip("/")

    @app.get("/api/courses")
    def api_courses():
        return jsonify({"articles": lecture.courses()})

    @app.post("/api/courses/<action>")
    def api_courses_action(action: str):
        """Relais vers le serveur du terminal, seul ecrivain de la base.

        Ce processus ne fait que recopier une poignee de parametres valides
        vers une URL qu'il construit lui-meme. Il n'ouvre aucune connexion en
        ecriture, et le stock du frigo n'est atteignable par aucune des quatre
        actions permises.
        """
        attendus = ACTIONS_COURSES.get(action)
        if attendus is None:
            return jsonify({"ok": 0, "erreur": "action inconnue"}), 404

        parametres = {"origine": "web"}
        for nom in attendus:
            valeur = (request.values.get(nom) or "").strip()
            if valeur:
                parametres[nom] = valeur[:120]

        cible = f"{serveur}/api/courses/{action}?" + urllib.parse.urlencode(parametres)
        requete = urllib.request.Request(cible, headers={"Accept": "application/json"})
        try:
            with urllib.request.urlopen(requete, timeout=DELAI_RELAIS) as reponse:
                charge = json.loads(reponse.read(65536) or b"{}")
        except (urllib.error.URLError, TimeoutError, OSError, ValueError) as exc:
            log.info("relais vers %s injoignable : %s", serveur, exc)
            return jsonify({
                "ok": 0,
                "erreur": "le serveur du terminal ne repond pas ; "
                          "la liste reste consultable",
            }), 503

        # La veille met jusqu'a une seconde a voir le changement dans SQLite.
        # Recalculer tout de suite evite que le navigateur recoive un
        # instantane anterieur a sa propre action.
        veille.rafraichir_et_pousser()
        return jsonify(charge)

    # -------------------------------------------------------------- photos

    @app.get("/photo/<code>")
    def photo(code: str):
        """Photo produit, retaillee et mise en cache.

        Seuls les codes deja presents en base sont servis : un visiteur ne
        peut pas se servir du site pour faire tirer des images arbitraires a
        Open Food Facts.
        """
        propre = "".join(c for c in code if c.isalnum())[:32]
        cote = request.args.get("c", type=int) or 128
        cote = min(TAILLES_PHOTO, key=lambda t: abs(t - cote))
        url = lecture.image_url(propre)
        if url and PILLOW_DISPONIBLE:
            resultat = vignettes.obtenir(propre, url, cote, cote, "png")
            if resultat is not None:
                corps, mime = resultat
                return Response(corps, mimetype=mime, headers={
                    "Cache-Control": "public, max-age=604800, immutable"})
        return Response(_silhouette(propre, cote), mimetype="image/svg+xml",
                        headers={"Cache-Control": "public, max-age=3600"})

    @app.get("/favicon.ico")
    def favicon():
        return Response(FAVICON, mimetype="image/svg+xml",
                        headers={"Cache-Control": "public, max-age=86400"})

    # ----------------------------------------------------------- temps reel

    @sock.route("/flux")
    def flux(ws):
        """Un abonne, un thread, une file. Voir l'en-tete de veille.py."""
        fil = veille.abonner()
        try:
            ws.send(veille.message("init"))
            while True:
                try:
                    message = fil.get(timeout=veille.periode * 20)
                except queue.Empty:
                    message = veille.pouls()
                ws.send(message)
        except Exception:
            pass                      # onglet ferme, reseau coupe : rien a dire
        finally:
            veille.desabonner(fil)

    return app


# ------------------------------------------------------------------ visuels

def _silhouette(code: str, cote: int) -> bytes:
    """Vignette de remplacement quand le produit n'a pas de photo.

    Une case vide dans une grille de produits casse la lecture. On dessine
    donc une boite filaire dont la teinte derive du code barres : deux
    produits sans photo ne se ressemblent pas, et le meme produit garde sa
    couleur d'une visite a l'autre.
    """
    teinte = int(hashlib.blake2b(code.encode(), digest_size=2).hexdigest(), 16) % 360
    barres = "".join(f'<path d="M{x} 52v6"/>' for x in range(20, 46, 3))
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" '
        f'width="{cote}" height="{cote}">'
        f'<rect width="64" height="64" fill="hsl({teinte} 45% 12%)"/>'
        f'<g fill="none" stroke="hsl({teinte} 80% 62%)" stroke-width="1.4" opacity=".85">'
        f'<path d="M18 22h28v26H18z"/><path d="M18 22l6-6h28l-6 6"/>'
        f'<path d="M46 22l6-6v26l-6 6"/><path d="M18 33h28"/></g>'
        f'<g stroke="hsl({teinte} 80% 62%)" stroke-width="1" opacity=".35">{barres}</g>'
        f'</svg>'
    ).encode("utf-8")


FAVICON = (
    b'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">'
    b'<rect width="32" height="32" rx="7" fill="#05090f"/>'
    b'<rect x="9.5" y="5.5" width="13" height="21" rx="2.5" fill="none" '
    b'stroke="#2ff0c8" stroke-width="1.6"/>'
    b'<path d="M9.5 14.5h13" stroke="#2ff0c8" stroke-width="1.6"/>'
    b'<path d="M12.6 9v3.2M12.6 17.6v3.2" stroke="#ff7ad9" stroke-width="1.8" '
    b'stroke-linecap="round"/>'
    b'</svg>'
)
