# Client Windows CE pour le Skorpio

Programme natif .NET Compact Framework 2.0, en C#, pour Windows CE 5.0 sur ARM.

## Ce que fait le programme

- capture les codes-barres via l'émulation clavier du lecteur (aucun SDK
  Datalogic requis) ;
- demande la date de péremption au pavé numérique, avec aperçu en direct ;
- écrit chaque scan sur le disque **avant** toute autre chose ;
- envoie le tampon au PC par série/USB ou TCP, ou l'exporte sur carte mémoire ;
- ne purge rien avant confirmation du serveur.

## Fichiers

| Fichier | Rôle |
|---|---|
| `Program.cs` | point d'entrée ; refuse de démarrer si le tampon est inutilisable |
| `MainForm.cs` | écran unique, machine à états scan → date |
| `Protocol.cs` | protocole de ligne, miroir de `frigo/protocol.py` |
| `Dates.cs` | saisie de dates au pavé, miroir de `frigo/dates.py` |
| `ScanBuffer.cs` | tampon append-only, numéros de séquence, export carte |
| `Uploader.cs` | liaisons série et TCP, séquence de transfert, diagnostic des ports |
| `Settings.cs` / `SettingsForm.cs` | réglages dans `frigo.ini` |
| `TextForm.cs` | afficheur de texte défilant (tampon, diagnostic) |

## Compiler depuis Linux — voie utilisée ici

```bash
sudo pacman -S mono            # une fois
./build-linux.sh
```

Les assemblies de référence sont déjà dans `refs/` ; `tools/extraire_refs.py`
les régénère depuis le redistribuable Microsoft.

Le script fait trois passes, et la distinction entre les deux premières est le
point important :

1. **Contrôle de surface d'API**, contre `refs/` — les assemblies de référence,
   métadonnées seules. Elles ne décrivent que l'API publique documentée du
   CF 2.0, donc compiler contre elles garantit qu'on n'utilise rien d'autre. Le
   binaire produit est jeté.
2. **Binaire livré**, contre `refs/runtime/` — les assemblies réellement
   installées sur le terminal. `mcs` recopie la version de métadonnées depuis le
   corlib référencé : ce jeu porte `v2.0.50727`, exactement ce qu'embarque un
   binaire produit par Visual Studio 2008. Compiler contre `refs/` donnerait
   `2.0.0.0`, une estampille qu'aucun binaire CF authentique ne porte.
3. **Contrôle des en-têtes** du binaire, via `tools/verifier_binaire.py`.

`-langversion:ISO-2` est délibéré : il fait échouer ici, et non sur le terminal,
tout ce que le CF 2.0 ne comprend pas — les propriétés auto-implémentées, par
exemple.

`refs/wince/` contient les `.asmmeta` décrivant la surface d'API du Windows CE
générique. Elles servent à `tools/verifier_api.py`, mais **pas** comme cibles de
compilation : elles amputent les valeurs d'énumération, si bien qu'un
`FontStyle.Bold` y semble absent.

## Compiler avec Visual Studio 2008

Voie alternative, utile si vous préférez le débogueur pas-à-pas sur le
terminal. C'est la dernière version de Visual Studio qui sait cibler le Compact
Framework.

1. Installer **Visual Studio 2008 Professional** avec les outils *Smart Device*.
2. Ouvrir `SkorpioFrigo.csproj`.
3. Si le projet refuse de s'ouvrir — les GUID de plateforme dépendent des SDK
   installés — créer un projet neuf, ce qui est aussi rapide :
   `Fichier > Nouveau > Projet > Smart Device > Windows CE > Device Application`,
   framework **.NET CF 2.0**, puis *Ajouter un élément existant* sur les `.cs`.
4. Références nécessaires, et elles suffisent :
   `mscorlib`, `System`, `System.Drawing`, `System.Windows.Forms`.
5. Compiler en **Release**. Le binaire sort dans `bin\Release\SkorpioFrigo.exe`.

Le `.exe` est de l'IL portable : il n'y a pas de cible ARM à choisir, le
Compact Framework du terminal s'en occupe à l'exécution.

## Installer sur le terminal

Le programme écrit son tampon et ses réglages **dans son propre dossier**.
Installez-le donc en mémoire interne, pas sur la carte mémoire :

```
\Program Files\SkorpioFrigo\SkorpioFrigo.exe
```

`./inventaire.py pousser` prépare le dossier à copier, avec `frigo.ini` déjà
renseigné et un `LISEZMOI.TXT`. Trois voies : carte mémoire (défaut, avec
vérification d'empreinte après copie), `--http` pour que le navigateur du
terminal télécharge le binaire, ou un dossier quelconque en argument.

Voir [../docs/01-terminal-skorpio.md](../docs/01-terminal-skorpio.md) pour le
transfert par carte mémoire, l'activation du wedge et les réglages.

## Fichiers créés à l'exécution

| Fichier | Contenu |
|---|---|
| `frigo.ini` | réglages |
| `tampon.txt` | scans, au format du protocole, en ajout seul |
| `tampon.state` | dernier numéro attribué, dernier numéro confirmé |
| `envoyes.txt` | archive des scans confirmés, après compactage |

`tampon.txt` est du texte lisible : en cas de doute, copiez-le sur le PC et
passez-le à `./inventaire.py importer`. Rien n'est enfermé dans un format opaque.
Si `tampon.state` est perdu, le programme se recale sur le plus grand numéro
présent dans `tampon.txt` au démarrage.
