#!/usr/bin/env bash
# Lance l'inventaire en production, dans des conteneurs.
#
# Le script ne fait rien que compose.yaml ne sache faire : il prepare ce que
# compose suppose deja pret. Un depot fraichement clone n'a ni .env, ni
# backend/donnees/, ni dist/ -- et laisser Docker creer ces dossiers lui-meme
# les rend a root, ce que personne ne remarque avant la premiere panne.
#
# La compilation du client Windows CE fait partie de la pile : le conteneur
# `client` produit dist/Inventaire.exe a chaque lancement, et les autres
# services attendent qu'il ait fini. Il n'y a plus rien a compiler a la main.
#
#   ./demarrer.sh              prepare, construit, demarre, verifie
#   ./demarrer.sh --arreter    arrete sans rien supprimer
#   ./demarrer.sh --etat       etat des services et sondes de sante
#   ./demarrer.sh --journaux   suit les journaux des services
#   ./demarrer.sh --apercu     photographie les ecrans du client -> dist/apercu/
#   ./demarrer.sh --nettoyer   arrete, supprime conteneurs et images
#
# Les donnees ne sont jamais touchees : elles vivent dans backend/donnees/,
# sur l'hote.
set -euo pipefail

racine="$(cd "$(dirname "$0")" && pwd)"
cd "$racine"

vert()  { printf '\033[32m%s\033[0m\n' "$*"; }
jaune() { printf '\033[33m%s\033[0m\n' "$*"; }
rouge() { printf '\033[31m%s\033[0m\n' "$*" >&2; }

usage() { sed -n '2,/^set -euo/p' "$0" | sed 's/^# \{0,1\}//; $d'; }

action="demarrer"
for argument in "$@"; do
  case "$argument" in
    --arreter)  action="arreter" ;;
    --etat)     action="etat" ;;
    --journaux) action="journaux" ;;
    --apercu)   action="apercu" ;;
    --nettoyer) action="nettoyer" ;;
    -h|--help)  usage; exit 0 ;;
    *) rouge "argument inconnu : $argument"; usage; exit 2 ;;
  esac
done

# ------------------------------------------------------------- le moteur

# docker et podman, avec ou sans le plugin compose. Le premier qui repond.
compose=()
moteur=""
if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
  moteur="docker"
  if docker compose version >/dev/null 2>&1; then compose=(docker compose)
  elif command -v docker-compose >/dev/null 2>&1; then compose=(docker-compose)
  fi
fi
if [ ${#compose[@]} -eq 0 ] && command -v podman >/dev/null 2>&1; then
  moteur="podman"
  if podman compose version >/dev/null 2>&1; then compose=(podman compose)
  elif command -v podman-compose >/dev/null 2>&1; then compose=(podman-compose)
  fi
fi
if [ ${#compose[@]} -eq 0 ]; then
  rouge "aucun moteur de conteneurs utilisable."
  cat >&2 <<'EOF'

    sudo dnf install docker-compose-plugin      # Fedora
    sudo apt install docker-compose-plugin      # Debian / Ubuntu

Ou podman, avec son greffon compose :

    sudo dnf install podman podman-compose

Si docker est installe mais ne repond pas, le service est peut-etre arrete :

    sudo systemctl enable --now docker
    sudo usermod -aG docker "$USER"   # puis se reconnecter
EOF
  exit 1
fi

# ---------------------------------------------------------- actions breves

case "$action" in
  arreter)  exec "${compose[@]}" stop ;;
  etat)     exec "${compose[@]}" ps ;;
  journaux) exec "${compose[@]}" logs -f --tail 50 ;;
  apercu)
    exec "${compose[@]}" --profile apercu run --rm apercu ;;
  nettoyer)
    "${compose[@]}" --profile apercu down --remove-orphans || true
    for image in inventaire-frigo inventaire-client inventaire-apercu; do
      $moteur rmi "$image:${FRIGO_TAG:-1.0.0}" 2>/dev/null || true
    done
    vert "conteneurs et images supprimes ; backend/donnees/ est intact"
    exit 0 ;;
esac

# --------------------------------------------------------------- .env

if [ ! -f .env ]; then
  jaune "premier demarrage : ecriture de .env"

  uid="$(id -u)"; gid="$(id -g)"
  # En podman sans privileges, l'UID du conteneur est projete sur un sous-UID
  # de l'hote : les fichiers ecrits dans le montage n'appartiendraient a
  # personne de joignable. Tourner en uid 0 *dans* le conteneur le fait
  # correspondre a l'utilisateur courant sur l'hote, qui reste sans privileges.
  if [ "$moteur" = "podman" ] \
     && [ "$(podman info --format '{{.Host.Security.Rootless}}' 2>/dev/null)" = "true" ]; then
    uid=0; gid=0
    jaune "  podman sans privileges : les conteneurs tourneront en uid 0 (projete sur $(id -un))"
  fi

  fuseau="$(timedatectl show -p Timezone --value 2>/dev/null || true)"
  [ -n "$fuseau" ] || fuseau="$(readlink -f /etc/localtime 2>/dev/null \
      | sed -n 's|.*/zoneinfo/||p')"
  [ -n "$fuseau" ] || fuseau="Europe/Paris"

  sed -e "s|^FRIGO_UID=.*|FRIGO_UID=$uid|" \
      -e "s|^FRIGO_GID=.*|FRIGO_GID=$gid|" \
      -e "s|^TZ=.*|TZ=$fuseau|" \
      .env.exemple > .env
  vert "  .env ecrit : uid $uid, gid $gid, fuseau $fuseau"
  echo "  relisez-le avant d'aller plus loin : $racine/.env"
fi

set -a; . ./.env; set +a

# -------------------------------------------------------------- dossiers

# Crees ici, donc avec l'identite de l'utilisateur. Laisses a Docker, ils
# reviendraient a root et le conteneur ne pourrait plus y ecrire.
mkdir -p backend/donnees dist

# ------------------------------------------------------------- demarrage

jaune "construction et demarrage (${compose[*]})"
echo "le conteneur client compile d'abord le binaire du terminal dans dist/"
echo
"${compose[@]}" up -d --build

# ------------------------------------------------------------ verification

port_serveur="${FRIGO_PORT_SERVEUR:-8080}"
port_vitrine="${FRIGO_PORT_VITRINE:-8081}"
port_mcp="${FRIGO_PORT_MCP:-8082}"

echo
jaune "attente des sondes de sante"
sain=0
for _ in $(seq 1 60); do
  if curl -fsS -m 2 "http://127.0.0.1:$port_serveur/api/ping" >/dev/null 2>&1 \
     && curl -fsS -m 2 "http://127.0.0.1:$port_vitrine/sante" >/dev/null 2>&1 \
     && curl -fsS -m 2 "http://127.0.0.1:$port_mcp/sante" >/dev/null 2>&1; then
    sain=1; break
  fi
  sleep 2
done

echo
"${compose[@]}" ps
echo

if [ "$sain" != 1 ]; then
  rouge "un service ne repond pas. Les journaux disent pourquoi :"
  for service in client serveur vitrine mcp; do
    echo "    ${compose[*]} logs $service"
  done
  exit 1
fi

# Les ponts de conteneurs et les tunnels VPN ont une adresse, mais le terminal
# ne les atteint pas : les ecarter evite de recopier la mauvaise dans ses
# reglages.
adresses="$(ip -4 -o addr show scope global 2>/dev/null \
    | awk '$2 !~ /^(docker|br-|podman|cni|veth|virbr|tun|tap|wg|tailscale|zt)/ \
           {split($4,a,"/"); print a[1]}' || true)"
[ -n "$adresses" ] || adresses="$(hostname -I 2>/dev/null | tr ' ' '\n' \
    | grep -E '^[0-9]+\.' || true)"
[ -n "$adresses" ] || adresses="127.0.0.1"

vert "en service."
echo
for ip in $adresses; do
  printf '    %-28s <- a saisir dans les reglages du terminal\n' \
         "http://$ip:$port_serveur/"
  printf '    %-28s le site public\n' "http://$ip:$port_vitrine/"
  printf '    %-28s serveur MCP, pour le LLM local\n' "http://$ip:$port_mcp/mcp"
done
cat <<EOF

Le pare-feu de l'hote, lui, n'a pas ete touche. Publier un port ne l'ouvre
pas ; c'est la cause numero un d'un terminal qui ne joint pas le serveur :

    sudo firewall-cmd --add-port=$port_serveur/tcp --permanent && sudo firewall-cmd --reload
    sudo ufw allow $port_serveur/tcp

Journaux : ./demarrer.sh --journaux      Arret : ./demarrer.sh --arreter
EOF
