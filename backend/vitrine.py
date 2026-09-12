#!/usr/bin/env python3
"""Site public de l'inventaire, en lecture seule.

    python3 backend/vitrine.py                 # 0.0.0.0:8081
    python3 backend/vitrine.py --port 9090 -v

Deuxieme processus, deuxieme port. `serveur.py` reste seul a ecrire dans la
base : c'est lui que le terminal tape, et lui qui parle a Open Food Facts. Ce
programme-ci ne fait que lire le meme fichier SQLite et le montrer. Les deux
peuvent demarrer, tomber et redemarrer sans se soucier l'un de l'autre.

Dependances : flask et flask-sock.

    python3 -m venv .venv && .venv/bin/pip install -r requirements.txt
    .venv/bin/python backend/vitrine.py
"""

from __future__ import annotations

import argparse
import logging
import socket
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from inventaire import VERSION                                       # noqa: E402
from inventaire.images import PILLOW_DISPONIBLE                      # noqa: E402

BASE_PAR_DEFAUT = Path(__file__).resolve().parent / "donnees" / "inventaire.db"
CACHE_PAR_DEFAUT = Path(__file__).resolve().parent / "donnees" / "cache"


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
    ap.add_argument("--port", type=int, default=8081,
                    help="port TCP (defaut : 8081, pour ne pas marcher sur 8080)")
    ap.add_argument("--db", type=Path, default=BASE_PAR_DEFAUT,
                    help="base lue par le site, jamais ecrite")
    ap.add_argument("--cache", type=Path, default=CACHE_PAR_DEFAUT,
                    help="dossier de cache des photos (partage avec le serveur)")
    ap.add_argument("--journal", type=int, default=120,
                    help="nombre de mouvements envoyes au navigateur")
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

    from inventaire.vitrine import creer_app

    try:
        # Flask n'est importe qu'ici : le reste du paquet tourne sur un Python
        # nu, et c'est ce qui permet de tester les mesures sans rien installer.
        app = creer_app(db=args.db, cache=args.cache, journal_max=args.journal)
    except ImportError as exc:
        print(f"il manque une dependance : {exc}", file=sys.stderr)
        print("  python3 -m venv .venv", file=sys.stderr)
        print("  .venv/bin/pip install -r requirements.txt", file=sys.stderr)
        print("  .venv/bin/python backend/vitrine.py", file=sys.stderr)
        return 1
    frigo = app.extensions["frigo"]
    lecture, veille = frigo["lecture"], frigo["veille"]
    etat = veille.etat()

    print(f"inventaire-frigo vitrine {VERSION}")
    print(f"  base      {args.db} ({'lecture seule' if lecture.seulement_ro else 'query_only'})")
    print(f"  photos    {'actives' if PILLOW_DISPONIBLE else 'DESACTIVEES (Pillow absent)'}")
    print(f"  contenu   {etat['compteurs']['unites']} unites, "
          f"{etat['compteurs']['references']} references")
    print(f"  ecoute    {args.adresse}:{args.port}")
    for ip in adresses_locales():
        print(f"            http://{ip}:{args.port}/")
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
        veille.arreter()
    return 0


if __name__ == "__main__":
    sys.exit(main())
