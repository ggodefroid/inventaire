#!/usr/bin/env bash
# Capture les ecrans du client, sans terminal.
#
# Le binaire livre est pour Windows CE et ne tourne pas ici, mais les memes
# sources compilent pour Mono de bureau (compiler.sh --bureau). On les lance dans
# un serveur X virtuel et on photographie chaque ecran. C'est ce qui permet
# d'eprouver une mise en page en 240x320 sans reposer le terminal sur son socle
# a chaque essai -- et la seule facon de voir a quoi ressemble un nom de produit
# de soixante caracteres avant qu'il ne deborde sur le materiel.
#
#     ./demarrer.sh --apercu              # -> dist/apercu/*.png
#
# Le serveur doit tourner : les ecrans sont remplis de vraies reponses.
set -euo pipefail

bureau="${1:?dossier du build de bureau}"
sortie="${2:?dossier de sortie}"
hote="${3:-127.0.0.1}"
port="${4:-8080}"
ecran="${DISPLAY_APERCU:-:99}"

for outil in Xvfb xdotool import mono; do
  command -v "$outil" >/dev/null || { echo "$outil manquant" >&2; exit 1; }
done

mkdir -p "$sortie"
rm -f "$sortie"/*.png

cd "$bureau"
cat > inventaire.ini <<INI
serveur = $hote
port = $port
delai_ms = 6000
photos = 1
taille_image = 80
bip = 0
veille_s = 0
INI
rm -f inventaire-erreur.txt

export DISPLAY="$ecran"
Xvfb "$ecran" -screen 0 400x480x24 >/dev/null 2>&1 &
pid_x=$!
nettoyer() { kill "${pid_mono:-0}" "$pid_x" 2>/dev/null || true; }
trap nettoyer EXIT
for _ in $(seq 20); do xdpyinfo >/dev/null 2>&1 && break; sleep 0.3; done

mono ./Inventaire.exe >/tmp/apercu-mono.log 2>&1 &
pid_mono=$!
for _ in $(seq 40); do
  fenetre=$(xdotool search --name '^Inventaire$' 2>/dev/null | head -1) || true
  [ -n "${fenetre:-}" ] && break
  sleep 0.4
done
if [ -z "${fenetre:-}" ]; then
  echo "aucune fenetre : l'application n'a pas demarre" >&2
  cat /tmp/apercu-mono.log >&2
  [ -f inventaire-erreur.txt ] && { echo "--- inventaire-erreur.txt ---" >&2; cat inventaire-erreur.txt >&2; }
  exit 1
fi
xdotool windowactivate --sync "$fenetre" 2>/dev/null || true
xdotool windowfocus --sync "$fenetre" 2>/dev/null || true
sleep 2.5

prise() { sleep "${2:-1}"; import -window "$fenetre" "$sortie/$1.png"; echo "  $1.png"; }
frappe() { xdotool type --delay 30 "$1"; }
touche() { xdotool key "$@"; }

prise 01-accueil 1
# Pas de Return : xdotool tape sans suffixe, et la rafale se conclut seule.
frappe '3017620422003';            prise 02-code-saisi 0.15
                                   prise 03-article 5
touche 5;                          prise 04-photo-grande 6        # photo plein ecran
touche Escape;                     prise 05-retour-article 3
touche 1;                          prise 06-saisie 2.5            # date par segments
touche Right Right Right;          prise 07-jour-plus-3 0.8       # +1 jour, trois fois
touche Down;                       prise 08-segment-mois 0.6      # haut/bas change de champ
touche Right;                      prise 09-mois-plus-1 0.8
touche Down Down;                  prise 10-segment-quantite 0.6
touche Right;                      prise 11-quantite-2 0.6
touche Up Up Up;                   sleep 0.4
touche Return;                     prise 12-ajoute 3.5
touche F1;                         prise 13-liste-vignettes 9     # le temps des photos
touche Down Down;                  prise 14-liste-selection 0.8
touche 5;                          prise 15-photo-depuis-liste 6
touche Escape;                     prise 16-retour-liste 3
touche Right;                      prise 17-colonne-retrait 0.8
touche F4;                         prise 18-courses-vide 2        # liste de courses
frappe '3017620422003';            prise 19-courses-inscrit 4      # un bip l'inscrit
touche Return;                     prise 20-courses-coche 2.5      # au panier, barre
touche F3;                         prise 21-reglages 2
touche F2;                         sleep 1
frappe '0000000000017';            prise 22-inconnu 4.5

echo
if [ -s inventaire-erreur.txt ]; then
  echo "EXCEPTION pendant l'apercu :" >&2
  cat inventaire-erreur.txt >&2
  exit 1
fi
echo "$(ls "$sortie"/*.png | wc -l) captures dans $sortie"
