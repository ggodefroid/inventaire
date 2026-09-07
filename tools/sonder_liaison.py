#!/usr/bin/env python3
"""Ecoute la liaison serie du terminal pour savoir ce qui parle a l'autre bout.

Une fois /dev/ttyUSB0 apparu, il reste une inconnue : le port USB du terminal
est-il pilote par ActiveSync, qui y parle son propre protocole binaire, ou est-il
en mode serie brut, ou notre client pourra dialoguer ?

Le diagnostic est simple : on ecoute sans rien emettre, puis on envoie un PING
de notre protocole. Trois issues possibles.

    silence total          personne n'ecoute cote terminal : soit le client
                           n'est pas lance, soit le port n'est pas le bon.
    octets non imprimables ActiveSync accapare le tube. Il faut basculer la
                           connexion PC du terminal en mode serie (docs/02 §2.3).
    lignes lisibles        la liaison est bonne.
"""

from __future__ import annotations

import argparse
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

try:
    import serial
except ImportError:
    raise SystemExit("pyserial requis : pip install --user pyserial")

from frigo.protocol import LINE_END, encode


def dump(data: bytes, prefix: str = "    ") -> None:
    for i in range(0, len(data), 16):
        row = data[i:i + 16]
        hexa = " ".join(f"{b:02X}" for b in row).ljust(47)
        texte = "".join(chr(b) if 32 <= b < 127 else "." for b in row)
        print(f"{prefix}{i:04X}  {hexa}  |{texte}|")


def verdict(data: bytes) -> str:
    if not data:
        return ("SILENCE. Personne n'emet cote terminal. C'est l'etat normal si le\n"
                "  client n'est pas lance, ou s'il ecoute un autre port COM.\n"
                "  Lancez SkorpioFrigo.exe sur le terminal, puis Actions > Envoyer au PC,\n"
                "  et relancez cette sonde. Si le silence persiste, essayez les autres\n"
                "  ports proposes par Outils > Diagnostic ports.")
    imprimables = sum(1 for b in data if 32 <= b < 127 or b in (10, 13, 9))
    ratio = imprimables / len(data)
    texte = data.decode("utf-8", "replace")
    if ratio > 0.9 and any(v in texte for v in ("SCAN", "HELLO", "PONG", "READY", "BYE")):
        return "PROTOCOLE RECONNU. La liaison est bonne, le serveur peut ingerer."
    if ratio > 0.9:
        return ("TEXTE LISIBLE, mais aucun verbe de notre protocole. Quelque chose\n"
                "  d'autre emet sur ce port (un shell, une appli du terminal).")
    return ("OCTETS BINAIRES. C'est la signature d'ActiveSync, qui accapare le tube\n"
            "  USB et y parle son propre protocole. Il faut basculer la connexion PC\n"
            "  du terminal en mode serie : voir docs/02-liaison-usb.md section 2.3.")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("port", nargs="?",
                    help="port serie (defaut : detecte par VID/PID)")
    ap.add_argument("--bauds", type=int, default=115200)
    ap.add_argument("--ecoute", type=float, default=4.0,
                    help="duree d'ecoute passive, en secondes")
    ap.add_argument("--sans-ping", action="store_true",
                    help="ne rien emettre du tout")
    args = ap.parse_args()

    if not args.port:
        # Le numero du port change a chaque redockage : on le resout.
        from frigo.transports.serial_link import find_datalogic_port
        import logging
        logging.disable(logging.CRITICAL)
        args.port = find_datalogic_port()
        if not args.port:
            print("  aucun terminal Datalogic detecte.")
            print("  Posez-le sur son socle, puis : ./inventaire.py ports")
            return 2

    print(f"  ouverture de {args.port} a {args.bauds} bauds")
    try:
        link = serial.Serial(
            args.port, baudrate=args.bauds, bytesize=8,
            parity=serial.PARITY_NONE, stopbits=1,
            timeout=0.3, write_timeout=3.0, rtscts=False, dsrdtr=False,
        )
    except (serial.SerialException, OSError) as exc:
        print(f"  ECHEC : {exc}")
        print("\n  Si c'est un refus de permission, verifiez le groupe uucp :")
        print("      groups | grep uucp")
        return 2

    recu = bytearray()
    with link:
        # Un tube bulk USB n'a pas de lignes de modem : TIOCMGET y echoue, et
        # ce n'est pas une anomalie. Sur un vrai RS-232 du socle, elles marchent.
        try:
            print(f"  etat des lignes : CTS={link.cts} DSR={link.dsr} "
                  f"CD={link.cd} RI={link.ri}")
        except OSError as exc:
            print(f"  lignes de modem indisponibles ({exc.strerror}) : normal sur "
                  f"un tube bulk USB.")
        print(f"\n  1) ecoute passive pendant {args.ecoute:.0f} s (rien n'est emis)...")
        fin = time.monotonic() + args.ecoute
        while time.monotonic() < fin:
            chunk = link.read(256)
            if chunk:
                recu.extend(chunk)
        if recu:
            print(f"     {len(recu)} octet(s) recu(s) :")
            dump(bytes(recu[:128]), "     ")
        else:
            print("     rien.")

        if not args.sans_ping:
            ligne = encode("PING")
            print(f"\n  2) emission de {ligne!r}")
            try:
                link.write((ligne + LINE_END).encode("ascii"))
                link.flush()
            except (serial.SerialTimeoutException, OSError) as exc:
                print(f"     ECHEC d'ecriture : {exc}")
                print("     Le terminal ne consomme pas ce qu'on lui envoie.")
                return 1
            print(f"     attente de reponse pendant {args.ecoute:.0f} s...")
            avant = len(recu)
            fin = time.monotonic() + args.ecoute
            while time.monotonic() < fin:
                chunk = link.read(256)
                if chunk:
                    recu.extend(chunk)
            nouveau = bytes(recu[avant:])
            if nouveau:
                print(f"     {len(nouveau)} octet(s) en reponse :")
                dump(nouveau[:128], "     ")
            else:
                print("     aucune reponse.")

    print(f"\n  Verdict : {verdict(bytes(recu))}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
