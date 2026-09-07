#!/usr/bin/env bash
# Compile le client .NET Compact Framework 2.0 depuis Linux, sans Visual Studio.
#
# Prerequis, une seule fois :
#     sudo pacman -S mono                                  # le compilateur mcs
#     python3 tools/extraire_refs.py NETCFSetupv2.msi       # les assemblies
#
# Trois etapes, et la distinction entre les deux premieres compte :
#
#   1. Controle de surface d'API, contre les assemblies de REFERENCE du CF 2.0
#      (metadonnees seules). Elles ne decrivent que l'API publique documentee :
#      compiler contre elles garantit qu'on n'utilise rien d'autre. Le binaire
#      produit ici est jete.
#
#   2. Binaire livre, contre les assemblies du RUNTIME du terminal. mcs recopie
#      la version de metadonnees depuis le corlib reference : ce jeu porte
#      "v2.0.50727", exactement ce qu'embarque un binaire produit par Visual
#      Studio 2008. Compiler contre les references donnerait "2.0.0.0", une
#      estampille qu'aucun binaire CF authentique ne porte.
#
#   3. Controle des en-tetes du binaire (tools/verifier_binaire.py).
#
# A ne pas confondre avec client/refs/wince/ : ces .asmmeta servent a verifier
# la presence d'un type (tools/verifier_api.py), mais sont inutilisables comme
# cibles de compilation, car elles amputent les valeurs d'enumeration.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/.." && pwd)"
src="$here/SkorpioFrigo"
refs="$here/refs"
runtime="$refs/runtime"
out="$here/bin"
exe="$out/SkorpioFrigo.exe"

if ! command -v mcs >/dev/null; then
  cat >&2 <<'EOF'
mcs introuvable. Installez Mono :

    sudo pacman -S mono          # Arch / CachyOS
    sudo apt install mono-devel  # Debian / Ubuntu

EOF
  exit 1
fi

ASSEMBLIES=(mscorlib.dll System.dll System.Drawing.dll System.Windows.Forms.dll)
for dir in "$refs" "$runtime"; do
  for dll in "${ASSEMBLIES[@]}"; do
    if [ ! -f "$dir/$dll" ]; then
      cat >&2 <<EOF
Assembly absente : $dir/$dll

Extrayez-les du redistribuable Microsoft :

    # SHA1 attendu : 773e6fe43ff5ed31986d75cd916b9c60ce645f22
    curl -fLo /tmp/NETCFSetupv2.msi \\
      'https://web.archive.org/web/20070308000000id_/https://download.microsoft.com/download/0/7/2/0728de3a-fa75-413f-b3b6-8050518cef86/NETCFSetupv2.msi'
    python3 tools/extraire_refs.py /tmp/NETCFSetupv2.msi

EOF
      exit 1
    fi
  done
done

sources=("$src"/*.cs "$src"/Properties/*.cs)
mkdir -p "$out"

# -nostdlib et -noconfig sont indispensables : sans eux mcs referencerait le
# mscorlib du bureau, et l'executable ne demarrerait pas sur le terminal.
# -langversion:ISO-2 cale le langage sur ce que comprend le CF 2.0 : les
# proprietes auto-implementees et compagnie sont ainsi refusees ici plutot
# que sur le terminal.
common=(-nostdlib -noconfig -langversion:ISO-2 -platform:anycpu -warn:4)

refs_of() {
  local dir="$1"
  for dll in "${ASSEMBLIES[@]}"; do printf -- '-r:%s/%s\n' "$dir" "$dll"; done
}

echo "== 1/3 controle de surface d'API (assemblies de reference) =="
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
mapfile -t r_ref < <(refs_of "$refs")
mcs -target:winexe -out:"$tmp/controle.exe" "${common[@]}" "${r_ref[@]}" "${sources[@]}"
echo "   aucune API hors de la surface publique du Compact Framework 2.0."

echo
echo "== 2/3 compilation du binaire livre (assemblies du runtime terminal) =="
mapfile -t r_run < <(refs_of "$runtime")
mcs -target:winexe -out:"$exe" "${common[@]}" "${r_run[@]}" "${sources[@]}"
echo "   $exe"

echo
echo "== 3/3 controle des en-tetes du binaire =="
python3 "$root/tools/verifier_binaire.py" "$exe"

echo
echo "Pour deployer sur la carte memoire du terminal :"
echo "    ./inventaire.py pousser"
