#!/usr/bin/env python3
"""Affiche le port serie du terminal Datalogic, ou rien s'il est absent.

Le numero du port change a chaque fois que le terminal est redocke : Linux
attribue le premier minor libre, si bien qu'on passe de ttyUSB0 a ttyUSB1, puis
ttyUSB2... Coder le nom en dur ne marche donc qu'une fois. Ce script reutilise
la detection par VID/PID du projet, qui elle reste juste.

Sortie : le chemin du port sur la sortie standard, code de retour 0 ;
         rien et code 1 si aucun terminal n'est present.
"""

from __future__ import annotations

import logging
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
logging.disable(logging.CRITICAL)          # la sortie doit rester exploitable

from frigo.transports.serial_link import find_datalogic_port  # noqa: E402

port = find_datalogic_port()
if not port:
    sys.exit(1)
print(port)
