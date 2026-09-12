"""Les routes du site public : Flask pour les pages, flask-sock pour le flux.

Rien ici n'ecrit. Les seules methodes declarees sont des GET, la base est
ouverte en `mode=ro` et la connexion porte `PRAGMA query_only` : ce qui est
expose sur Internet ne peut pas vider un frigo, meme en cas de faute de frappe
dans une route. Un test parcourt la table de routage et echoue si un POST y
apparait.
"""

from __future__ import annotations

import hashlib
import logging
import os
import queue
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
