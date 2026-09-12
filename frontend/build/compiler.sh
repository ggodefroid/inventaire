#!/usr/bin/env bash
# Compilation du client Windows CE. Execute dans le conteneur par build.sh,
# ou directement sur une machine ou mcs est installe.
#
# Trois passes, et la distinction entre les deux premieres est le coeur du
# procede :
#
#   1. CONTROLE DE SURFACE, contre les assemblies de REFERENCE du CF 2.0
#      (metadonnees seules). Elles ne decrivent que l'API publique documentee
#      du Compact Framework : si le code compile contre elles, il n'utilise
#      rien qui n'existe pas sur le terminal. Le binaire produit est jete.
#
#   2. BINAIRE LIVRE, contre les assemblies du RUNTIME du terminal. mcs recopie
#      la version de metadonnees depuis le corlib reference, et ce jeu porte
#      "v2.0.50727" -- exactement ce qu'embarque un binaire produit par Visual
#      Studio 2008. Compiler contre les references donnerait "2.0.0.0", une
#      estampille qu'aucun binaire CF authentique ne porte.
#
#   3. CONTROLE DES EN-TETES du binaire produit.
#
# Ne pas confondre avec frontend/refs/wince/ : ces .asmmeta servent a verifier
# la presence d'un type, mais sont inutilisables comme cibles de compilation
# car elles amputent les valeurs d'enumeration.
set -euo pipefail

ici="$(cd "$(dirname "$0")" && pwd)"
frontend="$(cd "$ici/.." && pwd)"
racine="$(cd "$frontend/.." && pwd)"
src="$frontend/src"
refs="$frontend/refs"
runtime="$refs/runtime"
dist="${DIST:-$racine/dist}"
exe="$dist/Inventaire.exe"

bureau=0
[ "${1:-}" = "--bureau" ] && bureau=1

if ! command -v mcs >/dev/null 2>&1; then
  echo "mcs introuvable. Lancez ./build.sh, qui prepare le conteneur." >&2
  exit 1
fi

ASSEMBLIES=(mscorlib.dll System.dll System.Drawing.dll System.Windows.Forms.dll)
for dossier in "$refs" "$runtime"; do
  for dll in "${ASSEMBLIES[@]}"; do
    if [ ! -f "$dossier/$dll" ]; then
      cat >&2 <<EOF
Assembly absente : $dossier/$dll

Extrayez-les du redistribuable Microsoft :

    # SHA1 attendu : 773e6fe43ff5ed31986d75cd916b9c60ce645f22
    curl -fLo /tmp/NETCFSetupv2.msi \\
      'https://web.archive.org/web/20070308000000id_/https://download.microsoft.com/download/0/7/2/0728de3a-fa75-413f-b3b6-8050518cef86/NETCFSetupv2.msi'
    python3 frontend/tools/extraire_refs.py /tmp/NETCFSetupv2.msi
EOF
      exit 1
    fi
  done
done

# Empreinte des sources, et non de l'horloge : reconstruire sans avoir rien
# change ne doit pas declencher de mise a jour sur le terminal. Version.cs est
# genere hors de src/ pour ne pas entrer dans son propre calcul.
empreinte="$(cat "$src"/*.cs "$src"/Properties/*.cs | sha1sum | cut -c1-12)"
cat > "$ici/Version.cs" <<CS
// Genere par frontend/build/compiler.sh. Ne pas modifier a la main.
namespace Inventaire
{
    internal static class Construction
    {
        public const string Empreinte = "$empreinte";
        public const string Date = "$(date -u '+%Y-%m-%d %H:%M')";
    }
}
CS

sources=("$src"/*.cs "$src"/Properties/*.cs "$ici/Version.cs")
mkdir -p "$dist"

# -nostdlib et -noconfig sont indispensables : sans eux mcs referencerait le
# mscorlib du bureau et l'executable ne demarrerait pas sur le terminal.
# -langversion:ISO-2 cale le langage sur ce que comprend le CF 2.0, si bien
# qu'une propriete auto-implementee est refusee ici plutot que sur le terminal.
commun=(-nostdlib -noconfig -langversion:ISO-2 -platform:anycpu -define:WINCE -warn:4)

references_de() {
  local dossier="$1"
  for dll in "${ASSEMBLIES[@]}"; do printf -- '-r:%s/%s\n' "$dossier" "$dll"; done
}

echo "== 1/3  controle de surface d'API (assemblies de reference CF 2.0)"
temporaire="$(mktemp -d)"
trap 'rm -rf "$temporaire"' EXIT
mapfile -t r_ref < <(references_de "$refs")
mcs -target:winexe -out:"$temporaire/controle.exe" "${commun[@]}" "${r_ref[@]}" "${sources[@]}"
echo "        aucune API hors de la surface publique du Compact Framework 2.0."

echo
echo "== 2/3  binaire livre (assemblies du runtime terminal)"
mapfile -t r_run < <(references_de "$runtime")
mcs -target:winexe -out:"$exe" "${commun[@]}" "${r_run[@]}" "${sources[@]}"
printf '        %s  (%s octets)\n' "$exe" "$(stat -c%s "$exe")"

# Le serveur lit cette empreinte pour dire au terminal s'il est a jour.
printf '%s\n' "$empreinte" > "$dist/version.txt"

echo
echo "== 3/3  controle des en-tetes du binaire"
python3 "$frontend/tools/verifier_binaire.py" "$exe"

# Modele de reglages, a cote du binaire : le terminal le lit au demarrage et
# l'ecran Reglages le reecrit. Le livrer evite d'avoir a tout saisir au pave.
cat > "$dist/inventaire.ini" <<'INI'
# Inventaire - reglages du terminal
# serveur : IP ou nom du PC qui fait tourner backend/serveur.py
serveur = 192.168.1.10
port = 8080
delai_ms = 5000
photos = 1
taille_image = 80
quantite_defaut = 1
# bip : 0 muet, 1 l'essentiel, 2 tous les effets sonores
bip = 2
# volume_max : pousse le volume de sortie du terminal au maximum au demarrage
volume_max = 1
# veille_s : economiseur d'ecran apres N secondes d'inactivite
# noir_s   : ecran noir apres N secondes. 0 = jamais
veille_s = 60
noir_s = 240
# maj_auto : verifier au demarrage si le serveur a une version plus recente
maj_auto = 1
# decodeur_bmp : auto, ou maison si le terminal refuse le BMP
decodeur_bmp = auto
INI

if [ "$bureau" = "1" ]; then
  echo
  echo "== bonus  build de test pour Linux (Mono de bureau)"
  mkdir -p "$dist/bureau"
  # Memes sources, sans -define:WINCE : la fenetre s'ouvre en 240x320 avec une
  # bordure, pour eprouver la mise en page sans sortir le terminal.
  mcs -target:winexe -langversion:ISO-2 -warn:4 \
      -r:System.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll \
      -out:"$dist/bureau/Inventaire.exe" "${sources[@]}"
  cp -f "$dist/inventaire.ini" "$dist/bureau/inventaire.ini"
  echo "        $dist/bureau/Inventaire.exe   (mono dist/bureau/Inventaire.exe)"
fi

echo
echo "Termine."
