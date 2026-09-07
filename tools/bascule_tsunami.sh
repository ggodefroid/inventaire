#!/usr/bin/env bash
# Bascule wlan0 sur le hotspot du telephone, AVEC filet de securite.
#
# Le PC n'a qu'une carte WiFi : passer sur "tsunami" coupe le reseau "R" par
# lequel la session Claude communique. Si "tsunami" n'a pas de data mobile, la
# session se gele. Ce script installe donc un retour automatique sur "R" au
# bout d'un delai, annulable une fois qu'on a confirme que tout va bien.
#
#   sudo -v            # (nmcli marche sans root ici : membre du groupe network)
#   ./tools/bascule_tsunami.sh tsunami
#   # ... si tout va bien :
#   ./tools/bascule_tsunami.sh --annuler-filet
set -uo pipefail

RETOUR="$(cat /tmp/skorpio-wifi-retour 2>/dev/null || echo R)"
DELAI="${DELAI:-600}"           # retour auto apres 10 min sans annulation
FILET_PID=/tmp/skorpio-filet.pid

if [ "${1:-}" = "--annuler-filet" ]; then
  if [ -f "$FILET_PID" ]; then
    kill "$(cat $FILET_PID)" 2>/dev/null && echo "  filet de securite annule (on reste sur tsunami)"
    rm -f "$FILET_PID"
  else
    echo "  aucun filet actif"
  fi
  exit 0
fi

if [ "${1:-}" = "--retour" ]; then
  echo "  retour manuel sur $RETOUR"
  nmcli con up "$RETOUR" 2>/dev/null || nmcli dev wifi connect "$RETOUR"
  exit $?
fi

SSID="${1:-tsunami}"

# Filet : un processus detache qui rebascule sur R apres le delai.
( sleep "$DELAI"; nmcli con up "$RETOUR" >/dev/null 2>&1 || nmcli dev wifi connect "$RETOUR" >/dev/null 2>&1 ) &
echo $! > "$FILET_PID"
echo "  filet arme : retour automatique sur '$RETOUR' dans $((DELAI/60)) min"
echo "  (annulable par : ./tools/bascule_tsunami.sh --annuler-filet)"
echo

echo "  bascule de wlan0 sur '$SSID'..."
if nmcli dev wifi connect "$SSID" 2>&1; then
  sleep 3
  ip=$(ip -4 -o addr show wlan0 2>/dev/null | awk '{print $4}' | cut -d/ -f1)
  echo "  connecte. IP du PC sur '$SSID' : ${ip:-<en attente>}"
else
  echo "  echec de connexion a '$SSID'. Le filet rebasculera sur '$RETOUR'."
  exit 1
fi
