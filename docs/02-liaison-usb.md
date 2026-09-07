# 2. La liaison entre le Skorpio et le PC

## 2.1 Ce que Linux voit

Branché sur son socle, le terminal se présente ainsi :

```
$ lsusb
Bus 003 Device 009: ID 080c:0200 Datalogic S.p.A. Datalogic USB Sync

  bInterfaceClass       255 Vendor Specific Class
  bNumEndpoints           2
    EP 1 IN   Bulk
    EP 2 OUT  Bulk
```

Deux endpoints bulk, classe propriétaire, **aucun pilote Linux attaché par
défaut** : c'est le profil « série sur USB » classique de Windows CE. Le module
noyau `ipaq`, écrit à l'origine pour les Compaq iPAQ, sait exposer ce genre de
liaison en port série ordinaire.

Le diagnostic intégré vous dit exactement où vous en êtes :

```bash
./inventaire.py ports
```

## 2.2 Faire apparaître `/dev/ttyUSB0`

Le module `ipaq` connaît 453 identifiants de terminaux Windows CE, mais **pas**
celui du Skorpio. Il faut le lui déclarer :

```bash
sudo modprobe ipaq
echo 080c 0200 | sudo tee /sys/bus/usb-serial/drivers/ipaq/new_id
./inventaire.py ports          # doit maintenant montrer /dev/ttyUSB0
```

> Beaucoup de documentations, y compris une version antérieure de celle-ci,
> indiquent `modprobe ipaq vendor=0x080c product=0x0200`. **Ces paramètres de
> module n'existent plus** — `modinfo -p ipaq` ne liste que `initial_wait` et
> `connect_retries`. L'ajout dynamique d'identifiant passe désormais par
> l'attribut sysfs `new_id`.

Pour rendre cela permanent, plus un lien stable `/dev/skorpio` et l'accès sans
root :

```bash
sudo cp tools/skorpio-udev.rules /etc/udev/rules.d/70-skorpio.rules
echo ipaq | sudo tee /etc/modules-load.d/skorpio.conf
sudo udevadm control --reload-rules
```

Sur Arch / CachyOS, les ports série appartiennent au groupe `uucp`. Vous en êtes
déjà membre, donc rien à faire ; sinon :

```bash
sudo usermod -aG uucp "$USER"      # puis se reconnecter
```

Ensuite :

```bash
./inventaire.py serve              # le port est trouvé tout seul
./inventaire.py serve --port-serie /dev/skorpio    # ou explicitement
```

Le serveur traite l'absence de port comme un état normal : il attend, et se
rattache de lui-même dès que vous posez le terminal. Vous pouvez le laisser
tourner en permanence.

## 2.3 Ce que parle réellement le port : mesuré, pas supposé

Une fois `/dev/ttyUSB0` apparu, la sonde donne la réponse :

```bash
python3 tools/sonder_liaison.py
```

Sur ce matériel, le résultat est net. Le terminal répond six octets :

```
0000  43 4C 49 45 4E 54                                |CLIENT|
```

Ce n'est donc **pas** une liaison série brute : c'est ActiveSync qui pilote le
port, et il attend la réponse `CLIENTSERVER` — la convention de connexion
directe de Windows (*Direct Cable Connection*). Répondre `SERVER` seul ne
produit rien, le terminal l'ignore en silence.

Deux détails qui font perdre du temps si on les ignore :

- le terminal n'émet `CLIENT` **qu'une fois par tentative de connexion**. Après
  un échec, il faut le sortir du socle et le reposer (ou débrancher/rebrancher
  l'USB) pour relancer sa machine à états ;
- le tube USB n'a pas de lignes de modem. `TIOCMGET` y échoue, et c'est normal —
  d'où l'option `local` passée à `pppd`.

### Piège : le numéro du port change à chaque dockage

Linux attribue le premier *minor* libre. Sortir le terminal du socle détruit
`/dev/ttyUSB0` ; le reposer crée `/dev/ttyUSB1`, puis `ttyUSB2`, et ainsi de
suite. Un nom de port codé en dur ne fonctionne donc **qu'une seule fois**.

Deux conséquences pratiques :

- installez les règles udev, qui donnent un nom stable `/dev/skorpio` :

  ```bash
  sudo cp tools/skorpio-udev.rules /etc/udev/rules.d/70-skorpio.rules
  echo ipaq | sudo tee /etc/modules-load.d/skorpio.conf
  sudo udevadm control --reload-rules
  ```

- les outils du projet résolvent le port par VID/PID à chaque fois
  (`python3 tools/trouver_port.py` l'affiche), donc ne leur passez un nom
  explicite que si vous avez une bonne raison.

### Conséquence : on peut obtenir une liaison IP sur le câble

Puisque le terminal enchaîne sur PPP après le handshake, répondre correctement
donne une vraie liaison IP par l'USB. Cela débloque les deux besoins d'un coup :
les scans passent par notre protocole en TCP, et le navigateur du terminal peut
télécharger le client en HTTP.

```bash
sudo ./tools/liaison_ppp.sh          # à lancer AVANT de toucher au terminal
# puis : sortir le terminal du socle et le reposer
```

**L'ordre compte, et pas pour une raison de confort.** Le terminal n'émet
`CLIENT` qu'à chaque nouvelle tentative de connexion, il faut donc le redocker ;
mais le sortir du socle détruit le port que `pppd` tenait ouvert. Lancer `pppd`
puis redocker ne peut pas fonctionner : il attendrait sur un port mort. Le
script fait donc l'inverse — il guette l'apparition d'un terminal, résout son
port, et lance `pppd` dans la seconde. Vous pouvez le démarrer terminal posé ou
non, et redocker autant de fois qu'il faut : chaque tentative repart proprement.

Il répond au handshake (`tools/handshake_ce.py`), lance au besoin le faux
service ActiveSync, puis passe la main à `pppd` — PC en `192.168.131.1`,
terminal en `192.168.131.2`.

Si PPP monte puis retombe au bout de quelques secondes, c'est qu'ActiveSync
cherche son service sur le port TCP 5679 du PC et n'y trouve personne. Lancez
en parallèle :

```bash
python3 tools/dccm_factice.py
```

Il accepte la connexion et la maintient, sans chercher à parler ActiveSync : on
ne veut que la liaison IP. `liaison_ppp.sh` le démarre automatiquement si le
port 5679 est libre.

### Lire le journal de négociation

`journalctl -t pppd -f` montre la négociation en direct. Deux étapes se
succèdent, et savoir les distinguer fait gagner du temps :

- **LCP** établit la liaison. Si vous voyez des `sent [LCP ConfReq]` suivis de
  `rcvd [LCP ConfAck]`, le handshake a réussi et la pile PPP du terminal
  répond — le plus dur est fait.
- **IPCP** attribue les adresses. Le terminal envoie
  `IPCP ConfReq <addr 0.0.0.0>`, ce qui signifie « donne-moi une adresse ».

Si IPCP boucle indéfiniment avec des `sent [IPCP ConfRej]` de notre côté alors
que `ppp0` existe déjà, c'est que `pppd` a été lancé **sans le couple
d'adresses** `192.168.131.1:192.168.131.2`. Il n'a alors rien à proposer, refuse
la demande, et le terminal la répète sans fin. L'argument n'est pas optionnel.

> **État de cette voie.** Sur ce terminal, le handshake fonctionne et la pile
> PPP de Windows CE 5.0 négocie : LCP passe, `ppp0` apparaît. En cas de blocage,
> la carte mémoire et le Wi-Fi restent les voies sûres.

### Autre possibilité : basculer le terminal en série brute

Côté terminal, on peut aussi faire lâcher le port par ActiveSync :

- `Démarrer > Settings > Control Panel > PC Connection` — décocher
  « Enable direct connections to the desktop », ou choisir une connexion série ;
- l'applet **Datalogic Configuration Utility** / *Communications*, réglage
  `USB Client` : `ActiveSync` → `Serial` (parfois nommé *USB-to-Serial*).

Puis relevez le nom du port avec **`Outils > Diagnostic ports`** dans le client :
le port USB apparaît souvent en `COM4:` ou `COM6:` plutôt qu'en `COM1:`.

## 2.4 Les trois solutions de repli

Elles fonctionnent toutes les trois avec le même serveur, sans rien recompiler.
Le protocole étant identique sur les trois transports, l'inventaire obtenu est
exactement le même.

### a) La carte mémoire — celle qui marche toujours

Aucun pilote, aucun câble, rien à configurer.

```
Sur le terminal :  Actions > Exporter sur carte
Sur le PC       :  ./inventaire.py importer /run/media/$USER/*/frigo-SKORPIO1-*.txt
```

L'import est idempotent : réimporter le même fichier ne crée pas de doublon.
Ajoutez `--archiver` pour que le fichier soit renommé en `.importe` après succès.

### b) Le socle RS-232

Le Skorpio expose un vrai port série sur `COM1:`. Avec un câble RS-232 du socle
et un adaptateur USB-série sur le PC (`/dev/ttyUSB0`), la liaison est directe et
sans ambiguïté — c'est le montage le plus prévisible des trois.

```bash
./inventaire.py serve --port-serie /dev/ttyUSB0 --bauds 115200
```

Si des caractères se perdent, descendez à `9600` des deux côtés (réglage
**Bauds** dans le client).

### c) Le Wi-Fi, si la radio est présente

La référence `701-902` correspond à un modèle **équipé du 802.11b/g**. Ça vaut
la vérification : `Démarrer > Settings > Network and Dial-up Connections`. Si le
terminal rejoint votre réseau, c'est de loin le plus confortable — les scans
arrivent en temps réel et le libellé du produit s'affiche à l'écran dès le scan.

```bash
./inventaire.py serve --hote-tcp 0.0.0.0 --port-tcp 9101
ip addr | grep 'inet '                       # l'IP à mettre dans le client
```

Côté client : `Réglages > Transport = tcp`, `Hôte PC = <IP du PC>`,
`Port TCP = 9101`. Pensez au pare-feu du PC.

## 2.5 Dépannage

| Symptôme | Cause probable | Action |
|---|---|---|
| `ports` ne voit aucun Datalogic | terminal éteint, en veille, ou mal posé | rallumer, vérifier les contacts du socle |
| Datalogic vu, pilote `<aucun>` | identifiant non déclaré au driver `ipaq` | `echo 080c 0200 \| sudo tee /sys/bus/usb-serial/drivers/ipaq/new_id` |
| `/dev/ttyUSB0` existe, `Permission denied` | droits du port | vérifier le groupe `uucp`, installer les règles udev |
| Le client dit « pas de réponse du PC » | serveur non lancé, ou mauvais port COM | lancer `serve`, puis `Outils > Diagnostic ports` |
| Le port s'ouvre, seul `CLIENT` arrive | ActiveSync pilote le tube USB | §2.3 : monter PPP, ou basculer le terminal en série brute |
| Plus aucun `CLIENT` n'arrive | le terminal ne retente plus | le sortir du socle et le reposer, **script déjà lancé** |
| `pppd` dit `Connect script failed` | il attendait sur un port mort, ou le terminal n'a pas été redocké | relancer `liaison_ppp.sh`, puis redocker |
| Le port est passé en `ttyUSB1`, `ttyUSB2`… | normal, un minor par dockage | installer les règles udev pour `/dev/skorpio` |
| Caractères perdus, checksums fausses | débit trop élevé sur RS-232 | passer à 9600 bauds des deux côtés |
| Le serveur log `checksum X attendue Y` | ligne abîmée en transit | normal et sans gravité : le scan est rejeté, le tampon le conserve, relancez l'envoi |

Dans tous les cas de figure : **le tampon du terminal n'est jamais purgé avant
confirmation du serveur.** Un transfert raté ne coûte qu'un nouvel envoi.
