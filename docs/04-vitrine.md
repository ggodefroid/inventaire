# 4. Le site public

Le terminal sert à ranger le frigo. Ce site-là sert à le regarder, depuis
n'importe quel navigateur, sans rien pouvoir changer.

C'est un **second processus**, sur un **second port**, qui ouvre la même base
SQLite **en lecture seule**. Les deux ne se connaissent pas : chacun démarre,
tombe et redémarre sans l'autre.

```
   terminal                 backend/serveur.py            backend/vitrine.py
   Skorpio CE 5.0                  :8080                        :8081
  ┌──────────────┐            ┌──────────────┐            ┌──────────────┐
  │  gâchette    │──HTTP─────▶│  écriture    │            │ lecture seule│
  └──────────────┘            │              │            │              │
                              │   inventaire.db  ◀────────│  mode=ro     │
                              │      ▲       │  data_     │  query_only  │
                              │      │       │  version   │              │
                              └──────┴───────┘            └──────┬───────┘
                                                                 │ WebSocket
                                                                 ▼
                                                          les navigateurs
```

## 4.1 Installer et lancer

Le serveur du terminal n'a qu'une dépendance (Pillow). Le site en a trois, et
elles n'ont rien à faire dans le Python du système :

```bash
python3 -m venv .venv
.venv/bin/pip install -r requirements.txt      # flask, flask-sock, pillow
.venv/bin/python backend/vitrine.py            # écoute 0.0.0.0:8081
```

```
inventaire-frigo vitrine 1.0.0
  base      backend/donnees/inventaire.db (lecture seule)
  photos    actives
  contenu   116 unites, 30 references
  ecoute    0.0.0.0:8081
            http://192.168.1.24:8081/
```

| Option | Rôle |
|---|---|
| `--port 8081` | port TCP. Il doit différer de celui de `serveur.py` |
| `--adresse` | interface d'écoute, `0.0.0.0` par défaut |
| `--db` | base à lire. La même que `serveur.py`, forcément |
| `--cache` | dossier des photos, partagé avec le serveur |
| `--journal 120` | nombre de mouvements envoyés au navigateur |
| `-v` / `-vv` | requêtes, puis tout |

Les deux processus ensemble :

```bash
python3 backend/serveur.py &                   # le terminal tape ici
.venv/bin/python backend/vitrine.py &          # le monde regarde là
```

## 4.2 Pourquoi la lecture seule tient

Trois verrous, indépendants l'un de l'autre.

**Le fichier.** La base est ouverte par une URI `file:...?mode=ro`. Le noyau
lui-même refuse l'écriture.

**La connexion.** Chaque connexion porte `PRAGMA query_only`. Un `INSERT` levé
par erreur dans le code échoue au niveau de SQLite, pas au niveau d'une
convention d'équipe.

**Les routes.** Aucune route ne déclare autre chose que `GET`. Un test parcourt
la table de routage de Flask et échoue si un `POST` y apparaît un jour.

```python
def test_aucune_ecriture_exposee(self):
    for regle in self.app.url_map.iter_rules():
        self.assertFalse({"POST", "PUT", "PATCH", "DELETE"} & regle.methods)
```

Le premier verrou peut manquer : une base en WAL exige un fichier `-shm`
accessible en écriture, absent quand personne n'a ouvert la base depuis le
dernier arrêt propre. Le programme retombe alors sur une ouverture normale, où
seul `query_only` protège. `/sante` dit lequel des deux est en place.

## 4.3 Le temps réel sans rien installer

Le site doit se mettre à jour quand le terminal bipe. Mais c'est un autre
processus qui écrit, et SQLite ne prévient personne.

`PRAGMA data_version` le dit : SQLite incrémente cet entier dès qu'une **autre**
connexion valide une transaction. Un thread le relit chaque seconde, ce qui
coûte la lecture d'une page déjà en cache. Quand la valeur bouge, l'instantané
est recalculé **une fois**, puis poussé à tous les navigateurs connectés. Dix
visiteurs ne font pas dix calculs.

La version est relevée **avant** la lecture des données, jamais après : une
écriture qui tombe pendant le calcul serait sinon déjà comprise dans le
résultat tout en marquant la version comme vue.

Chaque abonné a sa propre file bornée plutôt qu'une référence directe à sa
WebSocket : `send` depuis le thread de veille écrirait sur la socket d'un autre
thread. Une file pleine signale un client trop lent ; le message est laissé
tomber, et le client se resynchronise sur le numéro de révision du prochain
battement.

```
  navigateur          vitrine                     base
      │   GET /flux      │                          │
      ├─────────────────▶│                          │
      │◀──── init ───────┤  instantané complet      │
      │                  │                          │
      │◀──── pouls ──────┤  toutes les 5 s          │
      │                  │   heure, révision        │
      │                  │◀── data_version ────────▶│  1 fois par seconde
      │◀──── maj ────────┤  recalcul + diffusion    │
```

Si la WebSocket ne s'établit pas (proxy récalcitrant, réseau d'entreprise), la
page se rabat sur `GET /api/etat` et continue de fonctionner, sans temps réel.

## 4.4 Les routes

Toutes en `GET`, toutes en JSON sauf la page et les photos.

| Route | Rôle |
|---|---|
| `GET /` | la page, une seule |
| `GET /flux` | WebSocket : `init`, puis `maj` à chaque mouvement, `pouls` toutes les 5 s |
| `GET /api/etat` | l'instantané complet, le même que celui poussé par le flux |
| `GET /api/article/<code>` | tout ce que la base sait d'une référence |
| `GET /api/journal?limite=N` | les derniers mouvements |
| `GET /photo/<code>?c=128` | photo retaillée en PNG, ou une silhouette |
| `GET /sante` | état du processus, pour une sonde d'hébergeur |

Seuls les codes déjà présents en base sont servis par `/photo` : un visiteur ne
peut pas se servir du site pour faire tirer des images arbitraires à Open Food
Facts.

## 4.5 Les KPI

Dix indicateurs, numérotés, chacun avec sa formule affichée sous la valeur.

| | Indicateur | Formule |
|---|---|---|
| 01 | Fraîcheur | `moy( min(jours/30, 1) ) x 100`, pondérée par unité |
| 02 | Autonomie calorique | kcal totales / 2000 kcal par jour |
| 03 | Couverture catalogue | références avec fiche Open Food Facts / références |
| 04 | Pression 72 h | unités périmant sous 72 h / unités |
| 05 | Transformation moyenne | moyenne du groupe NOVA pondérée par les unités |
| 06 | Rotation | unités sorties sur 30 j / 30 x 7 |
| 07 | Densité calorique | kcal totales / masse totale x 100 |
| 08 | Diversité | entropie de Shannon / log2(références) |
| 09 | Profondeur de stock | unités / références |
| 10 | Exposition au gaspillage | unités déjà périmées / unités |

Tout part de la masse, et la masse vient du conditionnement, qui est une chaîne
libre saisie par des contributeurs : `400 g`, `1,15 L`, `6 x 125 g`, `800 gram`.
Ce qui ne se lit pas n'est pas deviné ; le total porte alors sur la part du
stock dont le conditionnement est lisible, et la carte le dit.

À côté, deux familles nettement séparées à l'écran : les **chiffres**, exacts
et utiles, et les **curiosités**, exactes aussi mais sans le moindre intérêt
pratique. Une base de données d'aliments contient de quoi calculer la distance
de marche que représente un frigo plein ; ne pas le faire serait du gâchis.

## 4.6 Publier sur Internet

Le serveur intégré de Flask suffit pour un frigo, mais il n'est pas fait pour
être exposé. Deux étages.

> Cette section décrit la mise en production **à la main**, sur l'hôte. La
> même chose en conteneurs, gunicorn et durcissement compris, tient en un
> `docker compose up -d` : **[docs/05-production.md](05-production.md)**.
> L'une ou l'autre, pas les deux.

**Un serveur WSGI.** L'application est une fabrique, prête pour gunicorn :

```bash
.venv/bin/pip install gunicorn
.venv/bin/gunicorn --bind 127.0.0.1:8081 --threads 16 \
  --worker-class gthread --workers 1 \
  'inventaire.vitrine:creer_app()'
```

Un seul *worker*. La veille et son thread vivent dans le processus ; deux
*workers* feraient deux threads de veille sur la même base, pour rien. Les
threads, eux, comptent : une WebSocket occupe le sien tant qu'elle est ouverte,
donc `--threads` borne le nombre d'onglets simultanés.

`PYTHONPATH` doit pointer sur `backend/`, et `FRIGO_DB` sur la base :

```bash
export PYTHONPATH=backend
export FRIGO_DB=backend/donnees/inventaire.db
export FRIGO_CACHE=backend/donnees/cache
```

**Un proxy avec TLS.** Caddy s'en charge en quatre lignes, certificat compris :

```caddyfile
frigo.exemple.fr {
    reverse_proxy 127.0.0.1:8081
}
```

Avec nginx, la WebSocket demande d'être explicite :

```nginx
location / {
    proxy_pass http://127.0.0.1:8081;
    proxy_http_version 1.1;
    proxy_set_header Upgrade    $http_upgrade;
    proxy_set_header Connection "upgrade";
    proxy_read_timeout 3600s;              # le flux reste ouvert
}
```

Le `proxy_read_timeout` compte : sans lui, nginx coupe la WebSocket au bout
d'une minute de silence. Le battement de cinq secondes la tient éveillée, mais
un proxy plus bavard peut quand même la fermer ; la page se reconnecte seule,
avec un délai qui double à chaque échec.

**Un service.** Deux unités systemd, une par processus :

```ini
# /etc/systemd/system/frigo-serveur.service
[Unit]
Description=Inventaire du frigo, serveur du terminal
[Service]
WorkingDirectory=/srv/inventaire
ExecStart=/usr/bin/python3 backend/serveur.py --port 8080
Restart=always
[Install]
WantedBy=multi-user.target
```

```ini
# /etc/systemd/system/frigo-vitrine.service
[Unit]
Description=Inventaire du frigo, site public
[Service]
WorkingDirectory=/srv/inventaire
Environment=PYTHONPATH=backend
ExecStart=/srv/inventaire/.venv/bin/gunicorn --bind 127.0.0.1:8081 \
  --threads 16 --workers 1 'inventaire.vitrine:creer_app()'
Restart=always
ProtectSystem=strict
ReadWritePaths=/srv/inventaire/backend/donnees/cache
[Install]
WantedBy=multi-user.target
```

`ProtectSystem=strict` avec un seul `ReadWritePaths` sur le cache des photos
verrouille le reste par le système de fichiers : la base ne peut plus être
écrite même si tout le reste tombait.

Publier le site **n'expose pas** le serveur du terminal. Le port 8080 reste
derrière le pare-feu ; seul 8081 sort, et il ne sait pas écrire dans la base.

## 4.7 Sur la page

Cinq vues, accessibles au clavier par les touches `1` à `5`.

- **Tableau** : le bandeau, les dix KPI, cinq anneaux, la charge du frigo
  reconstituée jour par jour, les mouvements, l'horizon de péremption, les
  heures de scan, la grille jour x heure, les classements, les curiosités.
- **Frigo** : un réfrigérateur qui n'existe pas. La position d'un article
  dérive de son code-barres, elle ne bouge donc pas d'une visite à l'autre.
  Les bouteilles vont dans la porte, le reste sur les étagères. Clic pour la
  fiche.
- **Stock** : toutes les références, filtrables par échéance, triables par huit
  critères.
- **Courses** : la liste de courses, partagée avec le terminal. On coche en
  faisant les courses, on ajoute un article en le tapant ou en collant son
  code-barres, et les suggestions proposent ce qui manque ou périme.
- **Flux** : le journal des mouvements, et ce qui presse.

Un bouton dans l'en-tête bascule entre **thème sombre et thème clair**. Le
sombre reste le défaut — la page est un afficheur — mais une liste de courses
se lit en plein soleil, et c'est exactement le moment où un fond noir ne se lit
plus. Sans choix explicite, la page suit la préférence du système ; le choix
est ensuite retenu. Les couleurs de données — Nutri-Score, NOVA, urgences — ne
bougent pas d'un thème à l'autre : ce sont des codes, pas de la décoration.

### La liste de courses, seule route qui n'est pas un GET

Cocher un article modifie quelque chose, et ce processus n'écrit pas. La route
`POST /api/courses/<action>` **relaie au serveur du terminal**, qui reste
l'unique écrivain de la base ; quatre actions sont permises, et rien d'autre
ne franchit le relais. Le stock demeure hors de portée : aucune route de ce
processus ne peut ajouter ni retirer une unité du frigo.

Si le serveur du terminal est arrêté, la liste reste consultable et la page le
dit. `FRIGO_SERVEUR` désigne le relais ; sous compose, c'est `http://serveur:8080`.

La recherche (`/` pour y aller) porte sur le nom, la marque, le code, les
catégories, les labels, l'origine et les allergènes. Elle filtre le stock et
éteint dans le frigo tout ce qui ne correspond pas.

La charge du frigo mérite un mot : aucune table ne garde l'historique du
niveau. On connaît le stock de maintenant et tous les mouvements. Le stock
d'hier soir, c'est celui de maintenant moins ce qui a bougé depuis, et on
remonte ainsi de jour en jour.
