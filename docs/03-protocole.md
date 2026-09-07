# 3. Le protocole

Des lignes ASCII terminées par `CRLF`, champs séparés par `|`, avec une somme de
contrôle XOR facultative en fin de ligne.

```
SCAN|12|3017620422003|2026-10-25|1|frigo|2026-09-07T18:04:11||*4F
^    ^  ^             ^          ^ ^     ^                   ^ ^
|    |  code-barres   péremption │ lieu  horodatage terminal  │ checksum
|    n° de séquence              quantité                     note
verbe
```

Ce format tient sur trois exigences : être lisible à l'œil dans un fichier de
tampon, être trivial à produire en C# sans bibliothèque JSON sur un terminal de
2008, et survivre à une liaison série bruitée.

## Terminal → serveur

| Ligne | Rôle |
|---|---|
| `HELLO\|<terminal>\|<protocole>\|<version client>` | ouverture de session |
| `SCAN\|<seq>\|<code>\|<péremption>\|<qté>\|<lieu>\|<horodatage>\|<note>` | un scan |
| `LOOK\|<code>` | demande du libellé produit |
| `BYE\|<nb envoyés>` | fin de session |
| `PING` | test de liaison |

## Serveur → terminal

| Ligne | Rôle |
|---|---|
| `READY\|<protocole>\|<version serveur>\|<terminal>\|<dernier seq connu>` | session acceptée |
| `OK\|<seq>\|<libellé>` | scan enregistré |
| `DUP\|<seq>` | déjà connu, ignoré |
| `ERR\|<seq>\|<motif>` | scan refusé |
| `INFO\|<code>\|<libellé>\|<marque>` | réponse à `LOOK` |
| `BYE\|<ok>\|<dup>\|<err>` | bilan de session |
| `PONG` | réponse à `PING` |

## Somme de contrôle

XOR de tous les octets UTF-8 de la charge utile (tout ce qui précède le dernier
séparateur), sur deux chiffres hexadécimaux majuscules, préfixé de `*`.

Une ligne **sans** checksum est acceptée — c'est ce qui permet de retoucher un
fichier de tampon à la main sur le PC. Une ligne **avec** une checksum fausse est
rejetée : sur une liaison série, mieux vaut perdre un scan que l'enregistrer
corrompu. Le terminal conserve la ligne dans son tampon et la renverra.

## Idempotence

Le couple `(terminal, séquence)` porte un index unique en base. Le numéro de
séquence est attribué une fois pour toutes par le terminal, au moment du scan, et
ne change plus jamais.

Conséquence pratique : renvoyer tout le tampon est **toujours** sûr. C'est ce qui
permet de gérer sans état compliqué le cas normal d'un socle — câble arraché en
plein transfert, terminal retiré, PC en veille. `READY` renvoie le dernier numéro
connu du serveur, ce qui évite de réémettre inutilement, mais ce n'est qu'une
optimisation : la correction ne repose que sur l'index unique.

Une saisie manuelle sur le PC (formulaire web, `inventaire.py ajouter`) porte
`seq = NULL` et échappe donc à la déduplication — deux yaourts identiques
ajoutés à la main sont bien deux articles.

## Champs

| Champ | Format | Notes |
|---|---|---|
| terminal | 32 car. alphanumériques | identifie le terminal dans la base |
| séquence | entier croissant | définitif, attribué par le terminal |
| code-barres | EAN-13, EAN-8, UPC-A, ou libre | l'UPC-A est converti en EAN-13, la clé de contrôle est vérifiée |
| péremption | `YYYY-MM-DD`, ou saisie brute du pavé, ou vide | vide = date inconnue |
| quantité | entier 1–999 | |
| lieu | 24 car. | `frigo`, `congelo`, `placard`… |
| horodatage | ISO local | horloge du terminal, conservée telle quelle |
| note | texte libre | |

Les caractères `|`, `*` et les caractères de contrôle sont remplacés par des
espaces à l'encodage, des deux côtés.

## Modifier le protocole

Le protocole est implémenté deux fois : `frigo/protocol.py` et
`client/SkorpioFrigo/Protocol.cs`. Toute modification doit toucher **les deux**,
plus le miroir `tests/csharp_mirror.py`, sinon `tests/test_interop.py` ne
détectera plus les divergences. Incrémentez `PROTO_VERSION` en cas de changement
incompatible : `HELLO` porte la version, et le serveur refuse une version
inconnue avec un `ERR` explicite plutôt que de mal interpréter les données.
