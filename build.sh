#!/usr/bin/env bash
# Construit le client Windows CE dans un conteneur jetable, et depose le
# binaire dans dist/.
#
# Le conteneur est la pour une seule raison : mono-devel et ses dependances
# n'ont rien a faire sur un poste de travail moderne juste pour produire un
# executable de 2005. distrobox monte le depot tel quel, la compilation ecrit
# dans dist/ avec votre propre UID, et `./build.sh --nettoyer` ne laisse rien.
#
#   ./build.sh                binaire terminal      -> dist/Inventaire.exe
#   ./build.sh --bureau       + build de test Linux -> dist/bureau/Inventaire.exe
#   ./build.sh --apercu       + capture des ecrans  -> dist/apercu/*.png
#   ./build.sh --local        compile sans conteneur (mcs deja installe)
#   ./build.sh --shell        ouvre un interpreteur dans le conteneur
#   ./build.sh --nettoyer     supprime le conteneur et dist/
set -euo pipefail

racine="$(cd "$(dirname "$0")" && pwd)"
BOITE="${BOITE:-inventaire-build}"
IMAGE="${IMAGE:-docker.io/library/debian:12}"
PAQUETS="mono-devel python3 ca-certificates"
PAQUETS_BUREAU="libgdiplus libc6-dev"
PAQUETS_APERCU="xvfb xdotool imagemagick x11-utils fonts-dejavu-core"

vert()  { printf '\033[32m%s\033[0m\n' "$*"; }
jaune() { printf '\033[33m%s\033[0m\n' "$*"; }
rouge() { printf '\033[31m%s\033[0m\n' "$*" >&2; }

usage() { sed -n '2,/^set -euo/p' "$0" | sed 's/^# \{0,1\}//; $d'; }

bureau=0
apercu=0
action="construire"
for argument in "$@"; do
  case "$argument" in
    --bureau)   bureau=1 ;;
    --apercu)   bureau=1; apercu=1 ;;
    --local)    action="local" ;;
    --shell)    action="shell" ;;
    --nettoyer) action="nettoyer" ;;
    -h|--help)  usage; exit 0 ;;
    *) rouge "argument inconnu : $argument"; usage; exit 2 ;;
  esac
done

# ---------------------------------------------------------------- nettoyage

if [ "$action" = "nettoyer" ]; then
  if command -v distrobox >/dev/null 2>&1; then
    distrobox rm --force "$BOITE" >/dev/null 2>&1 || true
    vert "conteneur $BOITE supprime"
  fi
  rm -rf "$racine/dist"
  vert "dist/ supprime"
  exit 0
fi

# ------------------------------------------------------ compilation directe

if [ "$action" = "local" ]; then
  jaune "compilation locale, sans conteneur"
  exec "$racine/frontend/build/compiler.sh" $([ "$bureau" = 1 ] && echo --bureau)
fi

# ------------------------------------------------------------- prerequis

if ! command -v distrobox >/dev/null 2>&1; then
  rouge "distrobox introuvable."
  cat >&2 <<'EOF'

    sudo dnf install distrobox      # Fedora
    sudo apt install distrobox      # Debian / Ubuntu
    sudo pacman -S distrobox        # Arch

Ou, si mono est deja installe sur la machine :

    ./build.sh --local
EOF
  exit 1
fi
if ! command -v podman >/dev/null 2>&1 && ! command -v docker >/dev/null 2>&1; then
  rouge "ni podman ni docker : distrobox n'a pas de moteur de conteneurs."
  exit 1
fi

# --------------------------------------------------------------- conteneur

if ! distrobox list 2>/dev/null | awk -F'|' 'NR>1 {gsub(/ /,"",$2); print $2}' \
     | grep -qx "$BOITE"; then
  jaune "creation du conteneur $BOITE ($IMAGE)"
  # Le depot est monte explicitement : distrobox partage $HOME, mais rien ne
  # garantit que le depot s'y trouve.
  volume=()
  case "$racine" in
    "$HOME"/*) ;;
    *) volume=(--volume "$racine:$racine:rw") ;;
  esac
  distrobox create --name "$BOITE" --image "$IMAGE" --yes "${volume[@]}" >/dev/null
  vert "conteneur cree"
fi

dans_la_boite() { distrobox enter --name "$BOITE" -- "$@"; }

paquets_voulus="$PAQUETS"
[ "$bureau" = 1 ] && paquets_voulus="$PAQUETS $PAQUETS_BUREAU"
[ "$apercu" = 1 ] && paquets_voulus="$paquets_voulus $PAQUETS_APERCU"

besoin_outils=0
dans_la_boite bash -lc 'command -v mcs >/dev/null' >/dev/null 2>&1 || besoin_outils=1
[ "$bureau" = 1 ] && ! dans_la_boite bash -lc 'ldconfig -p | grep -q libgdiplus' \
  >/dev/null 2>&1 && besoin_outils=1
[ "$apercu" = 1 ] && ! dans_la_boite bash -lc 'command -v xdotool >/dev/null' \
  >/dev/null 2>&1 && besoin_outils=1
if [ "$besoin_outils" = 1 ]; then
  jaune "installation de la chaine de compilation dans $BOITE"
  dans_la_boite sudo -n sh -c \
    "export DEBIAN_FRONTEND=noninteractive
     apt-get update -qq
     apt-get install -y --no-install-recommends $paquets_voulus"
  vert "chaine de compilation prete"
fi

if [ "$action" = "shell" ]; then
  exec distrobox enter --name "$BOITE" --  bash -l
fi

# ------------------------------------------------------------ compilation

echo
dans_la_boite bash -lc \
  "cd '$racine' && ./frontend/build/compiler.sh $([ "$bureau" = 1 ] && echo --bureau)"

if [ "$apercu" = 1 ]; then
  echo
  hote="${FRIGO_HOTE:-127.0.0.1}"
  port="${FRIGO_PORT:-8080}"
  if ! curl -fsS -m 3 "http://$hote:$port/api/ping" >/dev/null 2>&1; then
    rouge "aucun serveur sur http://$hote:$port"
    echo "Lancez d'abord : python3 backend/serveur.py" >&2
    echo "Ou visez ailleurs : FRIGO_HOTE=... FRIGO_PORT=... ./build.sh --apercu" >&2
    exit 1
  fi
  jaune "capture des ecrans (serveur $hote:$port)"
  dans_la_boite bash -lc \
    "cd '$racine' && ./frontend/build/apercu.sh '$racine/dist/bureau' \
     '$racine/dist/apercu' '$hote' '$port'"
fi

echo
vert "dist/ :"
ls -la "$racine/dist"
cat <<EOF

A copier sur le terminal :

    dist/Inventaire.exe      le programme
    dist/inventaire.ini les reglages (l'adresse du PC peut aussi se saisir
                        dans l'ecran Reglages du terminal)

Posez les deux dans un dossier de la memoire PERSISTANTE du terminal --
\\FlashDisk\\Inventaire ou \\Backup\\Inventaire selon le modele. Un cold boot vide tout
ce qui est ailleurs. Voir docs/01-terminal-skorpio.md.
EOF
