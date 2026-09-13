# 3. L'API HTTP

Le serveur parle à deux clients très inégaux : le terminal, pour qui tout est
fait, et un navigateur. Les deux tapent les mêmes routes.

## 3.1 Deux écritures d'une même réponse

Par défaut, les réponses sont en JSON indenté. Avec **`&fmt=kv`**, la même
structure sort en `clé=valeur`, une par ligne — le format que lit le terminal.

```bash
$ curl 'http://localhost:8080/api/scan?code=3017620422003&fmt=kv'
ok=1
aujourdhui=2026-09-12
code=3017620422003
connu=1
source=openfoodfacts
nom=Nutella
marque=Ferrero
contenance=400 g
image=1
nutriscore=e
nova=4
niveaux.graisses=high
niveaux.satures=high
niveaux.sucres=high
niveaux.sel=low
nutrition.kcal=539
nutrition.lipides=30.9
...
stock=3
peremption=2026-10-01
jours=19
suggestion=19
lots=2
lot.0.id=17
lot.0.qte=2
lot.0.peremption=2026-10-01
lot.0.jours=19
lot.1.id=22
lot.1.qte=1
lot.1.peremption=
lot.1.jours=
```

Pourquoi pas du JSON partout : écrire un analyseur JSON solide en C# 2.0
représente quelques centaines de lignes à risque sur un runtime de 2005, pour
des réponses qui tiennent en un kilo-octet. Le format plat se lit en trente
lignes côté terminal, et se relit à l'œil dans un navigateur.

Les conventions :

- une **liste annonce d'abord sa longueur** sous sa propre clé (`lots=2`), puis
  s'indexe au singulier (`lot.0.…`). Le terminal dimensionne ses tableaux avant
  de lire ;
- un **booléen** vaut `1` ou `0`, jamais `true` ;
- une **valeur absente** est une chaîne vide (`jours=`) ;
- une valeur est toujours **sur une seule ligne** : `\`, retour chariot et saut
  de ligne y sont échappés en `\\`, `\r`, `\n` ;
- le client **coupe au premier `=`**, donc une valeur peut en contenir.

## 3.2 Codes HTTP

| Cas | Réponse |
|---|---|
| succès | `200`, `ok=1` |
| refus métier (code inconnu, stock vide, date invalide) | **`200`, `ok=0`, `erreur=…`** |
| route inconnue | `404` |
| panne interne | `500`, `ok=0` |
| photo inexistante | `404` |

Le `200` sur refus métier est délibéré. Sous Compact Framework 2.0, un code
d'erreur lève une `WebException` ; le terminal ne saurait plus distinguer
« le serveur dit non » de « le serveur est tombé », et afficherait un
diagnostic réseau à la place du vrai motif.

## 3.3 Les routes

Toutes acceptent leurs paramètres en requête (`?a=1`) ou, pour les écritures,
en corps `application/x-www-form-urlencoded`.

### `GET /api/ping`

Vivant, version, compteurs. Sert aussi de test de liaison depuis le terminal.

```
ok=1  aujourdhui=…  version=1.0.0  heure=18:36  en_ligne=1  images=1
compteurs.unites=12  compteurs.references=7  compteurs.lots=9
compteurs.fiches=7  compteurs.mouvements=41  compteurs.perimes=1
```

`images=0` signale que Pillow n'est pas installé : les photos sont désactivées.

### `GET /api/scan?code=…`

Tout ce que le terminal affiche après un bip, en un aller-retour. Interroge
Open Food Facts si le cache est vide ou périmé (60 jours, 3 jours pour un code
introuvable). `&forcer=1` force la mise à jour.

| Champ | Sens |
|---|---|
| `connu` | `1` si un libellé est connu |
| `source` | `openfoodfacts`, `manuel`, `inconnu`, `reseau` (panne), `hors-ligne` |
| `code_lu` | le code tel que le terminal l'a envoyé ; diffère de `code` quand une clé de contrôle a dû être reconstituée |
| `precision` | `jour` ou `mois` : ce que l'emballage indiquait |
| `image` | `1` si une photo est disponible sur `/api/image` |
| `nutriscore` | `a`..`e`, ou vide |
| `nova` | `1`..`4`, ou `0` |
| `niveaux.*` | `low` · `moderate` · `high`, ou vide |
| `nutrition.*` | pour 100 g/ml |
| `stock` | total toutes dates confondues |
| `peremption` / `jours` | l'échéance la plus proche ; `jours` peut être négatif |
| `suggestion` | durée de conservation déjà constatée pour ce code, en jours |
| `lots` | du plus urgent au moins urgent, sans date en dernier |

### `POST /api/ajouter`

`code` · `qte` (défaut 1) · `peremption` **ou** `jours=N` (relatif à
aujourd'hui) · `origine` (`terminal`, `web`, `cli`).

`peremption` accepte `AAAA-MM-JJ`, et aussi **`AAAA-MM`** quand l'emballage
n'indique qu'un mois. Le lot est alors rangé au dernier jour du mois — c'est le
sens de « avant fin 11/2026 » — et la réponse porte `precision=mois`, que le
terminal affiche « fin 11/26 » plutôt qu'une date au jour près qui n'existe pas.

Un ajout de même code et même date **fusionne** avec le lot existant : bipper
six fois un yaourt donne un lot de six, pas six lignes.

Le stock est écrit **avant** l'appel à Open Food Facts : ranger une course ne
dépend jamais du réseau.

### `POST /api/retirer`

`code` · `qte` (défaut 1) · `lot` (optionnel).

Sans `lot`, sort en **FEFO** — *first expired, first out* : toujours ce qui
périme le plus tôt, en traversant plusieurs lots si nécessaire. Renvoie la
fiche à jour plus `retires=N`.

### `POST /api/lot`

`lot` · `qte` · `peremption`. Corrige un lot ; `qte=0` le supprime. Si la
nouvelle date est celle d'un autre lot du même produit, les deux fusionnent.

### `POST /api/nommer`

`code` · `nom`. Libellé manuel, pour les codes absents du catalogue. Il prime
sur Open Food Facts, mais les indicateurs et la photo continuent d'être repris
si le produit apparaît plus tard au catalogue.

### `GET /api/inventaire`

`tri` = `peremption` (défaut) · `nom` · `recent`. `limite` et `depuis`
paginent. Un poste par référence, avec son total, son échéance la plus proche,
sa note, et `image` à `1` quand une photo existe — le terminal ne demande que
celles-là.

### `GET /api/bientot?jours=N`

Les lots qui périment sous *N* jours, **périmés compris** (`jours` négatif).
`jours=0` ne rend que ce qui est déjà dépassé ou périme aujourd'hui.

### `GET /api/image?code=…`

| Paramètre | Défaut | Sens |
|---|---|---|
| `l`, `h` | 88 | largeur et hauteur du cadre, 16 à 480 |
| `img` | `bmp` | `bmp` · `jpg` · `png` |

Rend toujours exactement `l × h`, image entière centrée sur fond blanc : le
terminal réserve son cadre une fois pour toutes.

Le BMP est en **24 bits non compressé**, en-tête de 54 octets. C'est le seul
format que le Compact Framework décode sans dépendre des codecs de l'image OS
— et, au pire, que le client peut relire octet par octet lui-même.

`404` si le produit n'a pas de photo.

Les vignettes sont mises en cache à trois étages : l'original téléchargé et
chaque taille convertie sur le disque, puis les plus récemment servies en
mémoire (24 Mo par défaut, éviction du plus ancien consulté). `/api/ping`
rapporte l'état de cet étage sous `cache.*`. La réponse porte
`Cache-Control: public, max-age=86400` : une vignette ne change pas pour un
couple (code, taille) donné.

### `GET /api/maj`

Ce que `dist/` contient : `version` (empreinte des sources du client, écrite
par le conteneur `client`), `taille` du binaire, `nom`, et `disponible`. Le terminal
compare `version` à la sienne et propose la mise à jour si elles diffèrent.

### `GET /` · `GET /telecharger`

Tableau de bord, et `dist/` servi au navigateur du terminal (liste blanche :
`Inventaire.exe`, `inventaire.ini`, `version.txt`).

## 3.4 En ligne de commande

```bash
S=http://localhost:8080

curl "$S/api/scan?code=3017620422003"                     # fiche en JSON
curl -X POST -d 'code=3017620422003&qte=2&jours=30' $S/api/ajouter
curl -X POST -d 'code=3017620422003' $S/api/retirer
curl "$S/api/bientot?jours=3"                             # ce qui presse
curl "$S/api/inventaire?tri=nom"
curl -X POST -d 'code=2000000000001&nom=Gratin du dimanche' $S/api/nommer
curl -o photo.png "$S/api/image?code=3017620422003&l=200&h=200&img=png"
```

Pour un frigo entier d'un coup :

```bash
while read -r code jours; do
  curl -s -X POST -d "code=$code&jours=$jours&origine=cli" $S/api/ajouter \
    -d fmt=kv | grep '^message='
done <<'FIN'
3017620422003 90
3229820129488 7
FIN
```

<!-- navigation -->

---

<div align="center">

← **[📶 2 · Réseau](02-reseau.md)** · **[📚 Index](README.md)** · **[🏠 README](../README.md)** · **[📊 4 · Poste de contrôle](04-vitrine.md)** →

</div>
