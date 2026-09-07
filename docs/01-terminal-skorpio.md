# 1. Reprendre la main sur le Skorpio et installer le client

## 1.1 Le logiciel « Dataverde » déjà présent

Le terminal démarre aujourd'hui sur une application tierce, vraisemblablement
celle de son précédent exploitant (activité de récupération/collecte). Sur les
terminaux Windows CE d'entreprise, une telle application est en général lancée
de l'une de ces trois façons :

1. **raccourci dans `\Windows\StartUp\`** — le plus courant ;
2. **entrée de registre** `HKLM\init` ou `HKLM\...\Shell` — l'application
   remplace l'explorateur ;
3. **verrouillage par le DDU** (Datalogic Desktop Utility), qui masque le bureau
   et le menu Démarrer.

Pour retrouver le bureau CE, dans cet ordre :

- Fermer l'application (croix, `Alt`+`F4`, ou son propre menu Quitter).
- Ouvrir le **DDU** : `Démarrer > Settings > System > Datalogic Desktop Utility`,
  ou le raccourci clavier **`Alt`+`6`**. Le DDU contient les cases qui masquent
  le bureau, la barre des tâches et le menu Démarrer — les décocher rend le
  terminal utilisable normalement. Si un mot de passe est demandé, il a été posé
  par l'exploitant précédent : dans ce cas seul un *clean boot* le lèvera.
- Vérifier l'OS : `Démarrer > Settings > System > About`.

> **Le *clean boot* efface tout ce qui n'est pas en ROM.** C'est bien ce que vous
> voulez pour repartir propre, mais la combinaison de touches diffère selon le
> modèle et je préfère ne pas vous en donner une de mémoire : elle est dans le
> manuel du Skorpio, chapitre *Resetting the Skorpio*
> (<https://support.ocr.ca/Data/datalogic/skorpio.pdf>, ou le portail
> <https://developer.datalogic.com>). Sauvegardez la carte mémoire avant.

## 1.2 Point crucial : l'émulation clavier du lecteur

Le client **ne contient aucun SDK Datalogic**. Il s'appuie sur le *wedge*, le
mode d'émulation clavier du lecteur : la gâchette tape le code-barres dans le
champ de saisie qui a le focus, exactement comme un clavier. C'est le mode par
défaut sur ces terminaux, et c'est ce qui rend le programme portable sur toute
la gamme sans dépendre d'une DLL propriétaire.

Deux réglages doivent être vérifiés, dans le panneau de configuration du
lecteur (`Démarrer > Settings > Control Panel`, applet **Datalogic** /
*Decoding* / *Scanner Settings*, selon l'image installée) :

| Réglage | Valeur attendue |
|---|---|
| Wedge / Keyboard emulation | **activé** |
| Suffixe / Terminator | **Enter (CR)** |
| Préfixe | **aucun** |

Le suffixe `Enter` est celui qui compte : c'est lui qui déclenche le passage
automatique à la saisie de la date. S'il est absent, le programme reste
utilisable — le bouton **Valider** fait la même chose — mais vous perdez le
bénéfice du scan en une main.

Les symbologies à laisser actives : **EAN-13**, **EAN-8**, **UPC-A**. Ce sont
celles des produits alimentaires. Ajoutez **Code 39** ou **Code 128** si vous
comptez coller vos propres étiquettes sur des bacs.

## 1.3 Le .NET Compact Framework 2.0

Les images Windows CE 5.0 de Datalogic embarquent le plus souvent le CF 2.0 en
ROM. Pour vérifier, avec l'explorateur de fichiers du terminal :
`\Windows\` doit contenir des fichiers `NETCFv2.*` (ou `mscoree2_0.dll`).

S'il est absent, il faut installer `NETCFv2.wce5.armv4i.CAB` (environ 5 Mo,
disponible dans les archives Microsoft). Voir §1.4 pour le transférer.

## 1.4 Transférer les fichiers sur le terminal

Sous Linux il n'y a pas d'ActiveSync, et la liaison USB du socle sert à remonter
les scans, **pas à déposer des fichiers**. Trois voies, selon ce dont vous
disposez.

### a) La carte mémoire — la plus simple

Elle résout aussi le problème de l'œuf et de la poule : installer le Compact
Framework alors qu'on n'a encore aucune liaison.

```bash
./inventaire.py pousser
```

La commande détecte la carte montée, y écrit le dossier `SkorpioFrigo/` avec le
binaire, un `frigo.ini` déjà renseigné et un `LISEZMOI.TXT`, puis **relit le
binaire pour vérifier son empreinte** — une carte SD fatiguée accepte parfois
l'écriture et relit autre chose.

Joignez le CAB du Compact Framework s'il manque sur le terminal :

```bash
./inventaire.py pousser --avec /chemin/NETCFv2.wce5.armv4i.CAB
```

### b) Le navigateur du terminal — sans carte

Si le Wi-Fi du terminal fonctionne, le PC sert le binaire et Internet Explorer
le télécharge :

```bash
./inventaire.py pousser --http
# puis sur le terminal : Internet Explorer -> http://<IP du PC>:8078/
```

Vérifiez l'adresse annoncée : sur une machine avec VPN ou conteneurs, l'IP
détectée automatiquement peut ne pas être celle du réseau de la maison. Dans ce
cas, forcez-la avec `--hote`.

Dans Internet Explorer de Windows CE, maintenez le doigt sur le lien puis
choisissez *Enregistrer la cible sous...*

### c) Un dossier, puis copie manuelle

```bash
./inventaire.py pousser ~/carte-skorpio
```

Utile pour préparer la charge utile et la transférer par vos propres moyens.

### Puis, sur le terminal

1. Ouvrir l'explorateur de fichiers
   (`Démarrer > Programs > File Explorer`).
2. Si nécessaire, taper sur le `.CAB` pour installer le Compact Framework, puis
   redémarrer.
3. Copier `SkorpioFrigo.exe` dans **`\Program Files\SkorpioFrigo\`**.

   Le dossier compte : le programme écrit son tampon et ses réglages **à côté de
   son exécutable**. Windows CE n'a pas de répertoire courant, et lancer le `.exe`
   depuis la carte mémoire ferait vivre le tampon sur une carte amovible — le
   jour où vous la sortez, les scans partent avec.

4. Pour un lancement automatique au démarrage, créer un raccourci vers le `.exe`
   dans `\Windows\StartUp\`.

## 1.5 Premier lancement

Au premier démarrage, `Outils > Réglages` :

| Réglage | Valeur |
|---|---|
| Terminal | `SKORPIO1` (identifiant libre ; il distingue les terminaux dans la base) |
| Transport | `serie` pour le socle, `tcp` si le Wi-Fi fonctionne |
| Port COM | `COM1:` pour le socle RS-232 — pour l'USB, voir ci-dessous |
| Bauds | `115200` (descendez à `9600` si la liaison est instable) |
| Hôte PC / Port TCP | l'IP du PC et `9101`, en transport `tcp` |
| Lieu | `frigo` (ou `congelo`, `placard`…) |
| Demander la péremption | coché |

**Le nom du port USB est l'inconnue du montage.** Il dépend du réglage
« PC Connection » de l'image installée. Le menu **`Outils > Diagnostic ports`**
teste `COM1:` à `COM9:` et affiche lesquels s'ouvrent : les candidats sont ceux
marqués `OUVRABLE`. Testez-les un par un avec le serveur lancé sur le PC.

## 1.6 Usage quotidien

```
1. gâchette  → le code-barres apparaît, l'écran passe à « PEREMPTION JJMMAA »
2. 251026    → aperçu en direct : « 25/10/26 (J-48) »
3. Entrée    → « #12 enregistré », retour au scan
```

- Date inconnue : `Entrée` sur un champ vide.
- Le bouton **`-> J+n`** bascule en mode relatif : `7` signifie « dans 7 jours ».
  Pratique pour les restes et les produits frais sans date imprimée.
- `Échap` annule le produit en cours.
- Le compteur en haut à droite indique ce qui reste à envoyer au PC.
- En fin de session : poser le terminal sur le socle, puis
  **`Actions > Envoyer au PC`**.

Le tampon n'est jamais purgé avant confirmation du serveur. En cas d'échec de
transfert, l'écran affiche « Aucun scan perdu » — et c'est littéralement vrai :
il suffit de relancer l'envoi.
