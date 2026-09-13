#!/usr/bin/env bash
# Compile le build de bureau, puis photographie chaque ecran.
#
# Les ecrans du client, photographies sans terminal. Le serveur doit
# tourner : ils sont remplis de vraies reponses.
#
#   docker compose --profile apercu run --rm apercu
#
# FRIGO_HOTE et FRIGO_PORT visent un autre serveur que celui de la pile.
set -euo pipefail

ici="$(cd "$(dirname "$0")" && pwd)"
dist="${DIST:-/dist}"
hote="${FRIGO_HOTE:-serveur}"
port="${FRIGO_PORT:-8080}"

if ! python3 -c "
import sys, urllib.request
try:
    urllib.request.urlopen('http://$hote:$port/api/ping', timeout=3).read(64)
except Exception as exc:
    print(exc, file=sys.stderr); sys.exit(1)
" 2>/dev/null; then
  echo "aucun serveur sur http://$hote:$port" >&2
  echo "Lancez la pile d'abord :  ./demarrer.sh" >&2
  exit 1
fi

"$ici/compiler.sh" --bureau
exec "$ici/apercu.sh" "$dist/bureau" "$dist/apercu" "$hote" "$port"
