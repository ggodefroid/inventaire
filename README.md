# Inventaire du frigo — terminal Datalogic Skorpio

Bipper un produit devant le frigo, voir immédiatement s'il est en stock,
jusqu'à quand il est bon et ce qu'il vaut, puis l'ajouter ou le retirer.
Tout se pilote sur le terminal ; le PC ne sert qu'à héberger le serveur.

- **`frontend/`** — client Windows CE (.NET Compact Framework 2.0), compilé
  depuis Linux, sans Visual Studio.
- **`backend/`** — serveur Python 3 qui écoute sur `0.0.0.0:8080`, tient
  l'inventaire en SQLite et relaie Open Food Facts.
- **`backend/vitrine.py`** — le même inventaire, en lecture seule, sur le
  port 8081 : un site public avec tableau de bord, frigo dessiné et
  rafraîchissement par WebSocket.

## Le matériel

| | |
|---|---|
| Modèle | Datalogic **Skorpio**, Windows CE 5.0 |
| Processeur | Marvell PXA270 520 MHz |
| Écran | 240 × 320 tactile |
| Clavier | 28 touches numériques ou 38 alphanumériques |
| Liaison | Wi-Fi 802.11 b/g |

## Comment ça marche

À chaque bip, le terminal fait un aller-retour HTTP. Il ne stocke rien, ne
calcule rien, ne connaît pas la date : le serveur fait autorité sur tout.

```
   TERMINAL (Windows CE 5.0)                  PC (Linux)
   ┌──────────────────────────┐               ┌────────────────────────────┐
   │  gâchette → wedge        │               │  backend/serveur.py :8080  │
   │        ↓                 │               │            ↓               │
   │  GET /api/scan ──────────┼──── Wi-Fi ──▶ │    SQLite : lots + dates   │
   │        ↑                 │               │            ↓               │
   │  nom · Nutri-Score · NOVA│◀──────────────┤    proxy HTTP → HTTPS      │
   │  témoins · stock · lots  │               │            ↓               │
   │        ↓                 │               │    Open Food Facts         │
   │  GET /api/image ─────────┼─────────────▶ │            ↓               │
   │        ↑ BMP 80×80       │◀──────────────┤    photo retaillée         │
   │        ↓                 │               │            ↓               │
   │  POST /api/ajouter ──────┼─────────────▶ │    tableau de bord web     │
   └──────────────────────────┘               └─────────────┬──────────────┘
                                                            │ SQLite, mode=ro
                                              ┌─────────────┴──────────────┐
                                              │  backend/vitrine.py :8081  │
                                              │            ↓               │
                                              │    Flask + WebSocket       │
                                              │            ↓               │
                                              │    site public, lecture    │
                                              │    seule, temps réel       │
                                              └────────────────────────────┘
```

Trois choix portent l'ensemble :

1. **Le terminal ne parle qu'en HTTP clair.** Sa pile TLS date de 2005 et ne
   négocie plus rien de ce qu'exige openfoodfacts.org. Le serveur fait l'appel
   HTTPS à sa place, et lui rend le résultat réduit à ce qui tient sur
   240 pixels.

2. **Les photos arrivent en BMP 24 bits non compressé.** Le décodage JPEG du
   Compact Framework dépend de codecs qui peuvent manquer de l'image OS — et
   leur absence se manifeste par une exception, pas par une image dégradée. Le
   client sait relire le BMP octet par octet si jamais `Bitmap` refuse.

3. **Le serveur date les réponses.** L'horloge d'un terminal Windows CE dérive
   et repart à zéro au *cold boot*. Chaque réponse porte `aujourdhui=` et
   `heure=`, et le client cale calendrier et horloge dessus : « dans 7 jours »
   reste juste même quand le terminal se croit en 2005.

4. **Le code-barres n'est jamais assemblé à la main.** Windows produit deux
   flux pour une touche — `WM_KEYDOWN`, puis `WM_CHAR` fabriqué par
   `TranslateMessage`. Tant qu'on assemble le code soi-même à partir des
   événements clavier, on dépend de leur entrelacement, et sur la rafale d'un
   lecteur rien ne le garantit : un chiffre se perd, sans erreur ni trace. Un
   bloc-notes, lui, n'a jamais ce problème, parce que le contrôle d'édition
   traite `WM_CHAR` en interne.

   On fait donc pareil : une zone de saisie invisible accumule nativement, et
   le programme se contente de **lire son contenu une fois la file de messages
   vidée**. La touche `Entrée` ne conclut plus la lecture, elle annonce que la
   rafale se termine ; le relevé a lieu 170 ms plus tard. Aucun ordonnancement
   n'est plus supposé.

5. **Les sons sont synthétisés, pas joués depuis des fichiers.** `MessageBeep`
   ne donne que cinq sons figés, décidés par l'image du système — et souvent un
   seul sur un terminal industriel. `PlaySound` de `coredll` accepte en
   revanche un WAV **en mémoire** : le programme fabrique donc ses douze ondes
   lui-même, en 8 bits à 11 kHz, et n'a rien à déposer sur le terminal. Les
   sons qui portent une information font la queue au lieu de se couper
   l'un l'autre.

6. **Une clé de contrôle absente est reconstituée.** Beaucoup de décodeurs sont
   configurés pour ne pas transmettre le dernier chiffre d'un EAN-13, sans que
   rien ne le signale : le code part à douze chiffres et aucun catalogue ne le
   reconnaît. Le serveur s'en sort en trois temps. **Le calcul** d'abord :
   douze chiffres qui ne forment pas un UPC-A valide sont forcément un EAN-13
   amputé. **Le catalogue** ensuite, quand le calcul ne tranche pas — un EAN-13
   amputé a une chance sur dix de ressembler à un UPC-A valide, et les deux
   lectures sont alors proposées à Open Food Facts, celle qui existe gagne.
   **La mémoire** enfin : la correspondance trouvée est retenue, et au bout de
   deux corrections le serveur essaie d'emblée la complétion. Le réglage du
   lecteur reste préférable : [docs/01](docs/01-terminal-skorpio.md) §1.2.

## Démarrage

### 1. Le serveur

```bash
pip install --user pillow            # unique dépendance, pour les photos
python3 backend/serveur.py           # écoute 0.0.0.0:8080
```

Il affiche les adresses IP à recopier dans les réglages du terminal :

```
inventaire-frigo 1.0.0
  base      backend/donnees/inventaire.db
  photos    BMP pour le terminal
  réseau    Open Food Facts actif
  écoute    0.0.0.0:8080
            http://192.168.1.24:8080/   <- à saisir dans les réglages du terminal
  client    http://<ip>:8080/telecharger  (depuis le navigateur du terminal)
```

Le tableau de bord de secours est sur la même adresse, dans un navigateur.
Le vrai site, lui, tourne à part : voir **[Le site public](#le-site-public)**.

### 2. Le client

```bash
./build.sh                  # -> dist/Inventaire.exe + dist/inventaire.ini
./build.sh --bureau         # + une version testable sous Linux avec Mono
./build.sh --apercu         # + une capture de chaque écran -> dist/apercu/
```

`build.sh` crée un conteneur distrobox jetable, y installe `mono-devel`,
compile en trois passes, contrôle les en-têtes du binaire, et écrit dans
`dist/`. `./build.sh --nettoyer` ne laisse rien derrière. Si `mcs` est déjà
installé sur la machine, `./build.sh --local` se passe du conteneur.

Les trois passes comptent. La première compile contre les assemblies de
**référence** du Compact Framework, qui ne décrivent que l'API publique
documentée : si ça passe, le code n'utilise rien qui manque au terminal. Ce
binaire-là est jeté. La seconde produit le binaire livré contre les assemblies
du **runtime** du terminal, dont la version de métadonnées `v2.0.50727` est
celle qu'embarque un binaire de Visual Studio 2008. La troisième relit les
en-têtes du résultat.

### Voir l'interface sans le terminal

Le binaire livré est pour Windows CE, mais les mêmes sources compilent pour
Mono. `./build.sh --apercu` les lance dans un serveur X virtuel en 240 × 320,
déroule un scénario complet contre le serveur, et photographie chaque écran
dans `dist/apercu/`. C'est ce qui permet d'éprouver une mise en page sans
reposer le terminal sur son socle à chaque essai — et de voir ce que devient
un nom de produit de soixante caractères avant qu'il ne déborde sur le
matériel.

### 3. Installer sur le terminal

Une fois le Wi-Fi du terminal opérationnel, le plus simple est son propre
navigateur : **Internet Explorer → `http://<ip du PC>:8080/telecharger`**,
doigt maintenu sur le lien, *Enregistrer la cible sous...*

Déposez `Inventaire.exe` et `inventaire.ini` dans un dossier de la mémoire
**persistante** (`\FlashDisk\Frigo`, `\Backup\Frigo` selon le modèle) : un
*cold boot* efface tout le reste. Détails, réglages du lecteur code-barres et
première mise en route : **[docs/01-terminal-skorpio.md](docs/01-terminal-skorpio.md)**.

## Sur le terminal

```
  ACCUEIL                ARTICLE                   TOUT LE FRIGO
 ┌──────────────┐       ┌────────────────────┐    ┌──────────────────────┐
 │ INVENTAIRE 14:32       │ < 30176…   14:32 ▓ │    │ < Tout le frigo 14:32│
 │              │       │ ┌────┐Nutella      │    │ ▌[E] Biscuits…  ×1 -1│
 │ Visez un     │       │ │photo│Ferrero·400g│    │   périmé 2 j         │
 │ article      │       │ │    │ Nutri[E]    │    │ ▌[A] Muesli…    ×4 -1│
 │ ┌──────────┐ │  bip  │ └────┘ NOVA [4]    │ F1 │   dans 2 j           │
 │ │3017620…  │ │ ────▶ │ Gras AGS Suc Sel   │───▶│ ▌Gratin du…     ×1[-1]│
 │ └──────────┘ │       │ ███  ███ ███ ░░  539    │   dans 3 j           │
 │              │       │ végétarien, ss gluten   │ ▌[A] Cristaline ×2 -1│
 │ ┌──┐┌──┐┌──┐ │       │ EN STOCK          5│    │   dans 6 j           │
 │ │F1││F2││F3│ │       │ ▌jeu 01/10  19j ×2 │    │                      │
 │ └──┘└──┘└──┘ │       │ ┌───────┐┌────────┐│    │ ┌────────┐ ┌──┐┌──┐  │
 │              │       │ │1 AJOUT││2 RETIR ││    │ │Ce qui  │ │ ▲││ ▼│  │
 └──────────────┘       └────────────────────┘    └──────────────────────┘
```

- **Les témoins** viennent d'Open Food Facts : Nutri-Score, groupe NOVA,
  Eco-Score, et quatre jauges à trois crans (matières grasses, acides gras
  saturés, sucres, sel). Trois crans rouges sur le sucre se lisent sans savoir
  ce qu'est un gramme pour cent grammes. Labels et allergènes suivent, traduits
  en français depuis la taxonomie d'Open Food Facts.
- **`4` ouvre la fiche complète** : table nutritionnelle détaillée, additifs,
  liste d'ingrédients, origine, catégories. Tout ce que le catalogue sait du
  produit, sur un écran qui défile. **`5` montre la photo en grand.**
- **La liste montre les photos**, avec la note en pastille dans le coin de
  chaque vignette. Elles se chargent dans les creux, jamais devant une action,
  et une photo déjà vue ne se redemande jamais.
- **Tout se pilote au clavier.** Le terminal se tient d'une main, la gâchette
  sous l'index : atteindre un bouton au stylet oblige à poser l'appareil. Les
  flèches déplacent un curseur — marqué d'un anneau noir — entre les actions,
  `Entrée` exécute, `Échap` recule, `F1` `F2` `F3` mènent toujours au même
  endroit. Le tactile reste un second chemin, jamais le seul.
- **L'heure et la charge de la batterie** sont dans le bandeau, sur tous les
  écrans. L'heure vient du serveur, pas du terminal : son horloge repart à zéro
  au *cold boot*.
- **La couleur porte l'urgence**, partout : carré du lot, barre de liste, texte
  d'échéance. Rouge sous trois jours, vert au-delà de vingt.
- **La péremption s'ajuste champ par champ.** Le jour, le mois et l'année sont
  trois cases distinctes : `↑` `↓` choisit laquelle, `←` `→` la corrige de ±1.
  Rectifier « le 12 au lieu du 15 » ne demande donc plus de retaper la date.
  Les quatre chiffres marchent toujours (`2510` = 25 octobre prochain), les
  raccourcis `+3 j` à `+3 mois` aussi, et si le produit a déjà été rangé le
  serveur propose la durée constatée la dernière fois : il ne reste qu'à
  valider.
- **La péremption peut n'avoir qu'un mois.** Beaucoup d'emballages n'indiquent
  que « avant fin 11/2026 » : régler le mois sans toucher au jour laisse celui-ci
  vide, et l'article est enregistré à cette précision-là. On n'invente pas un
  jour que le produit n'a pas.
- **Il se met en veille.** Après une minute sans rien toucher — ou sur `Échap`
  depuis l'accueil — l'écran passe à un économiseur rétro : pluie de pixels,
  champ d'étoiles, logo rebondissant, lignes qui rebondissent, tuyaux, relayés
  toutes les trente secondes, puis le noir. Une touche réveille **et** agit, pour
  qu'une pression sur la gâchette scanne sans avoir à tirer deux fois ; un
  appui sur l'écran réveille seulement.
- **Il se met à jour tout seul.** Au démarrage, s'il joint le serveur et que
  `dist/` y contient un binaire différent du sien, il le propose, le télécharge
  et se remplace — l'ancien est conservé sous `.old`, et à aucun moment le
  terminal ne se retrouve sans binaire valide. L'empreinte comparée est celle
  des sources, pas de l'horloge : reconstruire sans changement ne déclenche
  rien.
- **Ça fait du bruit.** Douze effets : fanfare au lancement, bip du lecteur,
  arpèges montants à l'ajout et descendants au retrait, battement dissonant sur
  erreur, déclencheur d'appareil photo, balayage à la suppression, alerte
  répétée devant un produit périmé. Trois niveaux dans les réglages, de
  `non` à `tout`.
- **On peut rebipper depuis n'importe quel écran.** Le programme distingue une
  rafale de gâchette d'une touche isolée à sa cadence de frappe — deux
  caractères apparus dans le même battement de 90 ms, aucun doigt ne fait
  cela. Pas besoin de revenir à l'accueil entre deux articles, et le programme
  se passe même de suffixe : une rafale suivie d'un silence se conclut seule.
- **Les retraits suivent le FEFO** — sort toujours ce qui périme le plus tôt.
  Toucher un lot le vise ; `0` le supprime alors d'un coup.
- **La liste a un `-1` par ligne.** Ranger le frigo après un repas, c'est `→`
  pour viser la colonne « retirer », puis `↓` et `Entrée` devant chaque article
  sorti — sans ouvrir une seule fiche. La liste étant triée par échéance, ce qui
  presse est en haut.
- **`F1`, `F2`, `F3`** mènent toujours au même endroit, depuis n'importe quel
  écran : tout le frigo, l'accueil, les réglages.
- **Si le programme s'arrête**, la trace complète est écrite dans
  `inventaire-erreur.txt`, à côté de l'exécutable. Sur un terminal sans console ni
  débogueur, c'est la seule façon de savoir ce qui s'est passé.

## Le site public

```bash
python3 -m venv .venv
.venv/bin/pip install -r requirements.txt      # flask, flask-sock, pillow
.venv/bin/python backend/vitrine.py            # écoute 0.0.0.0:8081
```

Deuxième processus, deuxième port. `serveur.py` écrit, celui-ci montre. Les
deux ne partagent qu'un fichier SQLite, et rien ne les oblige à démarrer ni à
tomber ensemble. Détail complet : **[docs/04-vitrine.md](docs/04-vitrine.md)**.

```
  ┌──────────────────────────────────────────────────────────────────┐
  │ FRIGO  poste de contrôle      ⌕ chercher...     ● R7  23:04:11   │
  ├──────────────────────────────────────────────────────────────────┤
  │ TABLEAU   FRIGO   STOCK ⑦   FLUX   MACHINE                       │
  ├──────────────────────────────────────────────────────────────────┤
  │  116      30      49       7        138 096     62,9             │
  │  unités   réfs    lots     périmé   kcal        fraîcheur        │
  │                                                                  │
  │  KPI 01 ▸ 62,9/100    KPI 02 ▸ 69 j     KPI 03 ▸ 100 %           │
  │  fraîcheur            autonomie         couverture catalogue     │
  │  moy(min(j/30,1))     kcal / 2000       fiches OFF / références  │
  │                                                                  │
  │    ╭───╮      ╭───╮      ╭───╮      ▁▃▅▆▇▆▅▃▁▂▄▆█▇▅▃▂▄▅▆        │
  │    │ A │      │ 4 │      │116│      charge du frigo, 60 jours    │
  │    ╰───╯      ╰───╯      ╰───╯                                   │
  │  Nutri-Score   NOVA    échéances    ▇▃▁▂▅▇▂▁▃▆▇▅▂▁▄▆▃▁▂▅        │
  └──────────────────────────────────────────────────────────────────┘
```

- **Lecture seule, trois fois.** Base ouverte en `mode=ro`, connexion en
  `PRAGMA query_only`, et aucune route qui déclare autre chose que `GET`. Un
  test parcourt la table de routage de Flask et échoue si un `POST` y apparaît.
- **Temps réel sans rien installer.** `PRAGMA data_version` change dès qu'une
  autre connexion valide une transaction. Un thread le relit chaque seconde,
  recalcule l'instantané **une fois**, et le pousse à tous les navigateurs
  connectés : dix visiteurs ne font pas dix calculs. Si la WebSocket ne passe
  pas, la page se rabat sur `GET /api/etat` et continue de fonctionner.
- **Dix KPI, formule affichée sous la valeur.** Fraîcheur, autonomie
  calorique, couverture du catalogue, pression 72 h, transformation moyenne,
  rotation, densité calorique, diversité de Shannon, profondeur de stock,
  exposition au gaspillage.
- **Un frigo qui n'existe pas.** La position d'un article dérive de son
  code-barres : elle ne bouge donc pas d'une visite à l'autre. Les bouteilles
  vont dans la porte, le reste sur les étagères, et un clic ouvre la fiche.
- **Les chiffres utiles et les autres**, nettement séparés. D'un côté la masse,
  l'énergie, les macros, les marques, les additifs, les allergènes. De l'autre,
  combien de morceaux de sucre dorment dans le frigo, combien de kilomètres à
  pied il représente, et son entropie de Shannon.
- **La charge du frigo est reconstituée** jour par jour, à rebours depuis
  l'état actuel et le journal des mouvements : aucune table ne garde
  l'historique du niveau.
- **Responsive**, du téléphone à l'écran large, et pilotable au clavier :
  `1` à `5` pour les vues, `/` pour chercher, `Échap` pour sortir.

## L'API

Toutes les routes rendent du JSON, ou du `clé=valeur` avec `&fmt=kv` — le
format que lit le terminal. Détail complet : **[docs/03-api.md](docs/03-api.md)**.

```bash
curl 'http://localhost:8080/api/scan?code=3017620422003&fmt=kv'
curl -X POST -d 'code=3017620422003&qte=2&jours=30' http://localhost:8080/api/ajouter
curl 'http://localhost:8080/api/bientot?jours=3'
```

| Route | Rôle |
|---|---|
| `GET /api/ping` | vivant, version, compteurs |
| `GET /api/scan` | fiche complète d'un code : produit, indicateurs, stock, lots |
| `GET /api/detail` | tout ce qu'Open Food Facts sait : nutrition, additifs, ingrédients |
| `POST /api/ajouter` | `code`, `qte`, `peremption=AAAA-MM-JJ` ou `jours=N` |
| `POST /api/retirer` | `code`, `qte`, `lot` optionnel (FEFO par défaut) |
| `POST /api/lot` | corriger la quantité ou la date d'un lot |
| `POST /api/nommer` | libellé manuel, pour les codes absents du catalogue |
| `GET /api/inventaire` | tout le stock, un poste par référence |
| `GET /api/bientot` | ce qui périme sous *n* jours |
| `GET /api/image` | photo retaillée en BMP, JPEG ou PNG, cache mémoire + disque |
| `GET /` | tableau de bord navigateur |
| `GET /api/maj` | version du binaire dans `dist/`, pour la mise à jour du terminal |
| `GET /telecharger` | `dist/` servi au navigateur du terminal |

Le site public, sur 8081, a les siennes, toutes en lecture :

| Route | Rôle |
|---|---|
| `GET /` | la page, une seule |
| `GET /flux` | WebSocket : instantané, puis mise à jour à chaque mouvement |
| `GET /api/etat` | l'instantané complet : KPI, séries, postes, journal |
| `GET /api/article/<code>` | tout ce que la base sait d'une référence |
| `GET /photo/<code>` | photo retaillée en PNG, ou une silhouette |
| `GET /sante` | état du processus, pour une sonde d'hébergeur |

Une erreur métier revient en **HTTP 200 avec `ok=0`**, jamais en 4xx : sous
Compact Framework, un code d'erreur lève une `WebException`, et le terminal ne
saurait plus distinguer « le serveur dit non » de « le serveur est tombé ».

Chaque réponse porte `aujourdhui=` et `heure=` : c'est l'horloge du terminal.

## En production

```bash
./build.sh          # produit dist/, servi au terminal sur /telecharger
./demarrer.sh       # prépare, construit, démarre, vérifie
```

```
en service.

    http://192.168.1.24:8080/    <- à saisir dans les réglages du terminal
    http://192.168.1.24:8081/    le site public
```

`demarrer.sh` ne fait rien que `docker-compose.yml` ne sache faire : il prépare
ce que compose suppose déjà prêt. Un dépôt fraîchement cloné n'a ni `.env`, ni
`backend/donnees/`, ni `dist/` — et laisser Docker créer ces dossiers lui-même
les rend à `root`, ce que personne ne remarque avant la première panne. Le
script choisit le moteur disponible (docker ou podman, avec ou sans greffon
compose), relève l'UID et le fuseau de la machine, crée ce qui manque, puis
attend que les deux sondes répondent.

Une seule image pour les deux rôles. Deux montages liés : `backend/donnees/` en
écriture, `dist/` en lecture seule. Pas de volume nommé — la base reste
consultable depuis l'hôte, `sqlite3` compris. Les conteneurs tournent sans
root, racine en lecture seule, sans aucune capacité Linux.

Deux pièges valent d'être connus, et le programme les signale lui-même plutôt
que de laisser SQLite le faire à sa place : **l'UID du conteneur doit être
celui du propriétaire de `backend/donnees/`** — rien ne traduit les identités à
travers un montage lié — et **`TZ` décide de la date que le terminal affiche**,
faute d'horloge fiable de son côté. Le reste, podman sans privilèges, SELinux,
sauvegarde à chaud et diagnostic, est dans
**[docs/05-production.md](docs/05-production.md)**.

Sans conteneurs, deux unités systemd font le même travail :
[docs/04 §4.6](docs/04-vitrine.md#46-publier-sur-internet).

## Organisation

```
build.sh                      conteneur de compilation -> dist/
demarrer.sh                   prépare et lance la pile de production
Dockerfile                    image unique des deux services Python
docker-compose.yml            les deux services, montages liés, durcissement
.env.exemple                  réglages de production, à copier en .env
docker/
  entree.sh                   un rôle -> une ligne de commande
  sante.py                    sonde de santé : le corps JSON, pas le seul 200
backend/
  serveur.py                  point d'entrée du serveur du terminal (:8080)
  vitrine.py                  point d'entrée du site public (:8081)
  inventaire/
    api.py                    routage HTTP, validation des paramètres
    db.py                     SQLite : lots, produits, journal
    off.py                    Open Food Facts + cache autoritaire
    images.py                 HTTPS -> BMP 24 bits, cache mémoire + disque
    codebarres.py             clés de contrôle EAN/UPC, réparation d'un code amputé
    rendu.py                  une structure, deux sorties (JSON et clé=valeur)
    web.py                    tableau de bord et page de téléchargement
    vitrine/                  le site public, en lecture seule
      lecture.py              SQLite en mode=ro, détection de changement
      mesures.py              KPI, séries, classements, curiosités
      veille.py               diffusion WebSocket aux navigateurs
      application.py          les routes Flask
      templates/ static/      une page, un CSS, un JS, sans dépendance
frontend/
  src/                        le client C#
    Fenetre.cs                fenêtre unique, routage clavier, appels réseau
    Ecran*.cs                 les sept écrans
    Sons.cs                   synthèse des effets sonores en mémoire
    CachePhotos.cs            photos gardées en RAM, chargées dans les creux
    Veille.cs                 les cinq économiseurs d'écran
    EcranMaj.cs               mise à jour du binaire depuis le serveur
    Theme.cs                  couleurs, polices, primitives de dessin
    Api.cs · Kv.cs            HTTP et lecture du format clé=valeur
    Dates.cs                  saisie de date au pavé numérique
    Photo.cs                  décodage BMP, avec repli maison
  refs/                       assemblies de référence du CF 2.0
  refs/runtime/               assemblies du runtime terminal (v2.0.50727)
  refs/wince/                 .asmmeta de surface d'API Windows CE
  build/compiler.sh           compilation en trois passes
  tools/                      extraction des assemblies, contrôles
tests/                        152 tests
docs/                         mise en service, réseau, API, site public,
                              production
.github/workflows/ci.yml      tests sur 3.11 à 3.13, puis la pile complète
```

## Tests

```bash
python3 -m unittest discover -s tests -t .
```

`tests/test_contrat_client.py` mérite un mot. Le client est en C#, le serveur
en Python, et rien ne les relie à la compilation : renommer `contenance` en
`quantite` côté serveur laisserait le terminal afficher une chaîne vide — pas
d'erreur, pas de journal, juste une information qui disparaît de l'écran. Ce
test lit les sources C#, en extrait les 55 clés qu'elles réclament, et vérifie
que le serveur les émet toutes.

## État de la vérification

Ce qui a été **exécuté et vérifié** :

- le serveur de bout en bout, y compris contre l'API publique d'Open Food
  Facts en direct ;
- 152 tests : fusion des lots, sorties FEFO, normalisation des fiches, couche
  HTTP, contrat de clés C# ↔ Python, et tout le site public ;
- la conversion des photos : BMP 24 bits non compressé, en-tête de 54 octets,
  aux dimensions exactes demandées ;
- la compilation du client, langage bridé en ISO-2, sans aucune API hors de la
  surface publique du Compact Framework 2.0 ;
- les en-têtes du binaire : runtime CLI 2.5, métadonnées `v2.0.50727`,
  références portant le jeton de clé publique du Compact Framework
  `969db8053d3322ac`, aucune référence au .NET de bureau ;
- **l'application elle-même, en fonctionnement** : mêmes sources compilées pour
  Mono, fenêtre de 240 × 320, scénario complet contre un vrai serveur — bip,
  fiche produit, photo, fiche Open Food Facts détaillée et son défilement,
  sélection puis suppression d'un lot, retrait FEFO, durée habituelle proposée,
  ajout, navigation dans le frigo avec retrait à la ligne, réglages, produit
  inconnu. Les 16 captures sont dans `dist/apercu/` ;
- **la lecture du code-barres, à quatre cadences** : 30 ms et 5 ms par
  caractère avec suffixe `Entrée`, 30 ms sans suffixe, et 150 ms en frappe
  lente. Les quatre rendent les treize chiffres, vérifiés caractère par
  caractère sur l'écran de diagnostic — le cas à 5 ms est plus rapide que ce
  qu'un lecteur réel produit ;
- **la reconstitution d'une clé de contrôle absente** : un code amputé et le
  code complet aboutissent au même produit et à la **même clé de stock**, un
  UPC-A authentique n'est pas modifié, et la réparation est idempotente — le
  terminal renvoyant le code corrigé à l'ajout comme au retrait ;
- **le bandeau d'état** : horloge calée sur le serveur, et jauge de batterie
  (vérifiée en simulant une charge, `coredll` n'existant pas sur un poste
  Linux) ;
- **la migration de base** : une base créée par la version précédente reçoit ses
  nouvelles colonnes sans perdre ni stock ni historique, et ses fiches produit
  se recomplètent d'elles-mêmes au scan suivant ;
- **le décodeur BMP de repli**, forcé par `decodeur_bmp = maison` : il rend une
  image identique à celle de `Bitmap(flux)`. C'était le chemin le plus risqué,
  puisqu'il n'est censé servir que là où personne ne peut l'essayer ;
- **les douze ondes sonores** : extraites du binaire et relues par la
  bibliothèque standard de Python — en-tête RIFF conforme, 8 bits mono
  11 025 Hz, durées et niveaux de crête conformes à ce qui est décrit. Leur
  *audition*, elle, ne pourra être jugée que sur le terminal ;
- **le site public, de bout en bout** : page chargée dans un vrai navigateur,
  aucune erreur en console, les six sections du tableau de bord rendues, les
  trente articles dessinés dans le frigo, la recherche, les filtres, le tri, la
  fiche produit et les cinq vues ; rendu contrôlé en 1500, 834 et 390 px de
  large ;
- **la lecture seule du site** : `DELETE`, `UPDATE`, `INSERT` et `DROP` refusés
  par SQLite sur ses connexions, stock intact après la tentative, et la table
  de routage de Flask parcourue pour vérifier qu'aucune route ne déclare autre
  chose qu'un `GET` ;
- **la diffusion temps réel** : une écriture faite par un **autre processus**
  est détectée en une seconde par `PRAGMA data_version`, recalculée une fois et
  poussée à l'abonné ; une file d'abonné saturée est abandonnée sans retenir
  les autres, et l'horloge qui tourne ne déclenche à elle seule aucune
  révision ;
- **la lecture du conditionnement** : `400 g`, `1,15 L`, `6 x 125 g`,
  `800 gram`, `75 cl`, `500 mg` rendent la bonne masse, et ce qui ne se lit pas
  rend explicitement rien plutôt qu'un poids inventé ;
- **le cache de vignettes du serveur** : deuxième demande servie par la mémoire,
  une entrée par taille, éviction du plus ancien, et une entrée plus grande que
  le cache simplement ignorée ;
- l'absence de tout caractère hors Latin-1 dans l'interface : les flèches de
  défilement sont dessinées, pas écrites, car rien ne garantit que la Tahoma
  d'une image Windows CE industrielle soit complète.

Ce qui reste à confirmer **sur le matériel** :

- la présence du .NET Compact Framework 2.0 dans l'image du terminal
  (voir [docs/01](docs/01-terminal-skorpio.md) §1.3) ;
- que le Wi-Fi du terminal monte et joigne le PC
  (voir [docs/02](docs/02-reseau.md)) ;
- que le lecteur soit bien en émulation clavier avec un suffixe `Enter`
  (§1.2) — sans lui, le bouton **VALIDER** fait le même travail, mais on perd
  le scan à une main ;
- que le rendu tombe juste sur l'écran physique. La mise en page est vérifiée
  en 240 × 320, mais Mono et le Compact Framework ne mesurent pas forcément la
  Tahoma au pixel près : quelques libellés peuvent demander un ajustement ;
- que le buzzer réponde à `MessageBeep` de `coredll.dll`, et que la batterie
  réponde à `GetSystemPowerStatusEx` ; en cas d'absence, le son disparaît et la
  jauge ne s'affiche pas, sans rien casser d'autre ;
- que la barre des tâches porte bien la classe `HHTaskBar` sur cette image CE.
  Le programme essaie aussi `Shell_TrayWnd` ; si aucune ne correspond, l'écran
  reste amputé de la barre sans autre conséquence.

En cas d'arrêt inattendu, `inventaire-erreur.txt` à côté de l'exécutable porte la
trace complète.

## Licence

[MIT](LICENSE). Les assemblies de référence sous `frontend/refs/` appartiennent
à Microsoft et ne sont là que pour compiler contre l'API du Compact Framework.
