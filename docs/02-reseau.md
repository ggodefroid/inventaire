# 2. Mettre le terminal sur le réseau

Tout le programme repose sur une seule chose : que le terminal puisse ouvrir
une connexion TCP vers le PC. Ce document sert à l'obtenir, puis à le prouver.

## 2.1 Côté terminal

`Démarrer > Settings > Control Panel > Network and Dial-up Connections`, ou
l'applet Wi-Fi de Datalogic (souvent **Summit Client Utility** ou *Wireless
Configuration*) selon l'image installée.

Ce qu'il faut obtenir :

| Point | Attendu |
|---|---|
| SSID | celui de la maison |
| Sécurité | **WPA2-PSK / AES** au mieux |
| Adresse IP | par DHCP, sur le **même sous-réseau** que le PC |
| Passerelle | celle de la box (même si Internet ne sert pas ici) |

> **Le WPA3 et le Wi-Fi 5 GHz sont hors de portée.** Cette radio est du
> 802.11 b/g : 2,4 GHz uniquement, et une pile WPA2 de 2005. Si la box est en
> WPA3 seul, ou en 2,4 GHz désactivé, le terminal ne verra tout simplement pas
> le réseau. La sortie habituelle est un SSID secondaire en 2,4 GHz / WPA2 —
> la plupart des box en proposent un.

Pour lire l'adresse obtenue, l'applet réseau affiche l'IP ; sinon
`\Windows\ipconfig.exe` existe sur certaines images.

## 2.2 Côté PC

```bash
python3 backend/serveur.py
```

Il écoute sur `0.0.0.0` et annonce les adresses utilisables :

```
  écoute    0.0.0.0:8080
            http://192.168.1.24:8080/   <- à saisir dans les réglages du terminal
```

Si plusieurs adresses apparaissent (VPN, `docker0`, `podman0`), retenez celle
du réseau domestique — en général `192.168.x.x` ou `10.0.x.x`.

**Le pare-feu du PC est la cause n°1 d'échec.** Le serveur tourne, le terminal
ne le joint pas.

```bash
# Fedora / RHEL
sudo firewall-cmd --add-port=8080/tcp          # le temps d'un essai
sudo firewall-cmd --add-port=8080/tcp --permanent && sudo firewall-cmd --reload

# Debian / Ubuntu
sudo ufw allow 8080/tcp
```

## 2.3 Prouver la liaison

**Depuis le PC**, vérifiez d'abord que le serveur répond sur son IP réseau et
pas seulement sur la boucle locale :

```bash
curl "http://192.168.1.24:8080/api/ping?fmt=kv"
```

**Depuis le terminal**, l'écran Réglages (`F3`) a un bouton **Tester** qui fait
exactement cet appel et affiche la version du serveur, la latence et le nombre
d'unités en base. C'est le diagnostic à utiliser : il emprunte le même chemin
que le reste de l'application.

À défaut, Internet Explorer du terminal sur
`http://192.168.1.24:8080/api/ping?fmt=kv` affiche la réponse en clair.

| Ce que dit le terminal | Où chercher |
|---|---|
| « serveur muet (5 s) » | pare-feu du PC, ou terminal sur un autre sous-réseau |
| « connexion refusée » | le serveur n'écoute pas sur ce port |
| « nom … introuvable » | vous avez saisi un nom d'hôte : mettez l'IP |
| « proxy du terminal à désactiver » | réglages de connexion hérités de l'exploitant précédent |

## 2.4 Ce que coûte le réseau

Un bip, c'est deux appels : la fiche, puis la photo.

| Appel | Taille | Remarque |
|---|---|---|
| `/api/scan` | ~700 octets | tout ce qu'affiche l'écran |
| `/api/image` | ~19 ko à 80 px | BMP non compressé, une seule fois par produit |

À 48 px de côté, la photo tombe à 7 ko. La conversion est mise en cache sur le
PC : rebipper le même produit ne déclenche plus aucun trafic vers Open Food
Facts, et la photo ressort du disque.

Si le Wi-Fi est vraiment lent, `photos = 0` dans `inventaire.ini` supprime le second
appel ; l'écran garde tout le reste.

## 2.5 Adresse fixe

Une IP de PC qui change au gré du DHCP oblige à ressaisir les réglages du
terminal. Deux sorties :

- **réservation DHCP** sur la box, à partir de l'adresse MAC du PC — la
  solution propre ;
- **adresse statique** sur le PC.

Un nom d'hôte (`frigo.local`) ne fonctionnera que si le terminal sait le
résoudre, ce qui suppose un DNS local : Windows CE 5.0 ne fait pas de mDNS.
L'IP reste le choix sûr.

## 2.6 Le serveur au démarrage du PC

```bash
mkdir -p ~/.config/systemd/user
cat > ~/.config/systemd/user/frigo.service <<'UNIT'
[Unit]
Description=Inventaire du frigo
After=network-online.target

[Service]
ExecStart=%h/Documents/GitHub/Perso/inventaire/backend/serveur.py --port 8080
Restart=on-failure
RestartSec=5

[Install]
WantedBy=default.target
UNIT

systemctl --user daemon-reload
systemctl --user enable --now frigo.service
loginctl enable-linger "$USER"     # pour qu'il tourne sans session ouverte
```

Adaptez le chemin. `journalctl --user -u frigo -f` suit les requêtes.

<!-- navigation -->

---

<div align="center">

← **[📱 1 · Terminal Skorpio](01-terminal-skorpio.md)** · **[📚 Index](README.md)** · **[🏠 README](../README.md)** · **[🔌 3 · API](03-api.md)** →

</div>
