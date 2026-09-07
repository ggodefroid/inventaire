"""Import d'un fichier depose par le terminal (carte mini-SD / CF, ou copie).

C'est le transport de secours qui ne depend d'aucun pilote : le client ecrit son
tampon sur la carte memoire, on sort la carte, on la met dans le PC, on importe.

Deux formats acceptes :

* Format protocole (celui que le client ecrit) -- idempotent, recommande :
      HELLO|SKORPIO1|1|0.1.0
      SCAN|1|3017620422003|2026-10-25|1|frigo|2026-09-07T18:04:11|
* CSV a la main -- pratique mais non idempotent (pas de numero de sequence) :
      3017620422003;25/10/2026;1;frigo
"""

from __future__ import annotations

import csv
import io
import logging
from pathlib import Path

from ..db import Store
from ..ingest import Session
from ..products import Enricher
from ..protocol import encode

__all__ = ["import_path", "sniff_format"]

log = logging.getLogger("frigo.fichier")


def sniff_format(text: str) -> str:
    """'protocole' si on voit des verbes du protocole, sinon 'csv'."""
    for line in text.splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        verb = line.split("|")[0].strip().upper()
        if verb in {"SCAN", "HELLO", "LOOK", "BYE", "PING"} and "|" in line:
            return "protocole"
        return "csv"
    return "protocole"


def _csv_to_protocol(text: str, device: str, keep_seq: bool) -> list[str]:
    """Convertit un CSV libre en lignes SCAN.

    keep_seq=False : aucun numero de sequence, donc reimporter le fichier
    recreera les lignes. C'est assume et signale a l'appelant.
    """
    sample = text[:4096]
    try:
        dialect = csv.Sniffer().sniff(sample, delimiters=";,\t")
    except csv.Error:
        dialect = csv.excel
        dialect.delimiter = ";"
    lines: list[str] = []
    seq = 0
    for row in csv.reader(io.StringIO(text), dialect):
        cells = [c.strip() for c in row]
        if not cells or not cells[0] or cells[0].startswith("#"):
            continue
        if not any(ch.isdigit() for ch in cells[0]):
            continue                                    # ligne d'en-tete
        seq += 1
        code = cells[0]
        expiry = cells[1] if len(cells) > 1 else ""
        qty = cells[2] if len(cells) > 2 else "1"
        location = cells[3] if len(cells) > 3 else ""
        note = cells[4] if len(cells) > 4 else ""
        lines.append(encode("SCAN", seq if keep_seq else "", code, expiry,
                            qty, location, "", note))
    return lines


def import_path(
    store: Store,
    path: str | Path,
    *,
    device: str = "SKORPIO",
    online: bool = True,
    enricher: Enricher | None = None,
    default_location: str = "frigo",
    csv_keep_seq: bool = False,
    archive: bool = False,
) -> Session:
    """Ingere un fichier. Retourne la Session pour lire ses statistiques."""
    p = Path(path).expanduser()
    text = p.read_text(encoding="utf-8", errors="replace")
    kind = sniff_format(text)
    session = Session(store, channel="fichier", online=online, enricher=enricher,
                      default_location=default_location, device=device)

    if kind == "csv":
        log.warning(
            "%s lu comme CSV : sans numero de sequence, un reimport recreera les "
            "lignes (utilisez le format protocole pour l'idempotence)", p.name
        )
        lines = _csv_to_protocol(text, device, csv_keep_seq)
    else:
        lines = text.splitlines()

    for line in lines:
        if line.strip().startswith("#"):
            continue
        for reply in session.handle_line(line):
            if reply.startswith(("ERR", "DUP")):
                log.debug("reponse : %s", reply)

    log.info("%s (%s) : %s", p.name, kind, session.stats)
    if archive and session.stats.ok:
        done = p.with_suffix(p.suffix + ".importe")
        p.rename(done)
        log.info("fichier archive sous %s", done.name)
    if enricher:
        enricher.nudge()
    return session
