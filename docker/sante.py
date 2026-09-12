#!/usr/bin/env python3
"""Sonde de sante des conteneurs.

    python3 /app/docker/sante.py http://127.0.0.1:8080/api/ping

Ni curl ni wget dans l'image : urllib suffit. La sonde ne se contente pas d'un
200 -- les deux services repondent `ok` dans leur corps JSON, et c'est ce
champ qui dit si la base est lisible.
"""

from __future__ import annotations

import json
import sys
import urllib.error
import urllib.request

DELAI = 5.0


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print(f"usage: {argv[0]} <url>", file=sys.stderr)
        return 2
    url = argv[1]
    try:
        with urllib.request.urlopen(url, timeout=DELAI) as reponse:
            if reponse.status != 200:
                print(f"{url} : HTTP {reponse.status}", file=sys.stderr)
                return 1
            corps = reponse.read(65536)
    except (urllib.error.URLError, OSError) as exc:
        print(f"{url} : {exc}", file=sys.stderr)
        return 1

    try:
        charge = json.loads(corps)
    except ValueError:
        return 0            # une reponse non-JSON reste une reponse

    if isinstance(charge, dict) and not charge.get("ok", 1):
        print(f"{url} : ok={charge.get('ok')!r}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
