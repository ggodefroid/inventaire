#!/usr/bin/env python3
"""Serveur MCP de l'inventaire : le frigo comme outil pour un LLM local.

    python3 backend/mcp.py                     # 0.0.0.0:8082
    python3 backend/mcp.py --port 9000 -v

Troisieme processus, troisieme port. Il lit la meme base que les deux autres,
en lecture seule, et expose son contenu au Model Context Protocol : un modele
local branche dessus peut demander ce qu'il y a dans le frigo et proposer un
repas en consequence.

A brancher dans un client MCP :

    {"mcpServers": {"frigo": {"type": "http",
                              "url": "http://192.168.1.24:8082/mcp"}}}

Dependances : flask.
"""

from __future__ import annotations

import argparse
import logging
import socket
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from inventaire import VERSION                                       # noqa: E402
from inventaire.mcp import PROTOCOLE                                 # noqa: E402

BASE_PAR_DEFAUT = Path(__file__).resolve().parent / "donnees" / "inventaire.db"
SERVEUR_PAR_DEFAUT = "http://127.0.0.1:8080"


def adresses_locales() -> list[str]:
    trouvees: list[str] = []
    try:
        for info in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET):
            ip = info[4][0]
            if not ip.startswith("127.") and ip not in trouvees:
                trouvees.append(ip)
    except OSError:
        pass
    if not trouvees:
        try:
            with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as s:
                s.connect(("192.0.2.1", 9))
                trouvees.append(s.getsockname()[0])
        except OSError:
            pass
    return trouvees


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--adresse", default="0.0.0.0",
                    help="interface d'ecoute (defaut : toutes)")
    ap.add_argument("--port", type=int, default=8082,
                    help="port TCP (defaut : 8082, apres le serveur et la vitrine)")
    ap.add_argument("--db", type=Path, default=BASE_PAR_DEFAUT,
                    help="base lue par le serveur MCP, jamais ecrite")
    ap.add_argument("--serveur", default=SERVEUR_PAR_DEFAUT,
                    help="serveur du terminal, ou sont relayees les ecritures")
    ap.add_argument("-v", "--verbeux", action="count", default=0,
                    help="-v : requetes ; -vv : tout")
    args = ap.parse_args(argv)

    logging.basicConfig(
        level=[logging.WARNING, logging.INFO, logging.DEBUG][min(args.verbeux, 2)],
        format="%(asctime)s %(levelname).1s %(name)s: %(message)s",
        datefmt="%H:%M:%S")

    if not args.db.is_file():
        print(f"base absente : {args.db}", file=sys.stderr)
        print("  lancez d'abord backend/serveur.py, ou passez --db", file=sys.stderr)
        return 1

    from inventaire.mcp import creer_app
    try:
        app = creer_app(db=args.db, serveur=args.serveur)
    except ImportError as exc:
        print(f"il manque une dependance : {exc}", file=sys.stderr)
        print("  .venv/bin/pip install -r requirements.txt", file=sys.stderr)
        return 1

    print(f"inventaire-frigo mcp {VERSION}")
    print(f"  protocole {PROTOCOLE}")
    print(f"  base      {args.db} (lecture seule)")
    print(f"  ecritures relayees a {args.serveur}")
    print(f"  ecoute    {args.adresse}:{args.port}")
    for ip in adresses_locales():
        print(f"            http://{ip}:{args.port}/mcp   <- a declarer dans le client MCP")
    print("  Ctrl+C pour arreter.")

    try:
        app.run(host=args.adresse, port=args.port, threaded=True,
                debug=False, use_reloader=False)
    except OSError as exc:
        print(f"impossible d'ecouter sur {args.adresse}:{args.port} : {exc}",
              file=sys.stderr)
        return 1
    except KeyboardInterrupt:
        print("\narret.")
    finally:
        app.extensions["frigo"]["lecture"].fermer()
    return 0


if __name__ == "__main__":
    sys.exit(main())
