<div align="center">

<img src="docs/images/hero.svg" alt="INVENTAIRE — la plateforme AI-Native d'intelligence frigorifique" width="100%">

# 🧊🧠 INVENTAIRE — **The AI-Native Fridge Intelligence Platform** 🚀✨

### *Le premier **AI Inventory Framework** 🤖 **AI-First**, **AI-Driven**, **Agent-Ready** et **Edge-Deployed** — qui tourne sur un terminal Windows CE de 2005.* 🔮

<br>

[![AI-Native](https://img.shields.io/badge/🧠_AI-Native-2ff0c8?style=for-the-badge&labelColor=04070d)](#-la-pile-ai-native)
[![AI-First](https://img.shields.io/badge/✨_AI-First-8b7cff?style=for-the-badge&labelColor=04070d)](#-le-pitch)
[![MCP Ready](https://img.shields.io/badge/🤖_MCP-Native-ff7ad9?style=for-the-badge&labelColor=04070d)](#-agentic-layer--le-frigo-comme-outil-pour-un-llm)
[![Edge AI](https://img.shields.io/badge/📡_Edge-AI-ffb347?style=for-the-badge&labelColor=04070d)](#-edge-ai-capture--le-terminal)
[![Zero Prompt](https://img.shields.io/badge/🪄_Zero-Prompt-49b8ff?style=for-the-badge&labelColor=04070d)](#-zero-prompt-ingestion)

[![Tests](https://img.shields.io/badge/tests-180%20passing-3fe08a?style=flat-square&logo=github-actions&logoColor=white)](.github/workflows/ci.yml)
[![Python](https://img.shields.io/badge/Python-3.11%20→%203.14-3776AB?style=flat-square&logo=python&logoColor=white)](requirements.txt)
[![.NET CF](https://img.shields.io/badge/.NET_CF-2.0-512BD4?style=flat-square&logo=dotnet&logoColor=white)](frontend/)
[![Windows CE](https://img.shields.io/badge/Windows_CE-5.0-0078D6?style=flat-square&logo=windows&logoColor=white)](docs/01-terminal-skorpio.md)
[![SQLite](https://img.shields.io/badge/SQLite-WAL-003B57?style=flat-square&logo=sqlite&logoColor=white)](backend/inventaire/db.py)
[![Docker](https://img.shields.io/badge/Docker-rootless-2496ED?style=flat-square&logo=docker&logoColor=white)](compose.yaml)
[![Open Food Facts](https://img.shields.io/badge/Open_Food_Facts-connected-ff8714?style=flat-square)](https://openfoodfacts.org)
[![License](https://img.shields.io/badge/license-MIT-2ff0c8?style=flat-square)](LICENSE)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-ff7ad9?style=flat-square)](#-contribuer)
[![Made with](https://img.shields.io/badge/made_with-❤️_et_🧠-ff4d5e?style=flat-square)](#)

<br>

**[⚡ Démarrer](#-quickstart-en-30-secondes) · [🧠 AI Stack](#-la-pile-ai-native) · [🤖 MCP](#-agentic-layer--le-frigo-comme-outil-pour-un-llm) · [📊 Analytics](#-cognitive-analytics--le-poste-de-contrôle) · [📱 Edge](#-edge-ai-capture--le-terminal) · [🌈 55 Savers](#-55-generative-screensavers) · [🔌 API](#-lapi) · [📚 Docs](#-la-documentation)**

</div>

---

## 🎯 Le pitch

> 🥑 **Vous ouvrez le frigo. Vous ne savez pas ce qu'il y a dedans.**
> 🗑️ **Un tiers de ce que vous achetez finit à la poubelle.**
> 🧠 **INVENTAIRE règle ça — sans une seule invite à taper.**

**INVENTAIRE** est une **🚀 AI-Native Food Intelligence Platform** 🧊 qui transforme un
**📡 edge device industriel** en **🦾 point de capture autonome** : vous bippez un produit
devant le frigo, et **⚡ en une demi-seconde** l'**🧠 intelligence layer** vous rend son
nom, son Nutri-Score, son groupe NOVA, sa photo, son stock et ses dates de péremption.
Un geste. **🪄 Zéro clavier. Zéro invite. Zéro friction.**

Puis — et c'est là que la **🔮 magie agentique** opère — **🤖 votre LLM local se branche
dessus en MCP** et vous propose le dîner de ce soir avec ce qui périme demain.

<table>
<tr>
<td width="33%" align="center">

### 📡 **AI-DRIVEN CAPTURE**
Une gâchette, un bip.<br>
**🪄 Zero-Prompt Ingestion**<br>
sur un PXA270 de 2005.

</td>
<td width="33%" align="center">

### 📊 **COGNITIVE ANALYTICS**
10 KPI 📈 temps réel,<br>
**⚡ WebSocket-native**,<br>
frigo dessiné 🧊 en direct.

</td>
<td width="33%" align="center">

### 🤖 **AGENTIC LAYER**
7 outils 🔧 MCP,<br>
**🧠 LLM-Ready**,<br>
lecture seule 🔒 by design.

</td>
</tr>
</table>

<div align="center">

### 🆚 INVENTAIRE **vs.** les solutions legacy 🦕

| | 🦕 App de frigo classique | 🧠 **INVENTAIRE (AI-Native)** |
|:---|:---:|:---:|
| 🪄 Saisie d'un produit | 📝 taper 6 champs | ⚡ **une gâchette** |
| 🤖 Interrogeable par un LLM | ❌ | ✅ **MCP-Native** |
| 📡 Fonctionne hors du cloud | ❌ | ✅ **100 % local, 100 % souverain** |
| 🔒 Vos données partent chez qui ? | 🕵️ *« nos partenaires »* | 🏠 **un fichier SQLite chez vous** |
| 🧊 Frigo visualisé en temps réel | ❌ | ✅ **WebSocket, 5 vues** |
| 🕹️ Économiseurs d'écran démoscène | ❌ | ✅ **55** |
| 💸 Abonnement mensuel | 💳 9,99 €/mois | 🆓 **MIT** |
| 🦖 Tourne sur du matériel de 2005 | ❌ | ✅ **Windows CE 5.0** |

</div>

---

## ⚡ Quickstart en 30 secondes

> 🐳 **One command. Zero config. Full stack.** 🚀

```bash
git clone git@github.com:ggodefroid/inventaire.git && cd inventaire
./demarrer.sh          # 🪄 prépare, construit, compile le client CE, démarre, vérifie
```

```console
✅ en service.

    http://192.168.1.24:8080/     🖥️  <- à saisir dans les réglages du terminal
    http://192.168.1.24:8081/     📊  le poste de contrôle (Cognitive Analytics)
    http://192.168.1.24:8082/mcp  🤖  serveur MCP, pour le LLM local
```

<details>
<summary>🧰 <b>Sans Docker ? Trois processus, trois ports.</b></summary>

<br>

```bash
# ⚙️ 1. AUTONOMOUS CORE — le serveur du terminal (seul à écrire)
pip install --user pillow
python3 backend/serveur.py                     # 🖥️  0.0.0.0:8080

# 📊 2. COGNITIVE ANALYTICS — le poste de contrôle (lecture seule)
python3 -m venv .venv
.venv/bin/pip install -r requirements.txt      # flask, flask-sock, pillow
.venv/bin/python backend/vitrine.py            # 📈 0.0.0.0:8081

# 🤖 3. AGENTIC LAYER — le serveur MCP (lecture seule)
.venv/bin/python backend/mcp.py                # 🧠 0.0.0.0:8082
```

Les trois ne partagent **qu'un seul fichier SQLite** 🗄️ et rien ne les oblige à démarrer
ni à tomber ensemble. **Loosely coupled by design.** 🔗

</details>

<details>
<summary>📱 <b>Déployer sur le terminal (Edge Deployment)</b></summary>

<br>

Il n'y a **rien à compiler à la main** 🪄 : le conteneur `client` le fait à chaque lancement.

```bash
./demarrer.sh               # 🏗️  le client est compilé, puis les services démarrent
./demarrer.sh --apercu      # 📸 + une capture de chaque écran -> dist/apercu/
./demarrer.sh --veille      # 🌈 rend les 55 économiseurs -> dist/veille/
```

Une fois le Wi-Fi du terminal opérationnel, le plus simple est son propre navigateur :
**Internet Explorer → `http://<ip du PC>:8080/telecharger`**, doigt maintenu sur le lien,
*Enregistrer la cible sous…*

Déposez `Inventaire.exe` et `inventaire.ini` dans un dossier de la mémoire **persistante**
(`\FlashDisk\Frigo`, `\Backup\Frigo` selon le modèle) : un *cold boot* efface tout le reste.
Détails et réglages du lecteur : 📖 **[docs/01-terminal-skorpio.md](docs/01-terminal-skorpio.md)**

</details>

---

## 🧠 La pile AI-Native

> *Five layers. Three processes. One SQLite file. Zero cloud.* 🏗️

<div align="center">
<img src="docs/images/stack-ia.svg" alt="La pile AI-Native d'INVENTAIRE" width="100%">
</div>

<br>

```mermaid
flowchart TB
    T["📡 <b>00 · EDGE CAPTURE</b><br/>Datalogic Skorpio · Windows CE 5.0<br/>240 × 320 · une gâchette, zéro invite"]
    S["⚙️ <b>01 · AUTONOMOUS CORE</b> · :8080<br/>serveur.py — le seul à écrire"]
    OFF["🌍 Open Food Facts<br/>proxy HTTP → HTTPS"]
    DB[("🗄️ SQLite · WAL<br/>backend/donnees/inventaire.db")]
    V["📊 <b>02 · COGNITIVE ANALYTICS</b> · :8081<br/>vitrine.py — 10 KPI, 5 vues"]
    B["🌐 Navigateurs"]
    M["🤖 <b>03 · MCP ORCHESTRATION</b> · :8082<br/>mcp.py — 7 outils, 3 ressources, 3 invites"]
    L["🧬 <b>04 · AGENTIC EXPERIENCE</b><br/>LLM local — Ollama · LM Studio · Claude"]

    T -- "📶 HTTP clair, jamais TLS" --> S
    S -- "🔐 HTTPS à sa place" --> OFF
    S <--> DB
    DB -. "🔒 mode=ro" .-> V
    DB -. "🔒 mode=ro" .-> M
    V -- "⚡ WebSocket" --> B
    M -- "📡 JSON-RPC 2.0" --> L
    L -. "🛒 inscrire une course" .-> S

    classDef edge fill:#150a0c,stroke:#ff4d5e,color:#ffd7db
    classDef core fill:#06110c,stroke:#3fe08a,color:#d6ffe9
    classDef ana  fill:#05100f,stroke:#2ff0c8,color:#d6fff6
    classDef mcp  fill:#070d1a,stroke:#49b8ff,color:#d6ecff
    classDef ag   fill:#0a0818,stroke:#ff7ad9,color:#ffd9f2
    class T edge
    class S,OFF,DB core
    class V,B ana
    class M mcp
    class L ag
```

| 🏷️ Couche | 🎛️ Rôle | 🚪 Port | ✍️ Écrit ? |
|:---|:---|:---:|:---:|
| 🧬 **04 · Agentic Experience** | le LLM raisonne sur le stock réel | — | — |
| 🤖 **03 · MCP Orchestration** | 7 outils, 3 ressources, 3 invites | `8082` | 🛒 courses seulement |
| 📊 **02 · Cognitive Analytics** | 10 KPI, 5 vues, diffusion temps réel | `8081` | ❌ **jamais** |
| ⚙️ **01 · Autonomous Core** | SQLite, Open Food Facts, photos, FEFO | `8080` | ✅ |
| 📡 **00 · Edge Capture** | une gâchette, zéro invite | — | via `8080` |

---

## 🪄 Zero-Prompt Ingestion

> *Le geste le plus court entre un produit et une base de données.* ⚡

<div align="center">
<img src="docs/images/terminal-parcours-scan.png" alt="Le parcours de scan : accueil, fiche produit, saisie de date, confirmation" width="100%">
<sub>🔫 <b>bip</b> → 🧾 <b>fiche enrichie</b> → 📅 <b>date</b> → ✅ <b>rangé</b> — captures réelles du client, rendues en 240 × 320</sub>
</div>

<br>

```mermaid
sequenceDiagram
    autonumber
    actor H as 👤 Humain
    participant L as 🔫 Lecteur
    participant T as 📱 Terminal CE
    participant C as ⚙️ Core :8080
    participant O as 🌍 Open Food Facts

    H->>L: une pression sur la gâchette
    L->>T: rafale de 13 chiffres
    Note over T: 🧠 détection de cadence —<br/>2 caractères en 90 ms = une rafale
    T->>C: GET /api/scan?code=…&fmt=kv
    C->>C: 🔑 clé de contrôle reconstituée si besoin
    C->>O: 🔐 HTTPS (le terminal ne sait plus le faire)
    O-->>C: fiche produit
    C->>C: 🖼️ photo → BMP palettisé, préchauffée
    C-->>T: nom · Nutri-Score · NOVA · stock · lots · aujourdhui= · heure=
    T-->>H: ⚡ affichage immédiat
    T->>C: GET /api/image (déjà en cache — 8 ms)
```

<table>
<tr><td width="50%">

### 🎯 **Cadence-Aware Input**
Le programme distingue une **rafale de gâchette** 🔫 d'une touche isolée ⌨️ à sa cadence
de frappe — deux caractères apparus dans le même battement de **90 ms**, aucun doigt ne
fait cela. **On peut donc rebipper depuis n'importe quel écran.** 🔁

</td><td width="50%">

### 🔑 **Self-Healing Barcodes**
Beaucoup de décodeurs coupent le dernier chiffre d'un EAN-13 sans le dire. Le serveur le
**reconstitue en trois temps** — 🧮 calcul, 📚 catalogue, 🧠 mémoire — et retient la
correspondance : au bout de deux corrections, il essaie d'emblée la complétion.

</td></tr>
<tr><td width="50%">

### ⏰ **Server-Authoritative Time**
L'horloge d'un terminal CE dérive et **repart à zéro au *cold boot*** 💀. Chaque réponse
porte `aujourdhui=` et `heure=` : « dans 7 jours » reste juste même quand le terminal se
croit en 2005. 🕰️

</td><td width="50%">

### 🖼️ **Predictive Image Prefetch**
La photo part se télécharger **dès le bip** ⚡, pendant que l'utilisateur lit le
Nutri-Score. Quand l'écran la réclame, **elle est déjà là** — 8 ms au lieu d'un aller-retour
réseau. 🚀

</td></tr>
</table>

<details>
<summary>🔬 <b>Deep dive — les six décisions qui portent l'architecture</b></summary>

<br>

**1. 📶 Le terminal ne parle qu'en HTTP clair.** Sa pile TLS date de 2005 et ne négocie
plus rien de ce qu'exige `openfoodfacts.org`. Le serveur fait l'appel HTTPS à sa place, et
lui rend le résultat réduit à ce qui tient sur 240 pixels.

**2. 🎨 Les photos arrivent en BMP palettisé, non compressé.** Le décodage JPEG du Compact
Framework dépend de codecs qui peuvent manquer de l'image OS — et leur absence se manifeste
par une exception, pas par une image dégradée. Le client sait relire le BMP octet par octet
si jamais `Bitmap` refuse.

Palettisé, et non en couleurs vraies : le même cadre pèse **le tiers** — 41 ko au lieu de
120 pour la grande photo — pour une perte invisible sur cet écran. Sur une radio 802.11b,
ce tiers est la différence entre une demi-seconde d'attente et un affichage immédiat.

**3. ⏰ Le serveur date les réponses.** L'horloge d'un terminal Windows CE dérive et repart
à zéro au *cold boot*. Chaque réponse porte `aujourdhui=` et `heure=`, et le client cale
calendrier et horloge dessus.

**4. ⌨️ Le code-barres n'est jamais assemblé à la main.** Windows produit deux flux pour une
touche — `WM_KEYDOWN`, puis `WM_CHAR` fabriqué par `TranslateMessage`. Tant qu'on assemble
le code soi-même, on dépend de leur entrelacement, et sur la rafale d'un lecteur rien ne le
garantit : un chiffre se perd, sans erreur ni trace.

On fait donc comme un bloc-notes : une zone de saisie invisible accumule nativement, et le
programme **lit son contenu une fois la file de messages vidée**. `Entrée` ne conclut plus
la lecture, elle annonce que la rafale se termine ; le relevé a lieu 170 ms plus tard.

**5. 🔊 Les sons sont synthétisés, pas joués depuis des fichiers.** `MessageBeep` ne donne
que cinq sons figés, souvent un seul sur un terminal industriel. `PlaySound` de `coredll`
accepte en revanche un WAV **en mémoire** : le programme fabrique donc ses **quatorze ondes**
lui-même, en 8 bits à 11 kHz, et n'a rien à déposer sur le terminal. La quatorzième est une
**voix** — aucune image de Windows CE n'embarque de synthèse vocale, alors un synthétiseur à
formants de cent lignes prononce « Inventaire » au démarrage. 🗣️

**6. 🔑 Une clé de contrôle absente est reconstituée.** Douze chiffres qui ne forment pas un
UPC-A valide sont forcément un EAN-13 amputé. Quand le calcul ne tranche pas, les deux
lectures sont proposées à Open Food Facts — celle qui existe gagne. La correspondance
trouvée est retenue. Le réglage du lecteur reste préférable :
[docs/01](docs/01-terminal-skorpio.md) §1.2.

</details>

---

## 📡 Edge AI Capture — le terminal

<div align="center">

### 🦾 Le hardware

| 🔧 | |
|---|---|
| 🏭 **Modèle** | Datalogic **Skorpio**, Windows CE 5.0 |
| 🧮 **Processeur** | Marvell PXA270 **520 MHz** *(pas de FPU)* |
| 🖥️ **Écran** | **240 × 320** tactile |
| ⌨️ **Clavier** | 28 touches numériques ou 38 alphanumériques |
| 📶 **Liaison** | Wi-Fi 802.11 b/g |
| 💾 **Runtime** | .NET Compact Framework **2.0** |

</div>

<div align="center">
<img src="docs/images/terminal-parcours-frigo.png" alt="Liste du frigo avec vignettes, colonne de retrait, photo plein écran, réglages" width="100%">
<sub>🧊 <b>tout le frigo</b> · ➖ <b>colonne de retrait</b> · 🖼️ <b>photo plein écran</b> · ⚙️ <b>réglages</b></sub>
</div>

<br>

<div align="center">
<img src="docs/images/terminal-parcours-detail.png" alt="Code saisi, segment de mois, sélection dans la liste, produit inconnu" width="100%">
<sub>🔢 <b>code saisi</b> · 📅 <b>date champ par champ</b> · 🎯 <b>sélection</b> · ❓ <b>produit inconnu</b></sub>
</div>

<br>

### ✨ Ce que fait le client, en douze points

<table>
<tr><td width="50%">

**🏷️ Les témoins viennent d'Open Food Facts** — Nutri-Score, groupe NOVA, Eco-Score, et
quatre jauges à trois crans (gras, AG saturés, sucres, sel). Trois crans rouges sur le sucre
se lisent sans savoir ce qu'est un gramme pour cent grammes.

**📖 `4` ouvre la fiche complète** — table nutritionnelle, additifs, ingrédients, origine,
catégories. **`5` montre la photo en grand.** 🖼️

**🖼️ La liste montre les photos**, note en pastille dans le coin. Téléchargement **et**
décodage ont lieu sur un fil réservé : le fil de l'interface ne reçoit qu'une image prête.

**⌨️ Tout se pilote au clavier.** Le terminal se tient d'une main, la gâchette sous l'index.
Le tactile reste un second chemin, jamais le seul.

**🔋 L'heure et la batterie** sont dans le bandeau, sur tous les écrans.

**🚦 La couleur porte l'urgence** partout : rouge sous trois jours, vert au-delà de vingt.

</td><td width="50%">

**📅 La péremption s'ajuste champ par champ.** `↑` `↓` choisit le jour, le mois ou l'année ;
`←` `→` corrige de ±1. Et si le produit a déjà été rangé, **le serveur propose la durée
constatée la dernière fois** 🧠 — il ne reste qu'à valider.

**🗓️ La péremption peut n'avoir qu'un mois.** « avant fin 11/2026 » s'enregistre à cette
précision-là. **On n'invente pas un jour que le produit n'a pas.**

**😴 Il se met en veille** après une minute — **55 économiseurs** 🌈, relayés toutes les
30 secondes. Une touche réveille **et** agit.

**🔄 Il se met à jour tout seul.** Au démarrage, si `dist/` contient un binaire différent du
sien, il le propose, le télécharge et se remplace. L'ancien est gardé en `.old`. 🛡️

**🔊 Ça fait du bruit.** Quatorze effets synthétisés : fanfare, bip, arpèges montants à
l'ajout, descendants au retrait, alerte devant un produit périmé, carillon à la mise en
charge — et une voix qui dit son nom au démarrage.

**📉 Les retraits suivent le FEFO** — sort toujours ce qui périme le plus tôt.

</td></tr>
</table>

<details>
<summary>🖼️ <b>Les 19 écrans, un par un</b></summary>

<br>

<div align="center">

| 🏠 Accueil | 🔢 Code saisi | 🧾 Fiche produit | 🖼️ Photo |
|:---:|:---:|:---:|:---:|
| <img src="docs/images/terminal/01-accueil.png" width="170"> | <img src="docs/images/terminal/02-code-saisi.png" width="170"> | <img src="docs/images/terminal/03-article.png" width="170"> | <img src="docs/images/terminal/04-photo-grande.png" width="170"> |

| ↩️ Retour fiche | 📅 Saisie date | ➕ Jour +3 | 📆 Segment mois |
|:---:|:---:|:---:|:---:|
| <img src="docs/images/terminal/05-retour-article.png" width="170"> | <img src="docs/images/terminal/06-saisie.png" width="170"> | <img src="docs/images/terminal/07-jour-plus-3.png" width="170"> | <img src="docs/images/terminal/08-segment-mois.png" width="170"> |

| ➕ Mois +1 | 🔢 Segment quantité | ✌️ Quantité 2 | ✅ Ajouté |
|:---:|:---:|:---:|:---:|
| <img src="docs/images/terminal/09-mois-plus-1.png" width="170"> | <img src="docs/images/terminal/10-segment-quantite.png" width="170"> | <img src="docs/images/terminal/11-quantite-2.png" width="170"> | <img src="docs/images/terminal/12-ajoute.png" width="170"> |

| 🧊 Liste + vignettes | 🎯 Sélection | 🖼️ Photo depuis liste | ↩️ Retour liste |
|:---:|:---:|:---:|:---:|
| <img src="docs/images/terminal/13-liste-vignettes.png" width="170"> | <img src="docs/images/terminal/14-liste-selection.png" width="170"> | <img src="docs/images/terminal/15-photo-depuis-liste.png" width="170"> | <img src="docs/images/terminal/16-retour-liste.png" width="170"> |

| ➖ Colonne retrait | ⚙️ Réglages | ❓ Produit inconnu | |
|:---:|:---:|:---:|:---:|
| <img src="docs/images/terminal/17-colonne-retrait.png" width="170"> | <img src="docs/images/terminal/18-reglages.png" width="170"> | <img src="docs/images/terminal/19-inconnu.png" width="170"> | |

</div>

> 🧪 Ces captures ne sont pas des maquettes : **les mêmes sources C# compilées pour Mono**,
> lancées dans un serveur X virtuel en 240 × 320, déroulant un scénario complet contre un
> vrai serveur. `./demarrer.sh --apercu` les régénère.

</details>

---

## 📊 Cognitive Analytics — le poste de contrôle

> *Ten KPI. Five views. Real-time. Read-only. Zero build step.* 📈

<div align="center">
<img src="docs/images/vitrine/tableau-sombre.png" alt="Le tableau de bord en thème sombre : 10 KPI, répartitions, séries" width="100%">
<sub>🌙 <b>TABLEAU</b> — 10 KPI avec la formule affichée sous chaque valeur</sub>
</div>

<br>

<div align="center">
<img src="docs/images/vitrine/tableau-clair.png" alt="Le même tableau de bord en thème clair" width="100%">
<sub>☀️ Le même, en thème clair — <b>le thème suit le système, et se force d'un clic</b></sub>
</div>

<br>

### 🧊 Un frigo qui n'existe pas

<div align="center">
<img src="docs/images/vitrine/frigo-sombre.png" alt="La vue frigo : les articles dessinés sur les étagères et dans la porte" width="100%">
<sub>🧊 <b>FRIGO</b> — la position d'un article dérive de son code-barres : <b>elle ne bouge pas d'une visite à l'autre</b></sub>
</div>

<br>

<div align="center">
<img src="docs/images/vitrine/stock-sombre.png" alt="La vue stock : tableau trié et filtrable des références" width="100%">
<sub>📦 <b>STOCK</b> — tri, filtres, recherche, et un clic ouvre la fiche complète</sub>
</div>

<br>

<div align="center">
<img src="docs/images/vitrine/flux.png" alt="La vue flux : le journal des mouvements en temps réel" width="100%">
<sub>⚡ <b>FLUX</b> — le journal des mouvements, poussé par WebSocket à chaque écriture</sub>
</div>

<br>

<details>
<summary>☀️ <b>Les mêmes vues en thème clair</b></summary>

<br>

<div align="center">
<img src="docs/images/vitrine/frigo-clair.png" alt="La vue frigo en thème clair" width="100%">
<sub>🧊 <b>FRIGO</b> — thème clair</sub>
<br><br>
<img src="docs/images/vitrine/stock-clair.png" alt="La vue stock en thème clair" width="100%">
<sub>📦 <b>STOCK</b> — thème clair</sub>
</div>

<br>

Le thème **suit le système par défaut** 🖥️, et se force d'un clic sur ☾ / ☀ dans le bandeau.
Le choix est retenu dans `localStorage` et **appliqué avant le premier pixel** — sinon la page
s'affiche en sombre puis bascule sous les yeux. 👀

</details>

<br>

<div align="center">
<img src="docs/images/vitrine/mobile.png" alt="Le poste de contrôle sur téléphone, en clair et en sombre" width="70%">
<sub>📱 <b>Responsive</b> — du téléphone à l'écran large, et pilotable au clavier : <code>1</code>–<code>5</code>, <code>/</code>, <code>Échap</code></sub>
</div>

<br>

### 📈 Les 10 KPI

| # | 🏷️ KPI | 🧮 Formule | 💡 Ce qu'il dit |
|:---:|:---|:---|:---|
| 01 | 🌡️ **Fraîcheur** | `moy( min(jours/30, 1) ) × 100` | marge restante avant péremption |
| 02 | 🔋 **Autonomie calorique** | `kcal totales / 2000` | tenue du frigo pour une personne |
| 03 | 📚 **Couverture catalogue** | `fiches OFF / références` | part du stock que le catalogue documente |
| 04 | ⏰ **Pression 72 h** | `unités < 72 h / unités` | ce qu'il faut manger cette semaine |
| 05 | 🏭 **Transformation moyenne** | `moy(NOVA) pondérée` | 1 brut, 4 ultra-transformé |
| 06 | 🔄 **Rotation** | `sorties 30 j / 30 × 7` | débit de consommation constaté |
| 07 | ⚡ **Densité calorique** | `kcal / masse × 100` | au-dessus de 250, le frigo est gras |
| 08 | 🎲 **Diversité de Shannon** | `entropie / log2(références)` | 100 = toutes les réfs à quantité égale |
| 09 | 📦 **Profondeur de stock** | `unités / références` | exemplaires moyens par produit |
| 10 | 🗑️ **Exposition au gaspillage** | `unités périmées / unités` | **zéro est la seule valeur acceptable** |

<details>
<summary>🔬 <b>Deep dive — comment le temps réel tient sans rien installer</b></summary>

<br>

**🔒 Lecture seule, trois fois.** Base ouverte en `mode=ro`, connexion en `PRAGMA query_only`,
et **aucune route qui déclare autre chose que `GET`**. Un test parcourt la table de routage
de Flask et échoue si un `POST` y apparaît.

**⚡ Temps réel sans rien installer.** `PRAGMA data_version` change dès qu'une **autre
connexion** valide une transaction. Un thread le relit chaque seconde, recalcule l'instantané
**une fois**, et le pousse à tous les navigateurs connectés : **dix visiteurs ne font pas dix
calculs**. Si la WebSocket ne passe pas, la page se rabat sur `GET /api/etat`.

**📉 La charge du frigo est reconstituée** jour par jour, à rebours depuis l'état actuel et
le journal des mouvements : **aucune table ne garde l'historique du niveau**.

**🎭 Les chiffres utiles et les autres**, nettement séparés. D'un côté la masse, l'énergie,
les macros, les marques, les additifs, les allergènes. De l'autre, combien de morceaux de
sucre 🍬 dorment dans le frigo, combien de kilomètres à pied 🚶 il représente, et son
entropie de Shannon.

Détail complet : 📖 **[docs/04-vitrine.md](docs/04-vitrine.md)**

</details>

---

## 🤖 Agentic Layer — le frigo comme outil pour un LLM

> *Your fridge is now a tool. Your LLM is now a chef.* 🧑‍🍳🧠

```bash
.venv/bin/python backend/mcp.py                # 🤖 écoute 0.0.0.0:8082
```

Troisième processus, troisième port. Il parle le **🔌 Model Context Protocol** : un modèle
local — **Ollama**, **LM Studio**, **Claude**, n'importe quel client MCP — **interroge le
frigo lui-même** au lieu qu'on lui recopie l'inventaire dans son invite.

```json
{
  "mcpServers": {
    "frigo": {
      "type": "http",
      "url": "http://192.168.1.24:8082/mcp"
    }
  }
}
```

### 🔧 Les 7 outils agentiques

| 🛠️ Outil | 🎯 Ce qu'il rend | ✍️ |
|:---|:---|:---:|
| 🧾 `resume` | l'état du frigo en une phrase | 🔒 |
| 📦 `inventaire` | la liste complète : quantité, nom, marque, péremption | 🔒 |
| ⏰ `bientot_perime` | ce qui périme sous *n* jours | 🔒 |
| 🔍 `chercher` | une référence par nom, marque ou code | 🔒 |
| 📖 `details_produit` | tout ce qu'Open Food Facts sait d'un produit | 🔒 |
| 🛒 `liste_courses` | la liste de courses partagée | 🔒 |
| ✏️ `ajouter_aux_courses` | **le seul outil qui écrit** — et il relaie au serveur | ✅ |

### 📚 3 ressources · 💬 3 invites toutes faites

```
frigo://inventaire     📦  l'état du stock
frigo://courses        🛒  la liste de courses
frigo://urgences       🚨  ce qui périme le plus tôt
```

| 💬 Invite | 🍽️ |
|:---|:---|
| `recette` | 🧑‍🍳 **« une recette avec ce que j'ai »** |
| `menu_semaine` | 📅 **« un menu pour la semaine »** |
| `anti_gaspi` | ♻️ **« sauver ce qui va périmer »** |

<div align="center">

> ### 🔒 **Un modèle ne peut pas vider un frigo.**
> ### **Il peut seulement proposer d'y remettre quelque chose.**

</div>

Il lit la base en `mode=ro`, comme le poste de contrôle. **Human-in-the-loop by design.** 🧑‍⚖️

<details>
<summary>🧠 <b>Pourquoi le modèle lit du texte, et pas du JSON</b></summary>

<br>

```
2x Nutella (Ferrero) 400 g - périme dans 5 j [Nutri-Score E]
```

… se raisonne mieux — **et coûte moins de jetons** 🪙 — qu'une structure imbriquée. Les
sept outils rendent donc des lignes lisibles, pas des objets. **Token-efficient by design.**

Détail complet : 📖 **[docs/06-mcp.md](docs/06-mcp.md)**

</details>

---

## 🛒 AI Shopping Intelligence

> *Le geste qui compte : il ne reste plus de beurre, on bippe le paquet vide au-dessus de la poubelle, et c'est inscrit.* 🗑️→🛒

<div align="center">
<img src="docs/images/vitrine/courses.png" alt="La liste de courses sur le poste de contrôle, avec panier et suggestions" width="100%">
<sub>🛒 <b>COURSES</b> — la même liste sur le terminal (<code>F4</code>), sur le site, et dans le serveur MCP</sub>
</div>

<br>

<table>
<tr><td width="50%">

**🔫 Pas de menu, pas de clavier** — la gâchette suffit, comme pour le reste.

**➕ Un article rebippé voit sa quantité augmenter** au lieu d'apparaître deux fois.

**✅ Cocher plutôt que supprimer** — un article coché reste visible, barré, jusqu'à ce qu'on
vide le panier. C'est ce qui permet de vérifier qu'on n'a rien oublié avant la caisse.

</td><td width="50%">

**🧠 La liste ne se déduit pas du stock.** Un pot de moutarde entamé y a sa place alors
qu'il est en stock ; un surplus de yaourts n'y en a aucune. **Seul un humain — ou le modèle
à qui on demande un menu — sait.**

**💡 Les suggestions existent**, à partir de ce qui manque ou périme. **Mais il faut
cliquer.** Jamais d'ajout automatique.

</td></tr>
</table>

---

## 🌈 55 Generative Screensavers

> *Un terminal posé sur un plan de travail mérite mieux qu'une image figée.* 🕹️

<div align="center">
<img src="docs/images/veille/planche-55.png" alt="Les 55 économiseurs d'écran, rendus hors terminal" width="100%">
<sub>🎨 <b>Les 55</b>, tirés sans remise et relayés toutes les 30 secondes — rendus par <code>./demarrer.sh --veille</code></sub>
</div>

<br>

<div align="center">
<img src="docs/images/veille/vedettes.png" alt="Cinq économiseurs en grand : plasma, feu, tunnel, écran bleu, Tetris" width="100%">
<sub>🔥 <b>Plasma</b> · 🔥 <b>Feu de Doom</b> · 🌀 <b>Tunnel</b> · 💀 <b>Écran bleu</b> · 🧱 <b>Tetris</b></sub>
</div>

<br>

| 🎭 Famille | 🎨 Les effets |
|---|---|
| 🌌 **Démoscène** | plasma, feu de Doom, tunnel, rotozoom, metaballs, Lissajous, spirographe, attracteur de Lorenz, Sierpinski, Mandelbrot, Julia, barres copper, sinus scroller, moiré, boids, jeu de la vie, règle 30, sable, ondes, lampe à lave |
| 🕹️ **Bornes d'arcade** | Pong, casse-briques, Snake, Tetris, Space Invaders, le glouton de 1980, Astéroïdes, Simon, démineur, labyrinthe 3D |
| 💻 **Écrans de machine** | écran bleu, invite MS-DOS, défragmenteur, ScanDisk, installation, journal de démarrage, vidage hexadécimal, test mémoire, fenêtres volantes, oscilloscope, vumètre, mire, neige, horloge à aiguilles, horloge à volets, horloge binaire, tubes Nixie, code-barres, télex, égaliseur |
| ⭐ **Les cinq d'origine** | pluie de pixels, champ d'étoiles, logo rebondissant, Mystify, tuyaux |

<details>
<summary>⚙️ <b>Deux contraintes gouvernent tout ce code — et c'est ce qui lui donne sa forme</b></summary>

<br>

**🚫 Aucun nombre à virgule.** Le PXA270 n'a pas d'unité de calcul flottant : chaque `double`
est émulé par la bibliothèque, à des **centaines de cycles l'opération**. Un sinus par pixel
donnerait *une image par seconde*. Tout passe donc par une **table de sinus entière** —
256 pas, amplitude 1024 — exactement ce qu'on faisait sur Amiga, et pour la même raison. 🎛️

**🚫 Aucun pixel individuel.** `SetPixel` verrouille l'image à chaque appel, et **76 800
appels par image** sont hors de portée. Les effets qui couvrent l'écran travaillent en blocs
de quatre à huit pixels, peints en rectangles pleins. **C'est aussi ce qui leur donne leur
grain d'époque.** 🕹️

Le moteur ne sait rien d'aucun effet : il tient une table, tire dedans, appelle `Avancer`
puis `Peindre`. **Ajouter un économiseur, c'est ajouter une classe et une ligne.** Un effet
qui lèverait une exception est remplacé par le suivant sans que l'application s'en aperçoive. 🛡️

```bash
./demarrer.sh --veille      # 🌈 rend les 55 hors terminal -> dist/veille/*.png
```

Ce banc échoue si l'un d'eux lève une exception **ou rend un écran noir** — deux pannes
qu'on ne verrait autrement qu'en attendant trente secondes devant l'appareil, 55 fois. 🧪

</details>

---

## 🔌 L'API

> *JSON, ou `clé=valeur` avec `&fmt=kv` — le format que lit le terminal.* 📡

```bash
curl 'http://localhost:8080/api/scan?code=3017620422003&fmt=kv'
curl -X POST -d 'code=3017620422003&qte=2&jours=30' http://localhost:8080/api/ajouter
curl 'http://localhost:8080/api/bientot?jours=3'
```

### ⚙️ Autonomous Core — `:8080`

| 🚪 Route | 🎯 Rôle |
|---|---|
| `GET /api/ping` | 💓 vivant, version, compteurs |
| `GET /api/scan` | 🔍 fiche complète d'un code : produit, indicateurs, stock, lots |
| `GET /api/detail` | 📖 tout ce qu'Open Food Facts sait : nutrition, additifs, ingrédients |
| `POST /api/ajouter` | ➕ `code`, `qte`, `peremption=AAAA-MM-JJ` ou `jours=N` |
| `POST /api/retirer` | ➖ `code`, `qte`, `lot` optionnel (**FEFO** par défaut) |
| `POST /api/lot` | ✏️ corriger la quantité ou la date d'un lot |
| `POST /api/nommer` | 🏷️ libellé manuel, pour les codes absents du catalogue |
| `GET /api/inventaire` | 📦 tout le stock, un poste par référence |
| `GET /api/bientot` | ⏰ ce qui périme sous *n* jours |
| `GET /api/image` | 🖼️ photo retaillée en BMP, JPEG ou PNG, cache mémoire + disque |
| `GET /` | 🖥️ tableau de bord navigateur |
| `GET /api/maj` | 🔄 version du binaire dans `dist/`, pour la mise à jour du terminal |
| `GET /telecharger` | 📥 `dist/` servi au navigateur du terminal |

### 📊 Cognitive Analytics — `:8081` *(toutes en lecture)*

| 🚪 Route | 🎯 Rôle |
|---|---|
| `GET /` | 🖥️ la page, une seule |
| `GET /flux` | ⚡ WebSocket : instantané, puis mise à jour à chaque mouvement |
| `GET /api/etat` | 📸 l'instantané complet : KPI, séries, postes, journal |
| `GET /api/article/<code>` | 📖 tout ce que la base sait d'une référence |
| `GET /photo/<code>` | 🖼️ photo retaillée en PNG, ou une silhouette |
| `GET /sante` | 💓 état du processus, pour une sonde d'hébergeur |

<div align="center">

> ### 🚨 **Une erreur métier revient en HTTP 200 avec `ok=0`, jamais en 4xx.**
> Sous Compact Framework, un code d'erreur lève une `WebException` — et le terminal ne
> saurait plus distinguer **« le serveur dit non »** de **« le serveur est tombé »**.

</div>

Détail complet : 📖 **[docs/03-api.md](docs/03-api.md)**

---

## 🐳 Production-Ready, Enterprise-Grade, Rootless

```bash
./demarrer.sh       # 🚀 prépare, construit, compile le client, démarre, vérifie
```

```mermaid
flowchart TB
    subgraph COMPOSE["🐳 compose.yaml — 4 conteneurs, 1 fichier SQLite"]
        C["🏗️ client<br/><i>mono-devel</i><br/>compile puis s'arrête"]
        S["⚙️ serveur :8080<br/><b>écrit</b>"]
        V["📊 vitrine :8081<br/><i>lit</i>"]
        M["🤖 mcp :8082<br/><i>lit</i>"]
        DB[("🗄️ backend/donnees/<br/>inventaire.db")]
    end
    C -- "📦 dist/Inventaire.exe" --> S
    S <--> DB
    DB -.-> V
    DB -.-> M
```

`demarrer.sh` **ne fait rien que `compose.yaml` ne sache faire** : il prépare ce que compose
suppose déjà prêt. Un dépôt fraîchement cloné n'a ni `.env`, ni `backend/donnees/`, ni
`dist/` — et laisser Docker créer ces dossiers lui-même **les rend à `root`**, ce que
personne ne remarque avant la première panne. 💥

| 🛡️ Durcissement | ✅ |
|---|---|
| 🚫 Conteneurs sans root | ✅ |
| 🔒 Racine en lecture seule | ✅ |
| ⚙️ Aucune capacité Linux | ✅ |
| 📂 Pas de volume nommé — la base reste consultable depuis l'hôte | ✅ |
| 🐳 Une seule image pour les trois services Python | ✅ |

<details>
<summary>⚠️ <b>Deux pièges valent d'être connus</b></summary>

<br>

Le programme les signale lui-même plutôt que de laisser SQLite le faire à sa place :

1. 👤 **L'UID du conteneur doit être celui du propriétaire de `backend/donnees/`** — rien ne
   traduit les identités à travers un montage lié.
2. 🕰️ **`TZ` décide de la date que le terminal affiche**, faute d'horloge fiable de son côté.

Le reste — podman sans privilèges, SELinux, sauvegarde à chaud, diagnostic — est dans
📖 **[docs/05-production.md](docs/05-production.md)**.

Sans conteneurs, deux unités systemd font le même travail :
[docs/04 §4.6](docs/04-vitrine.md#46-publier-sur-internet).

</details>

---

## 🧪 Tests & vérification

```bash
python3 -m unittest discover -s tests -t .
```

```console
........................................
----------------------------------------------------------------------
Ran 180 tests in 2.239s

OK ✅
```

<div align="center">

[![CI](https://img.shields.io/badge/CI-Python_3.11_→_3.14_+_pile_complète-3fe08a?style=for-the-badge&logo=github-actions&logoColor=white&labelColor=04070d)](.github/workflows/ci.yml)

</div>

### 🔗 Le test qui mérite un mot : `test_contrat_client.py`

Le client est en **C#**, le serveur en **Python**, et **rien ne les relie à la compilation**.
Renommer `contenance` en `quantite` côté serveur laisserait le terminal afficher une chaîne
vide — *pas d'erreur, pas de journal, juste une information qui disparaît de l'écran.* 👻

Ce test **lit les sources C#**, en extrait les **55 clés** qu'elles réclament, et vérifie que
le serveur les émet toutes. 🔒

<details>
<summary>✅ <b>État de la vérification — ce qui a été exécuté et prouvé</b></summary>

<br>

- ⚙️ le serveur **de bout en bout**, y compris contre l'API publique d'Open Food Facts en direct ;
- 🧪 **180 tests** : fusion des lots, sorties FEFO, normalisation des fiches, couche HTTP,
  contrat de clés C# ↔ Python, liste de courses, tout le site public, et le protocole MCP ;
- 🖼️ **la conversion des photos** : BMP palettisé 8 bits aux dimensions exactes demandées,
  relu octet par octet et comparé pixel à pixel — **40 000 pixels, aucun écart** — avec ce que
  décode une bibliothèque de référence ;
- ⚡ **le préchauffage** : la photo part chez Open Food Facts dès la résolution de la fiche,
  si bien que la demande du terminal est servie en **8 ms** ;
- 🏗️ **la compilation du client**, langage bridé en ISO-2, sans aucune API hors de la surface
  publique du Compact Framework 2.0 ;
- 🔬 **les en-têtes du binaire** : runtime CLI 2.5, métadonnées `v2.0.50727`, jeton de clé
  publique `969db8053d3322ac`, **aucune référence au .NET de bureau** ;
- 📱 **l'application elle-même, en fonctionnement** : mêmes sources compilées pour Mono,
  fenêtre 240 × 320, scénario complet contre un vrai serveur. **Les captures sont dans
  `dist/apercu/`** — et dans ce README ;
- 🐳 **la pile de production entière**, depuis un dépôt fraîchement cloné : compilation du
  client en conteneur, les trois services sains, une écriture du serveur relue par le site,
  et **un article inscrit par le serveur MCP qui apparaît sur l'écran du terminal** ;
- 🔫 **la lecture du code-barres, à quatre cadences** : 30 ms et 5 ms par caractère avec
  suffixe `Entrée`, 30 ms sans suffixe, et 150 ms en frappe lente. **Les quatre rendent les
  treize chiffres** — le cas à 5 ms est plus rapide que ce qu'un lecteur réel produit ;
- 🔑 **la reconstitution d'une clé de contrôle absente** : un code amputé et le code complet
  aboutissent au **même produit et à la même clé de stock**, un UPC-A authentique n'est pas
  modifié, et **la réparation est idempotente** ;
- 🔋 **le bandeau d'état** : horloge calée sur le serveur, et jauge de batterie ;
- 🗄️ **la migration de base** : une base créée par la version précédente reçoit ses nouvelles
  colonnes **sans perdre ni stock ni historique** ;
- 🎨 **le décodeur BMP de repli**, forcé par `decodeur_bmp = maison` : il rend une image
  identique à `Bitmap(flux)`. *C'était le chemin le plus risqué, puisqu'il n'est censé servir
  que là où personne ne peut l'essayer* ;
- 🔊 **les douze ondes sonores** : extraites du binaire et relues par la bibliothèque standard
  de Python — en-tête RIFF conforme, 8 bits mono 11 025 Hz. *Leur audition, elle, ne pourra
  être jugée que sur le terminal* ;
- 🌐 **le site public, de bout en bout** : page chargée dans un vrai navigateur, **aucune
  erreur en console**, les six sections rendues, les trente articles dessinés dans le frigo,
  la recherche, les filtres, le tri, la fiche produit et les cinq vues ; rendu contrôlé en
  **1500, 834 et 390 px** de large ;
- 🔒 **la lecture seule du site** : `DELETE`, `UPDATE`, `INSERT` et `DROP` **refusés par
  SQLite**, stock intact après la tentative, et la table de routage de Flask parcourue pour
  vérifier qu'**aucune route ne déclare autre chose qu'un `GET`** ;
- ⚡ **la diffusion temps réel** : une écriture faite par un **autre processus** est détectée
  en une seconde, recalculée **une fois** et poussée à l'abonné ; une file d'abonné saturée
  est abandonnée sans retenir les autres ;
- ⚖️ **la lecture du conditionnement** : `400 g`, `1,15 L`, `6 x 125 g`, `800 gram`, `75 cl`,
  `500 mg` rendent la bonne masse, et **ce qui ne se lit pas rend explicitement rien plutôt
  qu'un poids inventé** ;
- 🗃️ **le cache de vignettes** : deuxième demande servie par la mémoire, une entrée par taille,
  éviction du plus ancien ;
- 🔤 **l'absence de tout caractère hors Latin-1** dans l'interface — les flèches de défilement
  sont *dessinées*, pas écrites, car rien ne garantit que la Tahoma d'une image Windows CE
  industrielle soit complète.

### 🔧 Ce qui reste à confirmer **sur le matériel**

- 💾 la présence du .NET Compact Framework 2.0 dans l'image du terminal
  ([docs/01](docs/01-terminal-skorpio.md) §1.3) ;
- 📶 que le Wi-Fi du terminal monte et joigne le PC ([docs/02](docs/02-reseau.md)) ;
- 🔫 que le lecteur soit bien en émulation clavier avec un suffixe `Enter` (§1.2) — sans lui,
  le bouton **VALIDER** fait le même travail, mais on perd le scan à une main ;
- 📐 que le rendu tombe juste sur l'écran physique : Mono et le Compact Framework ne mesurent
  pas forcément la Tahoma au pixel près ;
- 🔊 que le buzzer réponde à `MessageBeep` de `coredll.dll`, et la batterie à
  `GetSystemPowerStatusEx` ;
- 📊 que la barre des tâches porte bien la classe `HHTaskBar` sur cette image CE.

🩺 En cas d'arrêt inattendu, **`inventaire-erreur.txt`** à côté de l'exécutable porte la trace
complète. Sur un terminal sans console ni débogueur, **c'est la seule façon de savoir ce qui
s'est passé.**

</details>

---

## 🗂️ Organisation du dépôt

```
📁 inventaire/
├── 🚀 demarrer.sh                   prépare et lance la pile de production
├── 🐳 Dockerfile                    image unique des trois services Python
├── 🐳 compose.yaml                  les quatre conteneurs, montages liés, durcissement
├── ⚙️ .env.exemple                   réglages de production, à copier en .env
│
├── 🐳 docker/
│   ├── Dockerfile.client            mono-devel : compile le client, puis s'arrête
│   ├── entree.sh                    un rôle -> une ligne de commande
│   └── sante.py                     sonde de santé : le corps JSON, pas le seul 200
│
├── 🐍 backend/
│   ├── serveur.py                   ⚙️  Autonomous Core        (:8080)
│   ├── vitrine.py                   📊  Cognitive Analytics    (:8081)
│   ├── mcp.py                       🤖  Agentic Layer          (:8082)
│   └── inventaire/
│       ├── api.py                   routage HTTP, validation des paramètres
│       ├── db.py                    SQLite : lots, produits, journal, courses
│       ├── off.py                   Open Food Facts + cache autoritaire
│       ├── images.py                HTTPS -> BMP palettisé, préchauffé en tâche de fond
│       ├── codebarres.py            clés de contrôle EAN/UPC, réparation d'un code amputé
│       ├── rendu.py                 une structure, deux sorties (JSON et clé=valeur)
│       ├── web.py                   tableau de bord et page de téléchargement
│       ├── 🤖 mcp/
│       │   ├── outils.py            ce qu'un modèle peut demander au frigo
│       │   └── application.py       JSON-RPC 2.0 et la table des méthodes
│       └── 📊 vitrine/
│           ├── lecture.py           SQLite en mode=ro, détection de changement
│           ├── mesures.py           KPI, séries, classements, curiosités
│           ├── veille.py            diffusion WebSocket aux navigateurs
│           ├── application.py       les routes Flask
│           └── templates/ static/   une page, un CSS, un JS, sans dépendance
│
├── 📱 frontend/
│   ├── src/                         le client C#
│   │   ├── Fenetre.cs               fenêtre unique, routage clavier, appels réseau
│   │   ├── Ecran*.cs                les sept écrans
│   │   ├── Sons.cs                  🔊 synthèse des effets sonores en mémoire
│   │   ├── CachePhotos.cs           🖼️  photos en RAM, décodées hors interface
│   │   ├── Effet.cs                 socle des économiseurs : sinus entiers, blocs
│   │   ├── Veille.cs                🌈 le moteur : une table de 55, tirée sans remise
│   │   ├── VeilleClassiques.cs      ⭐ pluie, étoiles, logo, Mystify, tuyaux
│   │   ├── VeilleDemos.cs           🌌 20 effets de démoscène
│   │   ├── VeilleJeux.cs            🕹️  10 bornes d'arcade en mode attraction
│   │   ├── VeilleMachine.cs         💻 20 écrans de machine
│   │   ├── EcranMaj.cs              🔄 mise à jour du binaire depuis le serveur
│   │   ├── Theme.cs                 couleurs, polices, primitives de dessin
│   │   ├── Api.cs · Kv.cs           HTTP et lecture du format clé=valeur
│   │   ├── Dates.cs                 📅 saisie de date au pavé numérique
│   │   └── Photo.cs                 décodage BMP, avec repli maison
│   ├── refs/                        assemblies de référence du CF 2.0
│   ├── build/compiler.sh            🏗️  compilation en trois passes
│   └── tools/                       extraction des assemblies, contrôles
│
├── 🧪 tests/                         180 tests
├── 📚 docs/                          mise en service, réseau, API, site, production, MCP
└── 🤖 .github/workflows/ci.yml       tests sur 3.11 à 3.14, puis la pile complète
```

<details>
<summary>🏗️ <b>Pourquoi la compilation se fait en trois passes</b></summary>

<br>

1. 🥇 **Contre les assemblies de *référence* du Compact Framework**, qui ne décrivent que
   l'API publique documentée : *si ça passe, le code n'utilise rien qui manque au terminal.*
   **Ce binaire-là est jeté.** 🗑️
2. 🥈 **Contre les assemblies du *runtime* du terminal**, dont la version de métadonnées
   `v2.0.50727` est celle qu'embarque un binaire de Visual Studio 2008. **C'est le binaire
   livré.** 📦
3. 🥉 **Relecture des en-têtes du résultat.** 🔬

Une chaîne de compilation de 2005 n'a ainsi **jamais à toucher le poste de travail**. 🐳

</details>

---

## 📚 La documentation

| 📖 Document | 🎯 Ce qu'il couvre |
|:---|:---|
| **[01 · Terminal Skorpio](docs/01-terminal-skorpio.md)** | 📱 mise en route, réglages du lecteur code-barres, mémoire persistante |
| **[02 · Réseau](docs/02-reseau.md)** | 📶 Wi-Fi du terminal, adressage, pare-feu |
| **[03 · API](docs/03-api.md)** | 🔌 toutes les routes, tous les paramètres, tous les formats |
| **[04 · Poste de contrôle](docs/04-vitrine.md)** | 📊 les 5 vues, les 10 KPI, le temps réel, la publication |
| **[05 · Production](docs/05-production.md)** | 🐳 conteneurs, podman sans privilèges, SELinux, sauvegarde, diagnostic |
| **[06 · MCP](docs/06-mcp.md)** | 🤖 le protocole, les 7 outils, le branchement d'un LLM local |

---

## 🗺️ Roadmap

- [x] 📡 **Edge AI Capture** — client Windows CE, scan à une main
- [x] ⚙️ **Autonomous Core** — SQLite, Open Food Facts, FEFO, photos BMP
- [x] 📊 **Cognitive Analytics** — 10 KPI, 5 vues, WebSocket
- [x] 🤖 **Agentic Layer (MCP)** — 7 outils, 3 ressources, 3 invites
- [x] 🛒 **AI Shopping Intelligence** — liste partagée terminal ↔ site ↔ LLM
- [x] 🌈 **55 Generative Screensavers**
- [x] 🐳 **Production-ready** — 4 conteneurs, rootless, read-only
- [ ] 📱 **Confirmation sur le matériel réel** — le Skorpio attend sur son socle 🔌
- [ ] 🔔 **Alertes de péremption poussées** vers le LLM
- [ ] 🍽️ **Historique des repas** reconstitué depuis le journal des retraits

---

## 🤝 Contribuer

Les **PR sont bienvenues** 🎉 — ouvrez d'abord une issue pour discuter de ce que vous voulez
changer.

```bash
python3 -m unittest discover -s tests -t .   # 🧪 les 180 tests doivent passer
./demarrer.sh --apercu                       # 📸 si vous touchez à l'interface
./demarrer.sh --veille                       # 🌈 si vous touchez aux économiseurs
```

Deux règles qui n'ont pas l'air d'en être : 🔤 **pas de caractère hors Latin-1** dans
l'interface du terminal, et 🔒 **rien qui écrive** dans `vitrine/` ou `mcp/`. Un test
échouera de toute façon. 🧪

---

## 🫥 Transparence — le décodeur marketing

<details>
<summary><b>👉 Ce README est volontairement un festival de buzzwords. Voici la traduction honnête.</b></summary>

<br>

| 🎩 Ce que dit le README | 🧐 Ce que c'est vraiment |
|:---|:---|
| 🧠 **AI-Native / AI-First / AI Framework** | un serveur Python de 2 000 lignes et un client C# |
| 🪄 **Zero-Prompt Ingestion** | un lecteur de code-barres |
| 🧬 **Agentic Experience Layer** | **un vrai serveur MCP**, ça — 7 outils, JSON-RPC 2.0 ✅ |
| 📡 **Edge AI Capture** | un terminal Datalogic de 2005 qui fait des `GET` |
| 🎯 **Cadence-Aware Input** | un chronomètre entre deux touches |
| 🔑 **Self-Healing Barcodes** | l'arithmétique de la clé de contrôle EAN-13 |
| 📊 **Cognitive Analytics** | dix moyennes pondérées, formule affichée sous chacune |
| 🌈 **Generative Screensavers** | 55 effets écrits à la main, sinus entiers, blocs de 4 px |
| ⚡ **Real-Time Inference** | `PRAGMA data_version` relu chaque seconde |
| 🚀 **Enterprise-Grade** | quatre conteneurs sans root et un fichier SQLite |

**La seule intelligence artificielle de ce dépôt est celle que *vous* branchez dessus** 🔌 —
via MCP, sur le port 8082. Elle est facultative, elle est locale, et elle ne peut rien vider.

Le reste, c'est de l'ingénierie sur un processeur sans FPU. 🧮 **Ce qui est nettement plus
difficile.** 😄

</details>

---

<div align="center">

## 💚 Ils en parlent

> *« Je ne savais pas que j'avais trois pots de moutarde. Maintenant je sais. »*
> — **le frigo**, 2026 🧊

> *« On ne m'a jamais oublié au fond du bac à légumes depuis. »*
> — **un poireau**, anonyme 🥬

> *« 520 MHz, pas de FPU, et il me demande un plasma en plein écran. »*
> — **le PXA270**, sous la torture 🧮

<br>

### ⭐ Si ce projet vous plaît, mettez-lui une étoile ⭐

**Construit avec ❤️, 🧠, beaucoup de ☕ et un terminal industriel de 2005.**

<br>

[![License MIT](https://img.shields.io/badge/📜_Licence-MIT-2ff0c8?style=for-the-badge&labelColor=04070d)](LICENSE)
[![Open Food Facts](https://img.shields.io/badge/🌍_Données-Open_Food_Facts-ff8714?style=for-the-badge&labelColor=04070d)](https://openfoodfacts.org)
[![MCP](https://img.shields.io/badge/🤖_Protocole-MCP-ff7ad9?style=for-the-badge&labelColor=04070d)](https://modelcontextprotocol.io)

<sub>Les assemblies de référence sous <code>frontend/refs/</code> appartiennent à Microsoft et ne sont là que pour compiler contre l'API du Compact Framework.</sub>

</div>
