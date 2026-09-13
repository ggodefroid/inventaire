"""Serveur MCP : le frigo comme outil pour un LLM local.

Le Model Context Protocol laisse un modele interroger une source exterieure
lui-meme, plutot que d'attendre qu'on lui recopie les donnees dans son
invite. Branche sur ce serveur, un modele local -- Ollama, LM Studio, n'importe
quel client MCP -- peut demander ce qu'il y a dans le frigo, ce qui perime
jeudi, et proposer un diner en consequence.

    python3 backend/mcp.py                     # 0.0.0.0:8082

Le transport est le **Streamable HTTP** du protocole : du JSON-RPC 2.0 en POST
sur un seul point d'entree, `/mcp`. Pas de session, pas de flux SSE -- une
requete, une reponse. C'est le sous-ensemble le plus simple que la
specification autorise, et il suffit : les outils d'ici rendent tous leur
resultat d'un coup.

Aucune dependance de plus. Le protocole tient en un dictionnaire de methodes
et une enveloppe JSON-RPC ; l'ecrire a la main coute moins cher que d'installer
une pile d'execution asynchrone sur une machine dont le travail est de servir
un terminal de 2005.

**Lecture seule sur la base**, comme la vitrine : meme classe `Lecture`, meme
`mode=ro`, meme `PRAGMA query_only`. Le seul outil qui ecrit -- inscrire un
article sur la liste de courses -- relaie au serveur du terminal, qui reste
l'unique ecrivain. Un modele ne peut donc pas vider un frigo, seulement
proposer d'y remettre quelque chose.
"""

from __future__ import annotations

__all__ = ["creer_app", "PROTOCOLE"]

# Revision du protocole annoncee au client. Un client qui en demande une autre
# recoit celle-ci : la specification laisse le serveur repondre avec la version
# qu'il parle, et au client de decider s'il continue.
PROTOCOLE = "2025-06-18"


def creer_app(**reglages):
    """L'application Flask du serveur MCP. Voir `application.creer_app`."""
    from .application import creer_app as _creer_app
    return _creer_app(**reglages)
