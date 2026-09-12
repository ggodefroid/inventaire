"""Le site public de l'inventaire, en lecture seule, sur son propre port.

Deuxieme processus a cote de `serveur.py` : celui-la ecrit, celui-ci montre.
Ils ne partagent qu'un fichier SQLite, et rien ne les oblige a demarrer ni a
tomber ensemble.

    lecture.py       SQLite en lecture seule, detection de changement
    mesures.py       les chiffres, des KPI aux curiosites
    veille.py        diffusion WebSocket aux navigateurs connectes
    application.py   les routes Flask
    static/, templates/

Flask n'est importe qu'a l'appel de `creer_app`. Le reste du paquet -- les
mesures, la lecture -- tourne sur une installation Python nue, ce qui permet
de faire tourner toute la suite de tests sans installer quoi que ce soit.

Pour un hebergeur qui attend une application WSGI :

    gunicorn --threads 16 'inventaire.vitrine:creer_app()'

Les threads comptent : une WebSocket occupe le sien tant qu'elle est ouverte.
"""

from __future__ import annotations

__all__ = ["creer_app"]


def creer_app(**reglages):
    """L'application Flask du site. Voir `application.creer_app`."""
    from .application import creer_app as _creer_app
    return _creer_app(**reglages)
