#!/bin/sh
# Point d'entree de l'image : un role, une ligne de commande.
#
#   serveur    backend/serveur.py, le serveur du terminal (:8080)
#   vitrine    le site public, servi par gunicorn (:8081)
#   mcp        le serveur MCP pour un LLM local, servi par gunicorn (:8082)
#   <autre>    execute tel quel (python, sh, ...)
#
# Tout est `exec` : le processus Python devient PID 1 et recoit les signaux
# directement, donc `docker stop` l'arrete au lieu de l'abattre.
set -eu

role="${1:-serveur}"
if [ "$#" -gt 0 ]; then shift; fi

db="${FRIGO_DB:-/donnees/inventaire.db}"
donnees="$(dirname "$db")"

actif() {
  case "$(printf '%s' "${1:-}" | tr '[:upper:]' '[:lower:]')" in
    1|oui|yes|true|vrai|on) return 0 ;;
    *) return 1 ;;
  esac
}

# Un montage lie ne traduit aucune identite : l'UID du conteneur est compare
# tel quel au proprietaire du dossier sur l'hote. C'est la panne numero un du
# premier demarrage, et SQLite ne la signale que par un « unable to open
# database file » qui n'aide personne.
verifier_acces() {
  if [ ! -d "$donnees" ]; then
    echo "entree.sh : $donnees n'existe pas dans le conteneur." >&2
    echo "  le montage de backend/donnees/ manque dans compose.yaml" >&2
    exit 1
  fi
  [ -w "$donnees" ] && return 0
  cat >&2 <<MESSAGE
entree.sh : $donnees n'est pas accessible en ecriture.

  Le conteneur tourne sous l'UID $(id -u), et le dossier appartient a
  l'UID $(stat -c '%u' "$donnees" 2>/dev/null || echo '?') sur l'hote.
  Rien ne traduit les identites a travers un montage lie : les deux doivent
  coincider.

  Sur l'hote :
      id -u ; id -g                  puis reporter dans .env
      FRIGO_UID=...  FRIGO_GID=...
      docker compose up -d

  Ou, plus simplement :  ./demarrer.sh
MESSAGE
  exit 1
}

# La base est creee par serveur.py. `depends_on` suffit normalement ; sans lui
# -- un service lance seul -- mieux vaut patienter que fabriquer un fichier
# vide et le trouver sans tables.
attendre_la_base() {
  attente=0
  while [ ! -f "$db" ] && [ "$attente" -lt 30 ]; do
    [ "$attente" = 0 ] && echo "en attente de $db (creee par le serveur)..."
    sleep 1
    attente=$((attente + 1))
  done
}

# Un seul worker : la vitrine tient son thread de veille dans le processus, et
# deux workers feraient deux veilles sur la meme base. Les fils, eux, comptent
# -- une WebSocket occupe le sien tant que l'onglet reste ouvert.
# --no-control-socket : gunicorn 26 ouvrirait une socket de pilotage dans /app,
# que la racine en lecture seule refuse.
servir() {
  port="$1"; application="$2"; shift 2
  exec gunicorn \
      --bind "${FRIGO_ADRESSE:-0.0.0.0}:$port" \
      --workers 1 \
      --worker-class gthread \
      --threads "${FRIGO_FILS:-32}" \
      --worker-tmp-dir /dev/shm \
      --no-control-socket \
      --timeout 120 \
      --graceful-timeout 10 \
      --access-logfile - \
      --error-logfile - \
      --log-level "${FRIGO_NIVEAU:-info}" \
      "$@" \
      "$application"
}

case "$role" in

  serveur)
    verifier_acces
    set -- python /app/backend/serveur.py \
        --adresse "${FRIGO_ADRESSE:-0.0.0.0}" \
        --port    "${FRIGO_PORT:-8080}" \
        --db      "$db" \
        --cache   "${FRIGO_CACHE:-/donnees/cache}" \
        --dist    "${FRIGO_DIST:-/dist}" \
        "$@"
    if actif "${FRIGO_HORS_LIGNE:-}"; then set -- "$@" --hors-ligne; fi
    case "${FRIGO_VERBEUX:-0}" in
      0|"") ;;
      1)    set -- "$@" -v ;;
      *)    set -- "$@" -vv ;;
    esac
    exec "$@"
    ;;

  vitrine)
    verifier_acces
    servir "${FRIGO_PORT:-8081}" \
        "inventaire.vitrine:creer_app(journal_max=${FRIGO_JOURNAL:-120})" "$@"
    ;;

  mcp)
    verifier_acces
    attendre_la_base
    servir "${FRIGO_PORT:-8082}" "inventaire.mcp:creer_app()" "$@"
    ;;

  *)
    exec "$role" "$@"
    ;;

esac
