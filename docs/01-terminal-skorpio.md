# 1. Reprendre la main sur le Skorpio et installer le client

## 1.1 Le logiciel déjà présent

Le terminal démarre peut-être sur l'application de son précédent exploitant.
Sur les terminaux Windows CE d'entreprise, une telle application est en général
lancée de l'une de ces trois façons :

1. **raccourci dans `\Windows\StartUp\`** — le plus courant ;
2. **entrée de registre** `HKLM\init` ou `HKLM\...\Shell` — l'application
   remplace l'explorateur ;
3. **verrouillage par le DDU** (Datalogic Desktop Utility), qui masque le
   bureau et le menu Démarrer.

Pour retrouver le bureau CE, dans cet ordre :

- Fermer l'application (croix, `Alt`+`F4`, ou son propre menu Quitter).
- Ouvrir le **DDU** : `Démarrer > Settings > System > Datalogic Desktop
  Utility`, ou le raccourci clavier **`Alt`+`6`**. Le DDU contient les cases
  qui masquent le bureau, la barre des tâches et le menu Démarrer — les
  décocher rend le terminal utilisable normalement. Si un mot de passe est
  demandé, il a été posé par l'exploitant précédent : dans ce cas seul un
  *clean boot* le lèvera.
- Vérifier l'OS : `Démarrer > Settings > System > About`.

> **Le *clean boot* efface tout ce qui n'est pas en ROM.** C'est bien ce que
> vous voulez pour repartir propre, mais la combinaison de touches diffère
> selon le modèle et je préfère ne pas vous en donner une de mémoire : elle est
> dans le manuel du Skorpio, chapitre *Resetting the Skorpio*
> (<https://developer.datalogic.com>). Sauvegardez la carte mémoire avant.

## 1.2 Point crucial : l'émulation clavier du lecteur

Le client **ne contient aucun SDK Datalogic**. Il s'appuie sur le *wedge*, le
mode d'émulation clavier du lecteur : la gâchette tape le code-barres comme le
ferait un clavier. C'est le mode par défaut sur ces terminaux, et c'est ce qui
rend le programme portable sur toute la gamme sans dépendre d'une DLL
propriétaire.

Deux réglages sont à vérifier dans le panneau de configuration du lecteur
(`Démarrer > Settings > Control Panel`, applet **Datalogic** / *Decoding* /
*Scanner Settings*, selon l'image installée) :

| Réglage | Valeur attendue |
|---|---|
| Wedge / Keyboard emulation | **activé** |
| Suffixe / Terminator | **Enter (CR)** |
| Préfixe | **aucun** |

Le suffixe `Enter` est celui qui compte : c'est lui qui valide le code et
déclenche l'appel au serveur. S'il est absent, le programme reste utilisable —
le bouton **VALIDER** fait la même chose — mais vous perdez le bénéfice du scan
en une main.

C'est aussi ce suffixe qui permet de **rebipper depuis l'écran d'un article**
sans repasser par l'accueil. Le programme distingue une rafale de gâchette
d'une touche isolée à sa cadence de frappe : treize chiffres en quelques
dizaines de millisecondes, c'est le lecteur ; un chiffre qui reste seul, c'est
un doigt sur un raccourci.

Les symbologies à laisser actives : **EAN-13**, **EAN-8**, **UPC-A** — celles
des produits alimentaires. Ajoutez **Code 39** ou **Code 128** si vous comptez
coller vos propres étiquettes sur des bacs.

### La clé de contrôle doit être transmise

> **Le serveur s'en sort même sans ce réglage**, y compris dans les cas où le
> calcul seul ne suffit pas — voir plus bas. Mais la correction repose sur une
> déduction, pas sur la donnée : le réglage reste préférable.

C'est le réglage qui coûte le plus de temps à diagnostiquer, parce qu'il ne
produit aucune erreur. Beaucoup de décodeurs savent **ne pas transmettre le
dernier chiffre** d'un EAN-13 : sa clé de contrôle. Le lecteur décode
correctement, bipe normalement, et n'envoie que douze chiffres. Aucun catalogue
ne reconnaît alors le produit.

Le symptôme, dans un bloc-notes comme dans le programme :

```
code réel de l'article   3017620422003     (13 chiffres, EAN-13)
ce que le lecteur envoie  301762042200     (12 chiffres — la clé manque)
```

Dans l'applet de configuration du lecteur, **`Parameter` → `UpcEan`** : cherchez
les entrées qui contiennent **`Check Digit`**. Il y en a une par symbologie
(`EAN-13`, `UPC-A`, `UPC-E`). Celle d'**EAN-13 doit transmettre la clé** —
selon la version de l'applet, la valeur s'appelle *Enable*, *Send*, *Transmit*
ou *On*. Faites de même pour `UPC-A`.

Enregistrez ensuite par **`File` → `Save`**, sinon le réglage ne survit pas au
redémarrage du terminal.

Comment le serveur s'en sort quand même, dans l'ordre :

1. **Le calcul, quand il suffit.** Douze chiffres qui ne forment pas un UPC-A
   valide sont forcément un EAN-13 privé de sa clé. La clé se recalcule.
2. **Le catalogue, quand le calcul ne suffit pas.** Un EAN-13 amputé a une
   chance sur dix de ressembler à un UPC-A valide — `359671035508`, qui est en
   réalité `3596710355082`, en est un cas réel. Le serveur propose alors les
   deux lectures à Open Food Facts : celle qui existe gagne.
3. **La mémoire.** La correspondance trouvée est retenue : les scans suivants
   du même produit n'ont plus rien à deviner ni à demander au réseau. Et au
   bout de deux corrections constatées, le serveur tient pour acquis que ce
   lecteur retient les clés, et essaie la complétion en premier.

### Vérifier en un bip

Un bloc-notes du terminal suffit à trancher : bippez un article dedans et
comptez les chiffres. Treize pour un EAN-13. S'il en manque un, c'est la clé,
et le réglage ci-dessus est en cause.

## 1.3 Le .NET Compact Framework 2.0

Les images Windows CE 5.0 de Datalogic embarquent le plus souvent le CF 2.0 en
ROM. Pour vérifier, avec l'explorateur de fichiers du terminal : `\Windows\`
doit contenir des fichiers `NETCFv2.*`, ou `mscoree2_0.dll`.

S'il est absent, il faut installer `NETCFv2.wce5.armv4i.CAB` (environ 5 Mo,
dans les archives Microsoft). Déposez le CAB sur le terminal par l'une des
voies du §1.4, tapez dessus dans l'explorateur, et redémarrez.

> Si le terminal n'a **ni** Compact Framework **ni** Wi-Fi configuré, vous êtes
> face à un problème d'œuf et de poule : la seule sortie est la carte mémoire.

## 1.4 Transférer les fichiers sur le terminal

### a) Par le navigateur du terminal — le plus simple

C'est la voie normale une fois le Wi-Fi en place (voir
[docs/02-reseau.md](02-reseau.md)).

```bash
./demarrer.sh                    # produit dist/Inventaire.exe et dist/inventaire.ini
python3 backend/serveur.py       # sert dist/ sur /telecharger
```

Sur le terminal : **Internet Explorer → `http://<ip du PC>:8080/telecharger`**.
Maintenez le doigt sur chaque lien, puis *Enregistrer la cible sous...*

Vérifiez l'adresse annoncée par le serveur au démarrage : sur une machine avec
VPN ou conteneurs, plusieurs IP peuvent apparaître, et une seule est sur le
réseau de la maison.

### b) Par la carte mémoire

Copiez `dist/Inventaire.exe` et `dist/inventaire.ini` sur une carte mini-SD ou Compact
Flash, insérez-la, et recopiez-les depuis l'explorateur du terminal.

C'est aussi la voie pour le CAB du Compact Framework, avant toute liaison
réseau.

### Où poser les fichiers

**Dans un dossier de la mémoire persistante**, par exemple `\FlashDisk\Inventaire\`
ou `\Backup\Inventaire\` selon le modèle.

Le dossier compte pour deux raisons :

- un *cold boot* efface tout ce qui est en RAM, y compris `\Program Files\` sur
  certaines images ;
- le programme lit et réécrit `inventaire.ini` **à côté de son exécutable** — Windows
  CE n'a pas de répertoire courant, et un chemin relatif finirait à la racine.

Pour un lancement automatique au démarrage, créez un raccourci vers le `.exe`
dans `\Windows\StartUp\`.

## 1.5 Premier lancement

L'écran **Réglages** (bouton `F3` depuis l'accueil) :

| Réglage | Valeur |
|---|---|
| Serveur | l'IP du PC, celle qu'annonce `serveur.py` au démarrage |
| Port | `8080` |
| Délai (ms) | `5000` — montez à `8000` si le Wi-Fi est capricieux |
| Photo (px) | `80` — le côté du cadre ; `48` allège si le réseau rame |
| Photos | `oui` |
| Bip | `oui` |

### Réglages du fichier

| Sons | `tout` |

Le champ **Sons** a trois crans : `non`, `essentiel` (les bips qui portent une
information : lecture d'un code, ajout, retrait, erreur, alerte de péremption)
et `tout` (les précédents plus les clics de navigation, le déclencheur photo et
les balayages). Les flèches `←` `→` le font tourner, et il s'applique
immédiatement — on entend le réglage qu'on règle.

`inventaire.ini` porte trois réglages de plus, sans équivalent à l'écran :
`quantite_defaut` ; `volume_max` (pousse le volume de sortie du terminal au
maximum au démarrage — mettez `0` pour ne pas y toucher) ; et `decodeur_bmp`
(`auto`, ou `maison` pour court-circuiter `Bitmap(flux)` si l'image OS n'a pas
de codec).

L'heure affichée dans le bandeau est **celle du serveur**, pas celle du
terminal : l'horloge d'un Windows CE repart à zéro au *cold boot*, et une
péremption calculée dessus serait fausse. La charge de la batterie, elle, vient
bien du terminal.

L'adresse se tape au pavé numérique ; la touche `.` de l'écran sert aux
claviers qui n'ont pas de point accessible.

Appuyez sur **Tester** : le serveur doit répondre sa version, sa latence et le
nombre d'unités en base. Puis **Enregistrer** — le fichier `inventaire.ini` est
réécrit à côté de l'exécutable.

> « écriture refusée » signifie que le programme tourne depuis un support en
> lecture seule. Recopiez-le sur la mémoire interne.

## 1.6 Usage quotidien

```
1. gâchette         → la fiche s'affiche : nom, photo, Nutri-Score, NOVA,
                      Eco-Score, témoins nutritionnels, labels, allergènes,
                      stock et lots
2. 1 (ou AJOUTER)   → écran de péremption
3. 2510  puis ⏎     → ajouté, retour à la fiche
```

Le terminal se tient d'une main, la gâchette sous l'index : **tout se fait au
clavier**, l'écran tactile n'est qu'un second chemin. Un anneau noir marque
en permanence l'élément sous le curseur.

| Touche | Partout |
|---|---|
| `F1` | tout le frigo |
| `F2` | retour à l'accueil |
| `F3` | réglages |
| `Échap` | revenir en arrière · depuis l'accueil : mise en veille |
| gâchette | bippe un article, depuis n'importe quel écran |

| Touche | Fiche d'un article |
|---|---|
| `↑` `↓` | choisir un lot |
| `←` `→` | déplacer le curseur entre les actions |
| `Entrée` | exécuter l'action sous le curseur |
| `1` `2` | ajouter · retirer une unité |
| `3` `4` | saisir un libellé · fiche Open Food Facts |
| `5` | photo en grand |
| `0` | vider le lot sélectionné |

| Touche | Saisie de la péremption |
|---|---|
| `←` `→` | choisir le champ : jour, mois, année, quantité, puis les échéances |
| `↑` `↓` | **+1 / −1** sur le champ choisi |
| `Retour arr.` | sur le jour : l'efface et garde le mois |
| chiffres | la date d'un coup : `JJMM`, `JJMMAA` ou `JJMMAAAA` |
| `Entrée` | ajouter, ou appliquer l'échéance visée · `Échap` annule |

Corriger « le 12 au lieu du 15 » ne demande donc plus de retaper la date : on se
place sur le jour et on appuie trois fois sur `↓`.

Les flèches suivent toujours la disposition : on parcourt dans l'axe où les
choses sont rangées — jour, mois et année sont côte à côte, donc `←` `→` — et
on règle dans l'autre.

**Beaucoup d'emballages n'indiquent qu'un mois** — « à consommer de préférence
avant fin 11/2026 ». Régler le mois ou l'année sans avoir touché au jour laisse
celui-ci vide : le champ affiche `--` et l'article est enregistré à la précision
du mois, affiché ensuite « fin 11/26 ». Le jour ne s'invente pas.

| Touche | Tout le frigo |
|---|---|
| `↑` `↓` | article suivant, précédent |
| `←` `→` | viser « ouvrir la fiche » ou « retirer 1 » |
| `Entrée` | exécuter ce que vise le curseur |
| `5` | photo de l'article en grand |

| Touche | Fiche Open Food Facts · Réglages |
|---|---|
| `↑` `↓` | défiler · passer au champ, puis aux boutons |
| `←` `→` | page précédente, suivante · basculer un oui/non |
| `Entrée` | champ suivant, ou exécuter le bouton visé |

**Ranger le frigo après un repas** se fait depuis `F1` : `→` pour viser la
colonne « retirer », puis `↓` et `Entrée` devant chaque article sorti. La liste
étant triée par échéance, ce qui presse est en haut — un écran « périme
bientôt » séparé n'aurait rien montré de plus.

- Produit déjà rangé une fois : la durée habituelle est déjà proposée, `⏎`
  suffit.
- **Les photos apparaissent dans la liste** au fur et à mesure : elles se
  chargent dans les creux, jamais devant une action. Une photo déjà vue ne se
  redemande jamais, donc le second passage est immédiat.
- **Le programme occupe tout l'écran** et masque la barre des tâches. Elle
  revient quand on quitte par **Réglages → Quitter l'application**. Si le
  programme s'arrête brutalement, la barre peut rester cachée : un *soft reset*
  du terminal la ramène.
- Pas de date imprimée : bouton **sans date**, ou `⏎` sur un champ vide.
- Sortir un article : `2`, ou `Entrée` avec le curseur sur **RETIRER**. Part
  toujours le lot qui périme le plus tôt ; pour en viser un autre, sélectionnez-le
  avec `↑` `↓`, puis `0` le vide d'un coup.
- Produit absent d'Open Food Facts : `3` ouvre la saisie d'un libellé. Sans
  mode alphabétique sur le pavé, faites-le depuis le tableau de bord du
  navigateur — bouton `…` sur la ligne du produit.
- `F1` liste tout le frigo, `F2` revient à l'accueil, `Échap` recule d'un cran.

## 1.7 Quand ça ne marche pas

| Symptôme | Cause la plus probable |
|---|---|
| « serveur muet (5 s) » | mauvaise IP, ou `serveur.py` arrêté |
| « connexion refusée » | bon hôte, mauvais port, ou pare-feu du PC |
| « nom … introuvable » | un nom d'hôte a été saisi sans DNS ; mettez l'IP |
| « proxy du terminal à désactiver » | réglages de connexion hérités : `Démarrer > Settings > Network and Dial-up Connections` |
| Le code s'affiche mais rien ne part | rare depuis que la rafale se conclut seule ; vérifiez tout de même le suffixe `Enter` du wedge (§1.2) |
| Il manque un chiffre au code | la clé de contrôle n'est pas transmise : `Parameter` → `UpcEan` → `Check Digit` (§1.2). Le serveur corrige en attendant |
| Bandeau orange « clé EAN-13 ajoutée » | même cause : le réglage ci-dessus n'est pas fait. Le produit est trouvé quand même |
| Un code n'aboutit jamais | vérifiez sa longueur dans un bloc-notes du terminal : 13 chiffres pour un EAN-13. S'il en manque un, c'est la clé de contrôle (§1.2) |
| Photo absente, `?` dans le cadre | `pillow` non installé côté PC, ou produit sans photo |
| Photo absente, `...` qui reste | le terminal n'a pas su décoder le BMP ; essayez `decodeur_bmp = maison` dans `inventaire.ini` |
| L'application disparaît au lancement | Compact Framework 2.0 absent (§1.3) |
| L'application s'arrête en cours de route | lisez `inventaire-erreur.txt`, écrit à côté de l'exécutable : il porte la trace complète |
