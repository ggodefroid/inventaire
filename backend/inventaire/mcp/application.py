"""Le transport : JSON-RPC 2.0 sur HTTP, et la table des methodes MCP.

Rien de metier ici. Les outils sont dans `outils.py` ; ce fichier ne fait que
traduire une requete JSON-RPC en appel de fonction et remballer le resultat.
"""

from __future__ import annotations

import logging
import os
from pathlib import Path

from flask import Flask, Response, jsonify, request

from .. import VERSION
from ..vitrine.lecture import Lecture
from . import PROTOCOLE, outils

log = logging.getLogger("mcp")

RACINE = Path(__file__).resolve().parent.parent.parent
BASE_PAR_DEFAUT = RACINE / "donnees" / "inventaire.db"
SERVEUR_PAR_DEFAUT = "http://127.0.0.1:8080"

# Codes d'erreur JSON-RPC 2.0.
ANALYSE, REQUETE, METHODE, PARAMETRES, INTERNE = -32700, -32600, -32601, -32602, -32603

INSTRUCTIONS = """\
Ce serveur donne acces a l'inventaire d'un refrigerateur domestique, releve au
code-barres. Il sert a proposer des repas a partir de ce qui est reellement
disponible, et a eviter le gaspillage.

Commencez par `resume` pour la situation generale, puis `inventaire` pour le
detail. `bientot_perime` donne ce qu'il faut consommer en priorite : une
recette qui l'utilise vaut mieux qu'une recette parfaite.

Vous pouvez inscrire un article manquant sur la liste de courses avec
`ajouter_aux_courses`. C'est la seule action qui modifie quoi que ce soit ;
vous ne pouvez ni ajouter ni retirer quoi que ce soit du frigo lui-meme.

Les dates sont deja calculees en jours restants : fiez-vous a elles plutot
qu'a votre propre idee de la date du jour."""


def _texte(contenu: str) -> dict:
    return {"content": [{"type": "text", "text": contenu}], "isError": False}


def creer_app(*, db=None, serveur: str | None = None) -> Flask:
    app = Flask(__name__)

    chemin = Path(db or os.environ.get("FRIGO_DB") or BASE_PAR_DEFAUT)
    ecrivain = (serveur or os.environ.get("FRIGO_SERVEUR")
                or SERVEUR_PAR_DEFAUT).rstrip("/")
    lecture = Lecture(chemin)
    app.extensions["frigo"] = {"lecture": lecture, "serveur": ecrivain}

    # --------------------------------------------------------------- outils

    OUTILS = [
        {
            "name": "resume",
            "title": "Situation du frigo",
            "description": "Vue d'ensemble en cinq lignes : date du jour, nombre "
                           "d'unites, ce qui perime sous trois jours, taille de la "
                           "liste de courses. A appeler en premier.",
            "inputSchema": {"type": "object", "properties": {}, "additionalProperties": False},
        },
        {
            "name": "inventaire",
            "title": "Tout le contenu du frigo",
            "description": "La liste complete : quantite, nom, marque, "
                           "conditionnement, echeance et Nutri-Score de chaque "
                           "reference en stock.",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "trier": {
                        "type": "string", "enum": ["peremption", "nom"],
                        "description": "Ordre d'affichage. Par defaut, du plus "
                                       "urgent au moins urgent.",
                    },
                },
                "additionalProperties": False,
            },
        },
        {
            "name": "bientot_perime",
            "title": "Ce qu'il faut manger en priorite",
            "description": "Ce qui perime dans les N prochains jours, et ce qui est "
                           "deja perime. La base d'un repas anti-gaspillage.",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "jours": {"type": "integer", "minimum": 0, "maximum": 365,
                              "description": "Horizon en jours. 7 par defaut."},
                },
                "additionalProperties": False,
            },
        },
        {
            "name": "chercher",
            "title": "Chercher un produit dans le frigo",
            "description": "Cherche dans les noms, marques, categories, labels et "
                           "codes-barres du stock. Sert a verifier si un ingredient "
                           "est disponible avant de le mettre dans une recette.",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "terme": {"type": "string", "description": "Mot cherche, par "
                              "exemple « yaourt », « bio » ou « Ferrero »."},
                },
                "required": ["terme"],
                "additionalProperties": False,
            },
        },
        {
            "name": "details_produit",
            "title": "Fiche detaillee d'un produit",
            "description": "Nutrition pour 100 g, ingredients, allergenes, labels, "
                           "origine et lots en stock d'une reference precise.",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "code": {"type": "string", "description": "Code-barres, tel que "
                             "rendu par `inventaire` ou `chercher`."},
                },
                "required": ["code"],
                "additionalProperties": False,
            },
        },
        {
            "name": "liste_courses",
            "title": "Lire la liste de courses",
            "description": "Ce qui reste a acheter, et ce qui est deja au panier.",
            "inputSchema": {"type": "object", "properties": {}, "additionalProperties": False},
        },
        {
            "name": "ajouter_aux_courses",
            "title": "Inscrire un article sur la liste de courses",
            "description": "Ajoute un article manquant. Donnez un libelle en clair, "
                           "ou un code-barres si le produit est deja connu du "
                           "catalogue. Seule action de ce serveur qui modifie "
                           "quelque chose.",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "libelle": {"type": "string", "description": "Nom de l'article, "
                                "par exemple « creme fraiche »."},
                    "code": {"type": "string", "description": "Code-barres, si connu."},
                    "qte": {"type": "integer", "minimum": 1, "maximum": 99},
                },
                "additionalProperties": False,
            },
        },
    ]

    def appeler(nom: str, arguments: dict) -> dict:
        if nom == "resume":
            return _texte(outils.resume(lecture))
        if nom == "inventaire":
            return _texte(outils.inventaire(
                lecture, trier=arguments.get("trier", "peremption")))
        if nom == "bientot_perime":
            return _texte(outils.bientot_perime(
                lecture, jours=int(arguments.get("jours", 7))))
        if nom == "chercher":
            return _texte(outils.chercher(lecture, terme=arguments.get("terme", "")))
        if nom == "details_produit":
            return _texte(outils.details_produit(lecture, code=arguments.get("code", "")))
        if nom == "liste_courses":
            return _texte(outils.liste_courses(lecture))
        if nom == "ajouter_aux_courses":
            return _texte(outils.ajouter_aux_courses(
                lecture, serveur=ecrivain,
                libelle=arguments.get("libelle", ""),
                code=arguments.get("code", ""),
                qte=int(arguments.get("qte", 1))))
        raise KeyError(nom)

    # ------------------------------------------------------------ ressources

    RESSOURCES = [
        {"uri": "frigo://inventaire", "name": "inventaire",
         "title": "Contenu du refrigerateur",
         "description": "Tout le stock, avec les echeances.",
         "mimeType": "text/plain"},
        {"uri": "frigo://courses", "name": "courses",
         "title": "Liste de courses", "mimeType": "text/plain"},
        {"uri": "frigo://urgences", "name": "urgences",
         "title": "Ce qui perime sous sept jours", "mimeType": "text/plain"},
    ]

    def lire_ressource(uri: str) -> str:
        if uri == "frigo://inventaire":
            return outils.inventaire(lecture)
        if uri == "frigo://courses":
            return outils.liste_courses(lecture)
        if uri == "frigo://urgences":
            return outils.bientot_perime(lecture, jours=7)
        raise KeyError(uri)

    # --------------------------------------------------------------- invites

    PROMPTS = [
        {"name": "recette", "title": "Une recette avec ce que j'ai",
         "description": "Propose un plat realisable avec le contenu actuel du "
                        "frigo, en privilegiant ce qui perime bientot.",
         "arguments": [{"name": "repas", "description":
                        "petit-dejeuner, dejeuner, diner, ou gouter",
                        "required": False}]},
        {"name": "menu_semaine", "title": "Un menu pour la semaine",
         "description": "Construit des menus a partir du stock, et liste ce qu'il "
                        "faut acheter en complement.",
         "arguments": [{"name": "personnes", "description":
                        "Nombre de convives", "required": False}]},
        {"name": "anti_gaspi", "title": "Sauver ce qui va perimer",
         "description": "Que faire, ce soir, de ce qui perime dans les trois jours.",
         "arguments": []},
    ]

    GABARITS = {
        "recette": (
            "Propose-moi une recette pour le {repas}, realisable avec ce que "
            "contient mon frigo.\n\n"
            "Appelle d'abord `bientot_perime` puis `inventaire`. Construis la "
            "recette autour de ce qui perime le plus tot. Dis clairement quels "
            "ingredients me manquent, et propose de les inscrire sur ma liste de "
            "courses avec `ajouter_aux_courses` si je le souhaite."),
        "menu_semaine": (
            "Etablis un menu pour la semaine, pour {personnes} personne(s).\n\n"
            "Appelle `inventaire` et `bientot_perime`. Place en debut de semaine "
            "les plats qui utilisent ce qui perime le plus tot. Termine par la "
            "liste de ce qu'il faut acheter, et propose de l'inscrire sur la liste "
            "de courses."),
        "anti_gaspi": (
            "Qu'est-ce que je peux cuisiner ce soir pour ne rien jeter ?\n\n"
            "Appelle `bientot_perime` avec un horizon de trois jours. Propose un "
            "plat simple qui utilise le maximum de ces produits. Si quelque chose "
            "est deja perime, dis-le-moi separement plutot que de l'employer."),
    }

    # ------------------------------------------------------------- methodes

    def methode_initialize(params: dict) -> dict:
        return {
            "protocolVersion": PROTOCOLE,
            "capabilities": {
                "tools": {"listChanged": False},
                "resources": {"listChanged": False, "subscribe": False},
                "prompts": {"listChanged": False},
            },
            "serverInfo": {"name": "inventaire-frigo", "title": "Inventaire du frigo",
                           "version": VERSION},
            "instructions": INSTRUCTIONS,
        }

    def methode_tools_call(params: dict) -> dict:
        nom = params.get("name")
        arguments = params.get("arguments") or {}
        if not isinstance(arguments, dict):
            raise Invalide(PARAMETRES, "arguments doit etre un objet")
        try:
            return appeler(nom, arguments)
        except KeyError:
            raise Invalide(PARAMETRES, f"outil inconnu : {nom}") from None
        except (ValueError, TypeError) as exc:
            # Une erreur d'outil se rend dans le resultat, pas en erreur
            # JSON-RPC : le modele doit pouvoir la lire et se corriger.
            return {"content": [{"type": "text", "text": f"Erreur : {exc}"}],
                    "isError": True}

    def methode_resources_read(params: dict) -> dict:
        uri = params.get("uri") or ""
        try:
            contenu = lire_ressource(uri)
        except KeyError:
            raise Invalide(PARAMETRES, f"ressource inconnue : {uri}") from None
        return {"contents": [{"uri": uri, "mimeType": "text/plain", "text": contenu}]}

    def methode_prompts_get(params: dict) -> dict:
        nom = params.get("name")
        gabarit = GABARITS.get(nom)
        if gabarit is None:
            raise Invalide(PARAMETRES, f"invite inconnue : {nom}")
        arguments = params.get("arguments") or {}
        texte = gabarit.format(repas=arguments.get("repas") or "diner",
                               personnes=arguments.get("personnes") or "2")
        description = next(p["description"] for p in PROMPTS if p["name"] == nom)
        return {"description": description,
                "messages": [{"role": "user",
                              "content": {"type": "text", "text": texte}}]}

    METHODES = {
        "initialize": methode_initialize,
        "ping": lambda params: {},
        "tools/list": lambda params: {"tools": OUTILS},
        "tools/call": methode_tools_call,
        "resources/list": lambda params: {"resources": RESSOURCES},
        "resources/templates/list": lambda params: {"resourceTemplates": []},
        "resources/read": methode_resources_read,
        "prompts/list": lambda params: {"prompts": PROMPTS},
        "prompts/get": methode_prompts_get,
    }

    # -------------------------------------------------------------- routage

    def traiter(message: dict):
        """Un message JSON-RPC. Rend la reponse, ou None pour une notification."""
        if not isinstance(message, dict) or message.get("jsonrpc") != "2.0":
            return _erreur(None, REQUETE, "enveloppe JSON-RPC 2.0 attendue")
        identifiant = message.get("id")
        nom = message.get("methode") or message.get("method")
        params = message.get("params") or {}

        # Une notification n'a pas d'identifiant et n'attend aucune reponse.
        if identifiant is None:
            if nom and nom.startswith("notifications/"):
                return None
            return None

        fonction = METHODES.get(nom)
        if fonction is None:
            return _erreur(identifiant, METHODE, f"methode inconnue : {nom}")
        try:
            return {"jsonrpc": "2.0", "id": identifiant, "result": fonction(params)}
        except Invalide as exc:
            return _erreur(identifiant, exc.code, exc.message)
        except Exception as exc:                      # pragma: no cover
            log.exception("methode %s", nom)
            return _erreur(identifiant, INTERNE, str(exc))

    @app.post("/mcp")
    def point_entree():
        try:
            message = request.get_json(force=True)
        except Exception:
            return jsonify(_erreur(None, ANALYSE, "JSON illisible")), 400

        # Un lot : une liste de messages, une liste de reponses.
        if isinstance(message, list):
            reponses = [r for r in (traiter(m) for m in message) if r is not None]
            if not reponses:
                return Response(status=202)
            return jsonify(reponses)

        reponse = traiter(message)
        if reponse is None:
            return Response(status=202)      # notification : rien a rendre
        return jsonify(reponse)

    @app.get("/mcp")
    def pas_de_flux():
        """Le transport accepte un flux SSE ; celui-ci n'en ouvre pas.

        La specification permet au serveur de refuser : tous les outils d'ici
        repondent d'un coup, aucun n'a de progression a diffuser.
        """
        return jsonify(_erreur(None, REQUETE, "ce serveur ne diffuse pas de flux")), 405

    @app.get("/sante")
    def sante():
        return jsonify({"ok": lecture.existe(), "version": VERSION,
                        "protocole": PROTOCOLE, "lecture_seule": lecture.seulement_ro,
                        "outils": len(OUTILS), "serveur": ecrivain})

    @app.after_request
    def entetes(reponse: Response) -> Response:
        reponse.headers.setdefault("MCP-Protocol-Version", PROTOCOLE)
        reponse.headers.setdefault("X-Content-Type-Options", "nosniff")
        return reponse

    return app


class Invalide(Exception):
    def __init__(self, code: int, message: str) -> None:
        super().__init__(message)
        self.code = code
        self.message = message


def _erreur(identifiant, code: int, message: str) -> dict:
    return {"jsonrpc": "2.0", "id": identifiant,
            "error": {"code": code, "message": message}}
