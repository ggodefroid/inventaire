#!/usr/bin/env bash
# Chronometre le decodage d'une vignette, par les deux chemins.
#
#   docker compose --profile apercu run --rm \
#       --entrypoint /src/frontend/build/mesure-photo.sh apercu
set -euo pipefail

ici="$(cd "$(dirname "$0")" && pwd)"
frontend="$(cd "$ici/.." && pwd)"
hote="${FRIGO_HOTE:-serveur}"
port="${FRIGO_PORT:-8080}"
temporaire="$(mktemp -d)"
trap 'rm -rf "$temporaire"' EXIT

if [ ! -f "$ici/Version.cs" ]; then
  printf 'namespace Inventaire { internal static class Construction {\n  public const string Empreinte = "mesure"; public const string Date = "-"; } }\n' \
    > "$temporaire/Version.cs"
  version="$temporaire/Version.cs"
else
  version="$ici/Version.cs"
fi

mcs -target:exe -langversion:ISO-2 -warn:0 \
    -r:System.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll \
    -main:Inventaire.MesurePhoto -out:"$temporaire/mesure.exe" \
    "$frontend"/src/*.cs "$frontend"/src/Properties/*.cs \
    "$version" "$frontend/tools/MesurePhoto.cs" 2>&1 | grep -v "^$" || true

echo
mono "$temporaire/mesure.exe" "http://$hote:$port" "${1:-3017620422003}"
