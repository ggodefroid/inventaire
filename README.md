# Inventaire du frigo — terminal Datalogic Skorpio

Scanner les produits du frigo avec un terminal code-barres Datalogic
Skorpio-G, leur associer une date de péremption, et récupérer le tout sur le PC
dans une base consultable.

- **Client** : programme natif Windows CE (.NET CF 2.0) poussé sur le terminal.
- **Serveur** : Python 3, sur le PC. Une seule dépendance (`pyserial`), tout le
  reste est de la bibliothèque standard.

## Matériel visé

| | |
|---|---|
| Modèle | Datalogic **DL-SKORPIO-G**, réf. 701-902-xxx (Skorpio Gun, 1ʳᵉ génération) |
| OS | Windows CE 5.0 |
| Processeur | Marvell PXA270 520 MHz, 128 Mo RAM |
| Écran | 240 × 320 tactile |
| Clavier | 28 touches numériques ou 38 touches alphanumériques |
| Liaisons | USB (« Datalogic USB Sync », `080c:0200`), RS-232 par socle, Wi-Fi 802.11b/g en option |
| Extension | 1 slot Compact Flash, 1 slot mini-SD |

## Comment ça marche

Le terminal n'a pas de réseau utilisable en permanence : il travaille donc
**hors ligne**, et on synchronise quand il revient sur son socle.

```
   TERMINAL SKORPIO                          PC (Linux)
   ┌──────────────────────┐                  ┌────────────────────────────┐
   │ gâchette → wedge     │                  │  inventaire.py serve       │
   │      ↓               │                  │                            │
   │ code-barres          │                  │   ┌──────────────────┐     │
   │      ↓               │   USB / série    │   │  SQLite          │     │
   │ saisie péremption    │  ──────────────▶ │   │  produits+dates  │     │
   │      ↓               │   ou carte SD    │   └──────────────────┘     │
   │ tampon.txt (disque)  │  ──────────────▶ │            ↓               │
   │  numéros de séquence │   ou TCP/Wi-Fi   │   Open Food Facts          │
   └──────────────────────┘                  │   → libellés produits      │
                                             │            ↓               │
                                             │   tableau de bord web      │
                                             └────────────────────────────┘
```

Trois propriétés portent la fiabilité de l'ensemble :

1. **Chaque scan est écrit sur le disque du terminal avant tout.** Batterie
   arrachée, plantage, terminal éteint : rien n'est perdu.
2. **Chaque scan porte un numéro de séquence définitif.** Le serveur indexe
   `(terminal, séquence)` de façon unique, donc renvoyer tout le tampon après un
   câble débranché ne crée jamais de doublon. C'est ce qui rend la
   synchronisation sur socle sûre.
3. **Les trois transports partagent le même protocole texte.** USB/série, TCP et
   fichier sur carte mémoire passent par le même code d'ingestion. Si l'USB
   résiste, on sort la carte SD et le résultat est identique.

## Démarrage rapide (sans le terminal)

Le simulateur permet de valider toute la chaîne avant de toucher au matériel.

```bash
pip install --user pyserial          # unique dépendance

# terminal 1 : le serveur
./inventaire.py serve --sans-usb

# terminal 2 : un faux Skorpio
./inventaire.py simuler
  code-barres > 3017620422003
  peremption  > 251026            # ou 2510, ou 25102026
```

Puis le tableau de bord sur <http://127.0.0.1:8077/>, et en ligne de commande :

```bash
./inventaire.py ls                  # tout l'inventaire
./inventaire.py bientot -j 3        # ce qui périme sous 3 jours
./inventaire.py consommer 3017620422003
./inventaire.py exporter -o frigo.csv
```

## Compiler et pousser le client

Le client se compile **depuis Linux**, sans Visual Studio :

```bash
sudo pacman -S mono                        # une fois : le compilateur mcs
./client/build-linux.sh                    # -> client/bin/SkorpioFrigo.exe
./inventaire.py pousser                    # -> sur la carte mémoire du terminal
```

Les assemblies de référence du Compact Framework sont déjà dans `client/refs/`.
Pour les régénérer :

```bash
# SHA1 attendu : 773e6fe43ff5ed31986d75cd916b9c60ce645f22
curl -fLo /tmp/NETCFSetupv2.msi \
  'https://web.archive.org/web/20070308000000id_/https://download.microsoft.com/download/0/7/2/0728de3a-fa75-413f-b3b6-8050518cef86/NETCFSetupv2.msi'
python3 tools/extraire_refs.py /tmp/NETCFSetupv2.msi
```

Trois façons d'installer le `.exe` sur le terminal, selon ce dont vous disposez :

| Voie | Commande | Prérequis |
|---|---|---|
| Carte mémoire | `./inventaire.py pousser` | une carte mini-SD ou CF + lecteur |
| Navigateur du terminal | `./inventaire.py pousser --http` | le Wi-Fi du terminal opérationnel |
| Dossier puis copie manuelle | `./inventaire.py pousser ~/un/dossier` | rien |

## Mise en service réelle

Dans l'ordre :

1. **[docs/01-terminal-skorpio.md](docs/01-terminal-skorpio.md)** — reprendre la
   main sur le terminal, activer l'émulation clavier du lecteur, installer le
   client.
2. **[docs/02-liaison-usb.md](docs/02-liaison-usb.md)** — faire apparaître le
   terminal en `/dev/ttyUSB0` sous Linux, et les solutions de repli.
3. **[client/README.md](client/README.md)** — compiler le `.exe`.
4. **[docs/03-protocole.md](docs/03-protocole.md)** — le protocole, si vous
   voulez le modifier.

## Commandes

| Commande | Rôle |
|---|---|
| `serve` | serveur complet : USB + TCP + tableau de bord |
| `ports` | liste les ports série et diagnostique la liaison USB |
| `simuler` | faux terminal, pour tester sans matériel |
| `pousser` | prépare le client pour le terminal (carte mémoire ou HTTP) |
| `importer <fichier>` | ingère un tampon déposé sur carte mémoire |
| `ls` | état du frigo |
| `bientot -j N` | ce qui périme dans N jours |
| `ajouter <code> [date]` | saisie manuelle |
| `consommer <code>` | sort l'unité la plus urgente (FEFO) |
| `nommer <code> [libellé]` | force un libellé produit |
| `exporter` | CSV ou JSON |
| `stats` | compteurs de la base |

Options utiles : `--hors-ligne` (ne jamais interroger Open Food Facts),
`--db <fichier>`, `-v` / `-vv` (journalisation).

## Organisation du dépôt

```
inventaire.py              point d'entrée
frigo/
  protocol.py              protocole de ligne (miroir du C#)
  dates.py                 saisie de dates au pavé numérique
  barcode.py               normalisation EAN/UPC
  db.py                    SQLite : inventaire, produits, journal
  products.py              Open Food Facts + cache + enrichissement en tâche de fond
  ingest.py                cœur : applique le protocole à la base
  transports/              serial_link.py · tcp_link.py · fileimport.py
  webui.py                 tableau de bord local
  cli.py                   ligne de commande
  deploy.py                préparation de la charge utile pour le terminal
client/
  SkorpioFrigo/            client Windows CE en C# (.NET CF 2.0)
  refs/                    assemblies de référence du CF 2.0
  refs/runtime/            assemblies du runtime terminal (estampille v2.0.50727)
  refs/wince/              .asmmeta de surface d'API Windows CE
  build-linux.sh           compilation en trois passes
tests/                     38 tests, dont l'interopérabilité C#/Python
tools/
  sonder_liaison.py        identifie ce qui parle à l'autre bout du port
  liaison_ppp.sh           monte une liaison IP sur le câble USB du socle
  handshake_ce.py          handshake CLIENT/CLIENTSERVER de Windows CE
  dccm_factice.py          garde la liaison PPP debout face à ActiveSync
  extraire_refs.py         extrait les assemblies du redistribuable Microsoft
  verifier_api.py          contrôle la présence des membres .NET utilisés
  verifier_binaire.py      contrôle les en-têtes du binaire produit
  skorpio-udev.rules       règles udev pour la liaison USB
data/inventaire.db         la base (créée au premier lancement)
```

## Tests

```bash
python3 -m unittest discover -s tests -t .
```

`tests/test_interop.py` mérite un mot : le client est en C# et le serveur en
Python, et une divergence de somme de contrôle ou d'analyse de date casserait la
liaison **sans aucun message d'erreur**. Le fichier `tests/csharp_mirror.py`
translittère le C# en Python, et les tests comparent les deux implémentations
sur les mêmes vecteurs. Si vous modifiez `Protocol.cs` ou `Dates.cs`, mettez ce
miroir à jour, sinon les tests deviennent un faux témoin.

## État de la vérification

Ce qui a été **réellement exécuté et validé** :

- serveur complet, de bout en bout, avec le simulateur : scans, libellés Open
  Food Facts en direct, rejeu du tampon renvoyant `DUP` partout ;
- 38 tests, dont l'équivalence prouvée du protocole et des dates entre C# et
  Python ;
- **le client compile** depuis Linux (`mcs`, langage bridé en ISO-2) et passe
  le contrôle de surface d'API contre les assemblies de référence du CF 2.0 ;
- **les en-têtes du binaire sont conformes** : runtime CLI 2.5, métadonnées
  `v2.0.50727`, quatre références portant le jeton de clé publique du Compact
  Framework `969db8053d3322ac`, aucune référence au .NET de bureau ;
- la chaîne de déploiement : écriture sur carte avec vérification d'empreinte
  après copie, et service HTTP dont le binaire téléchargé est identique à
  l'original ;
- détection du terminal sur l'USB (`080c:0200`) et diagnostic du pilote absent.

Ce qui reste à confirmer **sur le matériel** :

- que le programme démarre et que l'écran se met en page correctement sur les
  240 × 320 réels — la compilation ne dit rien du rendu ;
- la présence du .NET Compact Framework 2.0 dans l'image du terminal ;
- le nom du port série côté terminal (`COM1:`, `COM6:`…), qui dépend du réglage
  « PC Connection » — le menu **Outils → Diagnostic ports** du client teste
  `COM1:` à `COM9:` et affiche lesquels s'ouvrent ;
- que PPP monte bien sur le câble USB. Ce qui est **mesuré** : `/dev/ttyUSB0`
  apparaît, le terminal répond `CLIENT`, donc c'est ActiveSync qui pilote le
  port et il attend `CLIENTSERVER` avant d'enchaîner sur PPP
  (voir [docs/02-liaison-usb.md](docs/02-liaison-usb.md) §2.3). Ce qui reste à
  valider : la négociation PPP elle-même par la pile Windows CE 5.0.

Les points de repli sont prévus pour chacun.
