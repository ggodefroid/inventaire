#!/usr/bin/env bash
# Rend les cinquante-cinq economiseurs hors terminal, et en fait une planche.
#
#   docker compose --profile apercu run --rm --entrypoint \
#       /src/frontend/build/planche-veille.sh apercu
#
# Aucun serveur n'est requis : les economiseurs ne parlent a personne.
set -euo pipefail

ici="$(cd "$(dirname "$0")" && pwd)"
frontend="$(cd "$ici/.." && pwd)"
sortie="${DIST:-/dist}/veille"
temporaire="$(mktemp -d)"
trap 'rm -rf "$temporaire"' EXIT

# Version.cs est genere par compiler.sh ; on en fabrique un minimal si besoin.
if [ ! -f "$ici/Version.cs" ]; then
  printf 'namespace Inventaire { internal static class Construction {\n  public const string Empreinte = "planche"; public const string Date = "-"; } }\n' \
    > "$temporaire/Version.cs"
  version="$temporaire/Version.cs"
else
  version="$ici/Version.cs"
fi

echo "compilation du banc de rendu"
mcs -target:exe -langversion:ISO-2 -warn:4 \
    -r:System.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll \
    -main:Inventaire.PlancheVeille \
    -out:"$temporaire/planche.exe" \
    "$frontend"/src/*.cs "$frontend"/src/Properties/*.cs \
    "$version" "$frontend/tools/PlancheVeille.cs"

mkdir -p "$sortie"
echo
mono "$temporaire/planche.exe" "$sortie"
echo
echo "planches dans $sortie"
