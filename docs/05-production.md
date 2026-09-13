# 5. En production, avec des conteneurs

Le frigo tourne toute l'année. Il faut que le serveur redémarre après une
coupure, survive à une mise à jour de Python sur l'hôte, et n'écrive nulle
part ailleurs que dans sa base.

C'est une alternative aux deux unités systemd de
[docs/04 §4.6](04-vitrine.md#46-publier-sur-internet) — pas un complément.

```
  hôte Linux
  ┌────────────────────────────────────────────────────────────────────┐
  │  frigo-client                                                      │
  │  mono-devel → dist/Inventaire.exe, puis s'arrête                   │
  │       │ les autres attendent qu'il ait fini                        │
  │       ▼                                                            │
  │  :8080 serveur      :8081 vitrine       :8082 mcp                  │
  │  ┌──────────────┐   ┌──────────────┐   ┌──────────────┐            │
  │  │ écrit        │   │ lit (ro)     │   │ lit (ro)     │            │
  │  │ racine RO    │   │ racine RO    │   │ racine RO    │            │
  │  └──────┬───────┘   └──────┬───────┘   └──────┬───────┘            │
  │         │  /donnees        │                  │                    │
  │         └──────────────────┴──────────────────┘                    │
  │                            ▼                                       │
  │              ./backend/donnees/   (montage lié)                    │
  └────────────────────────────────────────────────────────────────────┘
       ▲                    ▲                    ▲
       │ Wi-Fi              │ navigateurs        │ LLM local
    terminal Skorpio        de la maison         (MCP)
```

`serveur` est le seul écrivain. `vitrine` et `mcp` ouvrent la base en
`mode=ro` ; leurs rares écritures — cocher un article de la liste de courses —
lui sont relayées en HTTP, par `FRIGO_SERVEUR`. Un seul processus touche au
fichier.

## 5.1 Mise en route

```bash
./demarrer.sh       # prépare, compile le client, construit, démarre, vérifie
```

```
en service.

    http://192.168.1.24:8080/    <- à saisir dans les réglages du terminal
    http://192.168.1.24:8081/    le site public
    http://192.168.1.24:8082/mcp serveur MCP, pour le LLM local
```

`demarrer.sh` ne fait rien que `compose.yaml` ne sache faire : il prépare
ce que compose suppose déjà prêt. Un dépôt fraîchement cloné n'a ni `.env`, ni
`backend/donnees/`, ni `dist/` — et laisser Docker créer ces dossiers lui-même
les rend à `root`, ce que personne ne remarque avant la première panne.

Il choisit le moteur (`docker compose`, `podman compose`, `docker-compose`,
`podman-compose`), relève l'UID et le fuseau de la machine, écrit `.env`, crée
les dossiers manquants, construit, démarre, puis attend que les deux sondes
répondent.

```bash
./demarrer.sh --etat        # état des services
./demarrer.sh --journaux    # suit les journaux
./demarrer.sh --apercu      # photographie les écrans du client -> dist/apercu/
./demarrer.sh --arreter     # arrête sans rien supprimer
./demarrer.sh --nettoyer    # supprime conteneurs et images ; les données restent
```

Sans le script, tout marche aussi, à condition de préparer soi-même :

```bash
cp .env.exemple .env && ${EDITOR:-nano} .env    # au minimum FRIGO_UID et TZ
mkdir -p backend/donnees dist
docker compose up -d --build
```

## 5.2 L'UID, cause n°1 d'échec

Le conteneur écrit dans un dossier de l'hôte à travers un montage lié. **Rien
ne traduit les identités** : l'UID du processus dans le conteneur est comparé
tel quel au propriétaire du dossier sur l'hôte. S'ils diffèrent, SQLite échoue
à l'ouverture.

`demarrer.sh` relève `id -u` tout seul. À la main, c'est `.env` :

```ini
FRIGO_UID=1000
FRIGO_GID=1000
```

Le point d'entrée vérifie l'accès avant de lancer quoi que ce soit, et dit
lequel des deux UID ne va pas :

```
entrée.sh : /donnees n'est pas accessible en écriture.

  Le conteneur tourne sous l'UID 1234, et le dossier appartient à
  l'UID 1000 sur l'hôte.
```

### Podman sans privilèges

En rootless, l'UID du conteneur est projeté sur un sous-UID de l'hôte : les
fichiers écrits dans le montage n'appartiendraient à personne de joignable. La
sortie idiomatique est de tourner en **uid 0 dans le conteneur**, qui
correspond à l'utilisateur courant sur l'hôte — lequel reste sans privilèges.

```ini
FRIGO_UID=0
FRIGO_GID=0
```

`demarrer.sh` le détecte et l'écrit tout seul.

### SELinux

Sur Fedora, RHEL et partout où podman est en jeu, un montage lié non étiqueté
est refusé sans explication utile. Les volumes portent donc `:z` dans
`compose.yaml` — ignoré là où SELinux est absent, indispensable ailleurs.

## 5.3 Les montages

| Hôte | Conteneur | Mode | Contenu |
|---|---|---|---|
| `./backend/donnees` | `/donnees` | rw | `inventaire.db`, ses `-wal`/`-shm`, le cache des photos |
| `./dist` | `/dist` | **ro** | `Inventaire.exe`, `inventaire.ini`, `version.txt` |

Des montages liés, pas des volumes nommés : la base doit rester consultable
sans Docker. `sqlite3 backend/donnees/inventaire.db`, une copie, une
restauration — tout se fait depuis l'hôte, conteneurs arrêtés ou non.

`dist/` est rempli par le conteneur `client` à chaque lancement de la pile,
et monté en lecture seule dans `serveur`, qui le sert au terminal. Il n'y a
plus rien à compiler à la main.

### Pourquoi la vitrine monte la base en écriture

Le site est en lecture seule, et son montage est pourtant `rw`. Ce n'est pas un
oubli : **une base SQLite en mode WAL exige d'écrire le fichier `-shm` pour
pouvoir être lue.** Monter `:ro` ne rendrait pas le site plus sûr, seulement
incapable d'ouvrir la base.

Le garde-fou est dans SQLite : la base est ouverte par une URI `mode=ro` et la
connexion porte `PRAGMA query_only` (voir
[`lecture.py`](../backend/inventaire/vitrine/lecture.py) et
[docs/04 §4.2](04-vitrine.md#42-pourquoi-la-lecture-seule-tient)). Une écriture
échoue au niveau du moteur, pas du montage.

Le partage du `-shm` entre deux conteneurs marche parce qu'ils tournent sur le
même noyau et projettent le même fichier. À travers NFS, il ne marcherait pas —
mais ce n'est pas la topologie d'un frigo.

## 5.4 Le fuseau horaire

Chaque réponse porte `aujourdhui=` et `heure=`, et le terminal les affiche
telles quelles : son horloge dérive et repart à zéro au *cold boot*, elle ne
fait pas autorité. Le fuseau du conteneur décide donc de ce que le terminal
montre, et une péremption calculée dans le mauvais fuseau est fausse.

L'image `python:slim` n'embarque pas `tzdata` : sans lui, `TZ=Europe/Paris` ne
fait rien et la libc retombe sur UTC. Le Dockerfile l'installe ;
`demarrer.sh` relève le fuseau de l'hôte.

```bash
docker compose exec serveur date
```

## 5.5 L'image

Une seule image, deux rôles : même paquet Python, mêmes dépendances, une ligne
de commande différente.

```
docker/entree.sh serveur     ->  python backend/serveur.py --adresse … --port …
docker/entree.sh vitrine     ->  gunicorn … 'inventaire.vitrine:creer_app(…)'
docker/entree.sh mcp         ->  gunicorn … 'inventaire.mcp:creer_app()'
docker/entree.sh <autre>     ->  exécuté tel quel (python, sh, …)
```

Une seconde image, `docker/Dockerfile.client`, porte `mono-devel` et compile le
binaire du terminal. Elle n'a rien en commun avec la première et ne tourne
jamais en service : compose la lance, elle écrit dans `dist/` et s'arrête. Son
second étage ajoute un serveur X virtuel pour photographier les écrans
(`./demarrer.sh --apercu`), et ne fait pas partie de la pile ordinaire.

Deux étages de construction. Le premier installe les dépendances dans un
virtualenv, le second le recopie d'un bloc : l'image livrée n'a ni `pip`, ni
`setuptools`, ni cache de roues. Environ **220 Mo**, dont 120 de Python.

Les roues précompilées couvrent x86-64 et arm64. Sur une architecture sans roue
— un Raspberry Pi 32 bits — le premier étage installe une chaîne de compilation
et construit Pillow ; elle reste dans cet étage, que l'image finale ne recopie
pas.

Le site est servi par **gunicorn**, un worker et trente-deux fils. Un seul
worker parce que le thread de veille vit dans le processus : deux workers
feraient deux veilles sur la même base. Les fils, eux, comptent — une WebSocket
occupe le sien tant que l'onglet reste ouvert, donc `FRIGO_FILS` borne le
nombre d'onglets simultanés. Le serveur du terminal garde son `http.server` :
un thread par requête, et zéro dépendance.

## 5.6 Le durcissement

| Réglage | Effet |
|---|---|
| `user:` | jamais root sur l'hôte |
| `read_only: true` | racine figée ; seuls `/donnees` et un `tmpfs` de 16 Mo sur `/tmp` sont écrivables |
| `cap_drop: [ALL]` | aucune capacité Linux ; le processus n'en réclame aucune |
| `no-new-privileges` | pas d'escalade par un binaire setuid |
| `logging: 10m × 5` | les journaux ne remplissent pas le disque |

La surface qui compte est le décodage d'images : le serveur va chercher des
photos sur Open Food Facts et les passe à Pillow. Avec une racine en lecture
seule, un code arbitraire obtenu par une image malformée n'a nulle part où se
poser.

```bash
docker compose exec serveur touch /app/essai      # Read-only file system
docker compose exec serveur touch /donnees/essai  # passe, et c'est le seul
```

## 5.7 Les réglages

Tout est dans `.env` (voir `.env.exemple`). Rien n'est obligatoire.

| Variable | Défaut | Rôle |
|---|---|---|
| `FRIGO_UID` / `FRIGO_GID` | `1000` | identité des processus, à aligner sur `backend/donnees/` |
| `TZ` | `Europe/Paris` | l'heure que le terminal affiche |
| `FRIGO_IP` | `0.0.0.0` | interface de publication |
| `FRIGO_PORT_SERVEUR` | `8080` | port du serveur du terminal, côté hôte |
| `FRIGO_PORT_VITRINE` | `8081` | port du site public, côté hôte |
| `FRIGO_PORT_MCP` | `8082` | port du serveur MCP, côté hôte |
| `FRIGO_HORS_LIGNE` | `0` | `1` : ne jamais interroger Open Food Facts |
| `FRIGO_VERBEUX` | `0` | `1` les requêtes, `2` tout |
| `FRIGO_FILS` | `32` | fils du site : un par onglet connecté |
| `FRIGO_FILS_MCP` | `8` | fils du serveur MCP : un par appel d'outil simultané |
| `FRIGO_JOURNAL` | `120` | mouvements envoyés au navigateur à la connexion |
| `FRIGO_TAG` | `1.0.0` | étiquette de l'image construite |

`FRIGO_IP=0.0.0.0` n'est pas un oubli : le terminal arrive par le Wi-Fi, pas
par la boucle locale. C'est le pare-feu de l'hôte qui décide qui entre
([docs/02 §2.2](02-reseau.md)).

## 5.8 Exploitation

```bash
docker compose logs -f serveur
docker compose restart vitrine        # sans interrompre le frigo
git pull && docker compose up -d --build
```

Le serveur s'arrête sur `SIGINT` plutôt que `SIGTERM` (`stop_signal` dans le
compose) : `serveur.py` attrape `KeyboardInterrupt` pour refermer la base et sa
socket, là où `SIGTERM` l'abattrait avant son `finally`. Mesuré : une seconde,
code de sortie 0 pour les deux services.

**Sauvegarder**, sans rien arrêter. `cp` sur une base en WAL peut capturer un
instantané incohérent ; l'API `backup` de SQLite prend un verrou correct :

```bash
docker compose exec -T serveur python - <<'EOF'
import sqlite3
source, copie = sqlite3.connect("/donnees/inventaire.db"), sqlite3.connect("/donnees/sauvegarde.db")
with copie:
    source.backup(copie)
copie.close(); source.close()
EOF
mv backend/donnees/sauvegarde.db ~/sauvegardes/frigo-$(date +%F).db
```

Le fichier obtenu est une base complète, sans `-wal` ni `-shm` : il suffit de le
remettre en `backend/donnees/inventaire.db`, conteneurs arrêtés, pour restaurer.

## 5.9 Ce qui reste à faire à la main

- **Le pare-feu de l'hôte.** Publier un port ne l'ouvre pas dans `firewalld` ou
  `ufw`. C'est la cause n°1 d'un terminal qui ne joint pas le serveur :
  [docs/02 §2.2](02-reseau.md).
- **TLS.** Le compose ne publie que du HTTP simple, et c'est volontaire : le
  terminal n'a qu'une pile TLS de 2005. Si le **site** doit sortir de la
  maison, mettez Caddy ou nginx devant le port 8081 seulement
  ([docs/04 §4.6](04-vitrine.md#46-publier-sur-internet)). Les ports 8080 et
  **8082 restent derrière le pare-feu** : ni l'un ni l'autre n'a
  d'authentification, et le second laisserait allonger la liste de courses à
  qui le trouve ([docs/06 §6.5](06-mcp.md#65-sous-compose)).
- **Démarrer avec la machine.** `restart: unless-stopped` suppose que le démon
  démarre au boot : `sudo systemctl enable docker`.

## 5.10 Quand ça ne va pas

| Symptôme | Cause |
|---|---|
| `/donnees n'est pas accessible en écriture` | `FRIGO_UID` ≠ propriétaire de `backend/donnees/` — §5.2 |
| Permission denied sur le montage, Fedora ou podman | SELinux : le `:z` des volumes a été retiré — §5.2 |
| Fichiers appartenant à un UID inconnu, podman | rootless sans `FRIGO_UID=0` — §5.2 |
| `address already in use` | un `python3 backend/serveur.py` tourne déjà, ou changez `FRIGO_PORT_SERVEUR` |
| Le terminal ne joint rien, `curl` local marche | pare-feu de l'hôte ([docs/02](02-reseau.md)) |
| Dates et heures décalées | `TZ` absent de `.env` — §5.4 |
| `/telecharger` dit `disponible=0` | le conteneur `client` a échoué : `docker compose logs client` |
| Rien en temps réel sur le site | un proxy coupe la WebSocket ; `proxy_read_timeout` dans [docs/04 §4.6](04-vitrine.md#46-publier-sur-internet) |

<!-- navigation -->

---

<div align="center">

← **[📊 4 · Poste de contrôle](04-vitrine.md)** · **[📚 Index](README.md)** · **[🏠 README](../README.md)** · **[🤖 6 · MCP](06-mcp.md)** →

</div>
