#!/usr/bin/env bash
# Partage un dossier en SMB1 pour le File Explorer du terminal Windows CE.
#
# POURQUOI
#
# Le Skorpio n'a pas de navigateur, donc pas de telechargement HTTP. Mais son
# File Explorer sait ouvrir un chemin reseau \\IP\partage. On monte donc un
# partage SMB cote PC, que le terminal vient lire par-dessus la liaison PPP.
#
# Windows CE 5.0 ne parle que le SMB des annees 2000 (dialecte NT LM 0.12,
# authentification NTLMv1/LM). Les serveurs modernes desactivent tout cela par
# defaut : ce script reactive explicitement le vieux protocole, faute de quoi
# le terminal se verrait refuser la connexion sans explication.
#
# EN PRATIQUE
#
#     sudo ./tools/partage_smb.sh
#
# puis, sur le terminal, dans File Explorer, ouvrir :
#
#     \\192.168.131.1\frigo
#
# et copier SkorpioFrigo.exe et frigo.ini vers \Program Files\SkorpioFrigo\
set -uo pipefail

PARTAGE="${PARTAGE:-frigo}"
DOSSIER="${DOSSIER:-/tmp/skorpio-partage}"
IP_PC="${IP_PC:-192.168.131.1}"
IFACE="${IFACE:-ppp0}"

if [ "$(id -u)" -ne 0 ]; then
  echo "Le partage SMB ecoute sur le port 445 (privilegie) : root requis." >&2
  echo "    sudo $0" >&2
  exit 1
fi

if [ ! -d "$DOSSIER" ]; then
  echo "Dossier a partager introuvable : $DOSSIER" >&2
  echo "Preparez-le d'abord :" >&2
  echo "    ./inventaire.py pousser $DOSSIER" >&2
  exit 1
fi
echo "  dossier partage : $DOSSIER"
ls -1 "$DOSSIER" | sed 's/^/    /'

# --- pare-feu : autoriser SMB sur l'interface PPP -------------------------
regle_ajoutee=""
if command -v nft >/dev/null && nft list ruleset >/dev/null 2>&1; then
  if nft list ruleset 2>/dev/null | grep -q 'hook input'; then
    nft insert rule inet filter input iifname "$IFACE" tcp dport {139,445} accept 2>/dev/null \
      && regle_ajoutee="nft" \
      && echo "  pare-feu nft : SMB autorise sur $IFACE"
  fi
elif command -v iptables >/dev/null; then
  iptables -I INPUT -i "$IFACE" -p tcp -m multiport --dports 139,445 -j ACCEPT 2>/dev/null \
    && regle_ajoutee="iptables" \
    && echo "  pare-feu iptables : SMB autorise sur $IFACE"
fi

nettoyer() {
  echo
  if [ "$regle_ajoutee" = "nft" ]; then
    handle=$(nft -a list chain inet filter input 2>/dev/null \
             | grep "iifname \"$IFACE\".*dport.*445" | grep -oP 'handle \K[0-9]+' | head -1)
    [ -n "$handle" ] && nft delete rule inet filter input handle "$handle" 2>/dev/null \
      && echo "  regle pare-feu nft retiree"
  elif [ "$regle_ajoutee" = "iptables" ]; then
    iptables -D INPUT -i "$IFACE" -p tcp -m multiport --dports 139,445 -j ACCEPT 2>/dev/null \
      && echo "  regle pare-feu iptables retiree"
  fi
  echo "  partage arrete."
}
trap nettoyer EXIT INT TERM

echo
echo "  >>> Sur le terminal : File Explorer -> \\\\$IP_PC\\$PARTAGE"
echo "      copier SkorpioFrigo.exe et frigo.ini vers \\Program Files\\SkorpioFrigo\\"
echo "      Ctrl-C pour arreter le partage."
echo

# --- 1) impacket : leger, ideal pour un client SMB1 ancien -----------------
if command -v impacket-smbserver >/dev/null; then
  echo "  serveur : impacket-smbserver (SMB1)"
  # Sans -smb2support : on reste en SMB1 pur, le seul dialecte de CE 5.0.
  # -ip lie l'ecoute au bout PPP pour ne pas exposer le partage ailleurs.
  exec impacket-smbserver "$PARTAGE" "$DOSSIER" -ip "$IP_PC"
fi

# --- 2) repli : samba, configure pour le protocole ancien ------------------
if command -v smbd >/dev/null; then
  echo "  serveur : smbd (samba, SMB1 + auth ancienne)"
  conf="$(mktemp)"
  priv="$(mktemp -d)"
  cat > "$conf" <<CONF
[global]
    workgroup = WORKGROUP
    server min protocol = NT1
    server max protocol = NT1
    ntlm auth = yes
    lanman auth = yes
    client lanman auth = yes
    map to guest = Bad User
    guest account = nobody
    security = user
    smb ports = 445 139
    log level = 1
    private dir = $priv
    lock directory = $priv
    state directory = $priv
    cache directory = $priv
    pid directory = $priv
[$PARTAGE]
    path = $DOSSIER
    browseable = yes
    read only = yes
    guest ok = yes
    guest only = yes
CONF
  echo "  config temporaire : $conf"
  smbd_cleanup() { rm -rf "$conf" "$priv"; }
  trap 'nettoyer; smbd_cleanup' EXIT INT TERM
  exec smbd --foreground --no-process-group --configfile "$conf"
fi

echo "Aucun serveur SMB installe." >&2
echo "Installez-en un (dans les depots) :" >&2
echo "    sudo pacman -S impacket      # leger, recommande" >&2
echo "    sudo pacman -S samba         # ou samba en repli" >&2
exit 1
