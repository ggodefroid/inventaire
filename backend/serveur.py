#!/usr/bin/env python3
"""Serveur d'inventaire du frigo.

    python3 backend/serveur.py                 # 0.0.0.0:8080
    python3 backend/serveur.py --port 9000 -v

Le terminal Datalogic tape ce serveur en HTTP simple ; un navigateur y trouve
un tableau de bord sur la meme adresse.
"""

from __future__ import annotations

import argparse
import logging
import socket
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from inventaire import VERSION, off                                    # noqa: E402
from inventaire.api import Application, faire_serveur                  # noqa: E402
from inventaire.db import BASE_PAR_DEFAUT, Base                        # noqa: E402
from inventaire.images import (DELAI_PHOTO, PILLOW_DISPONIBLE,          # noqa: E402
                               Vignettes)

CACHE_PAR_DEFAUT = Path(__file__).resolve().parent / "donnees" / "cache"
DIST_PAR_DEFAUT = Path(__file__).resolve().parent.parent / "dist"


def adresses_locales() -> list[str]:
    """Adresses IPv4 utilisables, pour les recopier dans les reglages du terminal."""
    trouvees: list[str] = []
    try:
        for info in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET):
            ip = info[4][0]
            if not ip.startswith("127.") and ip not in trouvees:
                trouvees.append(ip)
    except OSError:
        pass
    if not trouvees:
        # getaddrinfo echoue souvent quand /etc/hosts ne resout pas le nom de
        # machine : une connexion UDP factice donne l'adresse de sortie.
        try:
            with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as s:
                s.connect(("192.0.2.1", 9))          # reseau de documentation
                trouvees.append(s.getsockname()[0])
        except OSError:
            pass
    return trouvees


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--adresse", default="0.0.0.0",
                    help="interface d'ecoute (defaut : toutes)")
    ap.add_argument("--port", type=int, default=8080, help="port TCP (defaut : 8080)")
    ap.add_argument("--db", type=Path, default=BASE_PAR_DEFAUT, help="fichier SQLite")
    ap.add_argument("--cache", type=Path, default=CACHE_PAR_DEFAUT,
                    help="dossier de cache des photos")
    ap.add_argument("--dist", type=Path, default=DIST_PAR_DEFAUT,
                    help="dossier servi sur /telecharger (binaire du terminal)")
    ap.add_argument("--hors-ligne", action="store_true",
                    help="ne jamais interroger Open Food Facts")
    ap.add_argument("--agent", default=off.AGENT,
                    help="User-Agent envoye a Open Food Facts")
    ap.add_argument("--delai-off", type=float, default=8.0,
                    help="delai d'attente sur les fiches Open Food Facts, en secondes")
    ap.add_argument("--delai-photo", type=float, default=DELAI_PHOTO,
                    help="delai d'attente sur les photos (telechargees en tache de fond)")
    ap.add_argument("-v", "--verbeux", action="count", default=0,
                    help="-v : requetes ; -vv : tout")
    args = ap.parse_args(argv)

    logging.basicConfig(
        level=[logging.WARNING, logging.INFO, logging.DEBUG][min(args.verbeux, 2)],
        format="%(asctime)s %(levelname).1s %(name)s: %(message)s",
        datefmt="%H:%M:%S")

    base = Base(args.db)
    vignettes = Vignettes(args.cache, agent=args.agent, delai=args.delai_photo)
    application = Application(base, vignettes, en_ligne=not args.hors_ligne,
                              agent=args.agent, delai_off=args.delai_off,
                              dist=args.dist)

    try:
        serveur = faire_serveur(args.adresse, args.port, application)
    except OSError as exc:
        print(f"impossible d'ecouter sur {args.adresse}:{args.port} : {exc}",
              file=sys.stderr)
        return 1

    print(f"inventaire-frigo {VERSION}")
    print(f"  base      {args.db}")
    print(f"  photos    {'BMP pour le terminal' if PILLOW_DISPONIBLE else 'DESACTIVEES (Pillow absent : pip install pillow)'}")
    print(f"  reseau    {'hors ligne' if args.hors_ligne else 'Open Food Facts actif'}")
    print(f"  ecoute    {args.adresse}:{args.port}")
    for ip in adresses_locales():
        print(f"            http://{ip}:{args.port}/   <- a saisir dans les reglages du terminal")
    print(f"  client    http://<ip>:{args.port}/telecharger  (depuis le navigateur du terminal)")
    print("  Ctrl+C pour arreter.")

    try:
        serveur.serve_forever(poll_interval=0.5)
    except KeyboardInterrupt:
        print("\narret.")
    finally:
        serveur.server_close()
        vignettes.arreter()
        base.fermer()
    return 0


if __name__ == "__main__":
    sys.exit(main())
