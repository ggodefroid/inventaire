#!/usr/bin/env python3
"""Handshake de connexion directe de Windows CE, pour l'option `connect` de pppd.

pppd lance ce programme avec le port serie sur son entree et sa sortie
standard. Le terminal, qui se comporte en client d'une liaison directe,
emet la chaine "CLIENT" et attend "CLIENTSERVER" en retour -- la meme
convention que le Direct Cable Connection de Windows. Une fois la reponse
donnee, il enchaine sur PPP et pppd prend la main.

A ne pas confondre avec une reponse "SERVER" seule : le terminal l'ignore
en silence, et rien ne se passe.

Le delai d'attente est volontairement court (quelques secondes). Le terminal
cycle : il enumere sur l'USB, emet CLIENT, abandonne faute de reponse, se
deconnecte, et recommence -- en changeant de numero de port a chaque tour.
Attendre longtemps sur un port donne garantit donc de rater le cycle suivant.
Mieux vaut echouer vite et laisser la boucle rattraper le port frais.
"""

from __future__ import annotations

import os
import sys
import time

ATTENDU = b"CLIENT"
REPONSE = b"CLIENTSERVER"
TIMEOUT = float(os.environ.get("HANDSHAKE_TIMEOUT", "8"))
JOURNAL = os.environ.get("HANDSHAKE_LOG", "/tmp/skorpio-handshake.log")


def journal(message: str) -> None:
    # La sortie standard est le port serie : tout message va sur stderr,
    # que pppd redirige vers son propre journal. Une copie sur disque permet
    # l'analyse apres coup, la fenetre etant trop courte pour lire a l'ecran.
    ligne = f"handshake-ce: {message}"
    print(ligne, file=sys.stderr, flush=True)
    try:
        with open(JOURNAL, "a", encoding="utf-8") as fh:
            fh.write(f"{time.strftime('%H:%M:%S')} {ligne}\n")
    except OSError:
        pass


def main() -> int:
    entree = sys.stdin.buffer.raw if hasattr(sys.stdin.buffer, "raw") else sys.stdin.buffer
    sortie = sys.stdout.buffer

    os.set_blocking(entree.fileno(), False)
    journal(f"attente de {ATTENDU.decode()} pendant {TIMEOUT:.0f} s")

    tampon = bytearray()
    fin = time.monotonic() + TIMEOUT
    while time.monotonic() < fin:
        try:
            chunk = entree.read(64)
        except (BlockingIOError, InterruptedError):
            chunk = None
        except OSError as exc:
            # Le terminal a quitte le socle : inutile d'attendre la fin du
            # delai, il faut rendre la main pour rattraper le port suivant.
            journal(f"port perdu ({exc.strerror}), abandon immediat")
            return 1
        if chunk:
            tampon.extend(chunk)
            journal(f"recu {len(chunk)} octet(s) : {bytes(chunk[:32])!r}")
            if ATTENDU in bytes(tampon):
                sortie.write(REPONSE)
                sortie.flush()
                journal(f"{REPONSE.decode()} envoye, PPP peut demarrer")
                return 0
            if len(tampon) > 4096:
                del tampon[:-256]
        else:
            time.sleep(0.05)

    journal("aucun CLIENT recu : sortez le terminal du socle et reposez-le, "
            "ou debranchez/rebranchez le cable")
    return 1


if __name__ == "__main__":
    sys.exit(main())
