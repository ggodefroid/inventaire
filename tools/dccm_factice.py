#!/usr/bin/env python3
"""Faux service ActiveSync, pour empecher le terminal de couper la liaison PPP.

Une fois PPP etabli, ActiveSync du terminal se connecte au port TCP 5679 du PC
pour negocier ses services de synchronisation. Si personne n'ecoute, il recoit
un refus de connexion et considere souvent la liaison comme inutilisable : PPP
tombe au bout de quelques secondes.

Ce programme se contente d'accepter la connexion et de la maintenir ouverte,
sans rien repondre. On ne cherche pas a parler ActiveSync -- on veut seulement
que la liaison IP reste debout, puisque c'est elle qui nous interesse : notre
protocole passe en TCP sur le port 9101, et le navigateur du terminal peut
telecharger le client en HTTP.
"""

from __future__ import annotations

import argparse
import socket
import socketserver
import threading

PORT_ACTIVESYNC = 5679


class Garde(socketserver.BaseRequestHandler):
    def handle(self) -> None:
        pair = self.client_address
        print(f"  ActiveSync s'est connecte depuis {pair[0]}:{pair[1]} "
              f"- connexion maintenue", flush=True)
        self.request.settimeout(None)
        try:
            while True:
                data = self.request.recv(4096)
                if not data:
                    break
                print(f"  {len(data)} octet(s) recu(s) d'ActiveSync (ignores)", flush=True)
        except (OSError, socket.timeout):
            pass
        print(f"  {pair[0]} s'est deconnecte", flush=True)


class Serveur(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--hote", default="0.0.0.0")
    ap.add_argument("--port", type=int, default=PORT_ACTIVESYNC)
    args = ap.parse_args()

    with Serveur((args.hote, args.port), Garde) as srv:
        print(f"  ecoute ActiveSync factice sur {args.hote}:{args.port}", flush=True)
        print("  Ctrl-C pour arreter.", flush=True)
        try:
            srv.serve_forever()
        except KeyboardInterrupt:
            print("\n  arrete.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
