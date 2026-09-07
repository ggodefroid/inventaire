#!/usr/bin/env bash
# Etablit une liaison IP avec le Skorpio par-dessus le cable USB du socle.
#
# POURQUOI
#
# Le port USB du terminal n'est pas une liaison serie brute : c'est ActiveSync
# qui le pilote. Sonde a l'appui (tools/sonder_liaison.py), le terminal emet la
# chaine "CLIENT" et attend "CLIENTSERVER" -- la convention de connexion directe
# de Windows -- puis enchaine sur PPP. En repondant correctement, on obtient une
# vraie liaison IP sur le cable, ce qui debloque tout :
#
#     scans     -> notre protocole en TCP sur 9101 (transport "tcp" du client)
#     install   -> le navigateur du terminal telecharge le .exe en HTTP
#
# POURQUOI UNE BOUCLE
#
# Le terminal n'emet "CLIENT" qu'a chaque nouvelle tentative de connexion, donc
# il faut le redocker. Mais le sortir du socle fait disparaitre /dev/ttyUSB0, ce
# qui tue le pppd qui le tenait ouvert : lancer pppd *puis* redocker ne peut pas
# fonctionner. Ce script fait donc l'inverse -- il attend que le port apparaisse
# et lance pppd dans la seconde. Vous pouvez le demarrer terminal pose ou non,
# et docker/redocker autant de fois que necessaire.
#
# EN PRATIQUE
#
#     sudo ./tools/liaison_ppp.sh
#
# puis sortez le terminal du socle et reposez-le. Ctrl-C pour arreter.
#
# ETAT : voie exploratoire. Le handshake est confirme sur ce materiel, la
# negociation PPP par la pile Windows CE 5.0 reste a valider. En cas d'echec, la
# carte memoire et le Wi-Fi restent les voies sures -- voir docs/02.
set -uo pipefail

# Le numero du port change a chaque redockage du terminal (ttyUSB0, puis
# ttyUSB1...), donc on le resout par VID/PID a chaque tentative. PORT force
# le choix si besoin.
PORT="${PORT:-}"
SPEED="${SPEED:-115200}"
IP_PC="${IP_PC:-192.168.131.1}"
IP_TERMINAL="${IP_TERMINAL:-192.168.131.2}"
export HANDSHAKE_TIMEOUT="${HANDSHAKE_TIMEOUT:-8}"
export HANDSHAKE_LOG="${HANDSHAKE_LOG:-/tmp/skorpio-handshake.log}"

here="$(cd "$(dirname "$0")" && pwd)"
handshake="$here/handshake_ce.py"
dccm="$here/dccm_factice.py"
trouver="$here/trouver_port.py"

# Empeche l'USB de mettre le terminal en veille pendant le handshake.
reveiller_usb() {
  local d
  for d in /sys/bus/usb/devices/*/; do
    [ -f "$d/idVendor" ] || continue
    [ "$(cat "$d/idVendor" 2>/dev/null)" = "080c" ] || continue
    [ -w "$d/power/control" ] && echo on > "$d/power/control" 2>/dev/null
  done
}

if [ "$(id -u)" -ne 0 ]; then
  echo "pppd exige les droits root :" >&2
  echo "    sudo $0 $*" >&2
  exit 1
fi
command -v pppd >/dev/null || { echo "pppd absent (paquet ppp)" >&2; exit 1; }

# Un pppd deja lance tiendrait le port et empecherait toute nouvelle tentative.
# On cherche par nom de processus exact : un -f sur la ligne de commande se
# retrouve a matcher les shells qui ont simplement affiche cette chaine.
if pgrep -x pppd >/dev/null 2>&1; then
  echo "Un pppd tourne deja :" >&2
  pgrep -ax pppd | sed 's/^/    /' >&2
  echo >&2
  echo "Arretez-le (Ctrl-C dans son terminal, ou 'sudo pkill -x pppd')" >&2
  echo "puis relancez ce script." >&2
  exit 1
fi

modprobe ppp_generic 2>/dev/null || true

# Declarer l'identifiant du Skorpio au driver ipaq (perdu apres un reboot/reset).
if [ ! -e /dev/ttyUSB0 ] && [ ! -e /dev/skorpio ]; then
  echo 080c 0200 > /sys/bus/usb-serial/drivers/ipaq/new_id 2>/dev/null || true
  sleep 1
fi

# ActiveSync cherche son service sur le port TCP 5679 du PC des que PPP monte.
# Sans reponse, il coupe souvent la liaison au bout de quelques secondes.
dccm_pid=""
if [ -x "$dccm" ] && ! ss -ltn 2>/dev/null | grep -q ":5679 "; then
  python3 "$dccm" >/tmp/skorpio-dccm.log 2>&1 &
  dccm_pid=$!
  echo "  faux service ActiveSync lance sur 5679 (journal /tmp/skorpio-dccm.log)"
fi

nettoyer() {
  [ -n "$dccm_pid" ] && kill "$dccm_pid" 2>/dev/null
  echo
  echo "  arrete."
}
trap nettoyer EXIT INT TERM

echo "  port          : ${PORT:-detecte par VID/PID} a $SPEED bauds"
echo "  adresses PPP  : PC $IP_PC  <->  terminal $IP_TERMINAL"
echo
echo "  handshake     : delai ${HANDSHAKE_TIMEOUT}s par tentative"
echo "  journaux      : $HANDSHAKE_LOG  et  /tmp/skorpio-dccm.log"
echo
echo "  >>> SORTEZ LE TERMINAL DU SOCLE ET REPOSEZ-LE <<<"
echo "      Le terminal cycle tout seul (il affiche \"connecting to host\") :"
echo "      le script guette chaque cycle et tente le handshake a chaque fois."
echo "      Laissez-le tourner une minute avant de conclure."
echo "      Ctrl-C pour arreter."
echo

essai=0
while :; do
  # Attente de l'apparition du port, sans rien tenir ouvert. Le nom est resolu
  # a chaque tour : il change d'un dockage au suivant.
  tics=0
  port=""
  while [ -z "$port" ]; do
    candidat=""
    if [ -n "$PORT" ]; then
      [ -e "$PORT" ] && candidat="$PORT"
    else
      candidat="$(python3 "$trouver" 2>/dev/null || true)"
    fi
    # Un noeud peut subsister apres le retrait du terminal : pppd echouerait
    # alors avec "Failed to open ... Input/output error", et cette rafale de
    # tentatives inutiles fait rater le vrai cycle de connexion. On ne retient
    # donc un port qu'apres avoir verifie qu'il s'ouvre reellement.
    if [ -n "$candidat" ] && python3 -c '
import os, sys
try:
    os.close(os.open(sys.argv[1], os.O_RDWR | os.O_NOCTTY | os.O_NONBLOCK))
except OSError:
    sys.exit(1)
' "$candidat" 2>/dev/null; then
      port="$candidat"
    else
      sleep 0.05
      tics=$((tics + 1))
      if [ $((tics % 200)) -eq 0 ]; then
        echo "  ... aucun terminal utilisable : posez-le sur son socle"
      fi
    fi
  done

  reveiller_usb
  essai=$((essai + 1))
  echo
  echo "  --- essai $essai : $port present, lancement de pppd ---"

  # local        : ignorer les lignes de modem, absentes sur un tube bulk USB
  # nocrtscts    : pas de controle de flux materiel sur ce type de liaison
  # noauth       : le terminal ne s'authentifie pas
  # novj         : pas de compression d'en-tetes, plus sur avec une pile de 2006
  # lcp-echo-*=0 : ne pas couper si la pile du terminal ignore les echos LCP
  # ms-dns/ms-wins: le terminal les reclame explicitement dans son IPCP ConfReq.
  #                Les refuser (ConfRej) laisse ActiveSync sans configuration
  #                reseau complete, et il raccroche avec une erreur Winsock
  #                quelques secondes apres l'etablissement de la liaison.
  # local:distant: INDISPENSABLE. Le terminal demande une adresse en envoyant
  #                IPCP <addr 0.0.0.0>. Sans ce couple, pppd n'a rien a proposer,
  #                repond ConfRej, et IPCP boucle indefiniment alors que LCP est
  #                deja passe et que ppp0 existe.
  pppd "$port" "$SPEED" \
      connect "$handshake" \
      local \
      noauth \
      nocrtscts \
      nodetach \
      nodefaultroute \
      novj \
      lcp-echo-interval 0 \
      lcp-echo-failure 0 \
      linkname skorpio \
      ipparam skorpio \
      ms-dns "$IP_PC" \
      ms-wins "$IP_PC" \
      debug \
      "$IP_PC:$IP_TERMINAL"
  code=$?
  echo "  --- pppd termine (code $code) ---"

  if [ ! -e "$port" ]; then
    echo "  $port a disparu : le terminal a change de cycle."
  fi
  # Reprise immediate : le terminal reenumere en quelques centaines de
  # millisecondes et il faut etre pret pour son prochain CLIENT.
  sleep 0.1
done
