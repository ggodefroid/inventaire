# 6. Le frigo comme outil pour un LLM

Demander à un modèle « qu'est-ce que je peux cuisiner ce soir ? » suppose qu'il
sache ce qu'il y a dans le frigo. On peut lui recopier l'inventaire dans
l'invite ; il sera périmé au repas suivant. Le **Model Context Protocol** règle
le problème dans l'autre sens : le modèle va chercher lui-même, quand il en a
besoin.

```bash
.venv/bin/python backend/mcp.py         # écoute 0.0.0.0:8082
```

Troisième processus, troisième port. Il lit la même base que les deux autres,
en lecture seule.

## 6.1 Brancher un client

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

C'est la forme que comprennent la plupart des clients MCP. Le transport est le
**Streamable HTTP** de la spécification : du JSON-RPC 2.0 en POST sur un seul
point d'entrée. Pas de session à tenir, pas de flux SSE — une requête, une
réponse. C'est le sous-ensemble le plus simple que la spécification autorise, et
il suffit : tous les outils d'ici répondent d'un coup.

Pour vérifier sans client :

```bash
curl -s -X POST -H 'Content-Type: application/json' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call",
       "params":{"name":"resume","arguments":{}}}' \
  http://192.168.1.24:8082/mcp | python3 -m json.tool
```

```
Nous sommes le 2026-09-13, il est 12:35.
Frigo : 6 unites, 3 references.
Perime : 0 unite(s).
A consommer sous 3 jours : 0 reference(s)
Liste de courses : 2 ligne(s) a prendre.
```

`GET /sante` donne l'état du processus, pour une sonde d'hébergeur.

## 6.2 Les sept outils

| Outil | Ce qu'il rend |
|---|---|
| `resume` | cinq lignes : date, unités, ce qui presse, taille de la liste |
| `inventaire` | tout le stock, trié par échéance ou par nom |
| `bientot_perime` | ce qui périme sous *n* jours, le déjà-périmé à part |
| `chercher` | noms, marques, catégories, labels, codes-barres |
| `details_produit` | nutrition, ingrédients, allergènes, lots d'une référence |
| `liste_courses` | ce qui reste à acheter, et ce qui est au panier |
| `ajouter_aux_courses` | inscrit un article — **le seul qui modifie quelque chose** |

**Du texte, pas du JSON.** Un modèle raisonne mieux sur
`2x Nutella (Ferrero) 400 g - perime dans 5 j [Nutri-Score E]` que sur une
structure imbriquée, et c'est moins cher en jetons : l'inventaire complet d'un
frigo de trente références tient en une vingtaine de lignes. Les dates sont
écrites en clair — « périme dans 2 jours » plutôt que `"jours": 2` — parce que
c'est ce sur quoi il doit raisonner, et parce qu'un modèle ne connaît pas la
date du jour.

Chaque schéma d'entrée porte `additionalProperties: false`. Un modèle qui
invente un paramètre reçoit une erreur plutôt qu'un silence.

## 6.3 Ressources et invites

Trois ressources, lisibles sans appeler d'outil : `frigo://inventaire`,
`frigo://courses`, `frigo://urgences`.

Trois invites toutes faites, que le client propose à l'utilisateur :

| Invite | Ce qu'elle demande |
|---|---|
| `recette` | un plat réalisable avec le stock, construit autour de ce qui périme |
| `menu_semaine` | des menus pour *n* personnes, plus la liste de ce qui manque |
| `anti_gaspi` | quoi faire ce soir de ce qui périme dans les trois jours |

Chacune dit au modèle **dans quel ordre appeler les outils**. C'est ce qui fait
la différence entre une recette plausible et une recette réalisable : sans
consigne, un modèle propose volontiers un plat dont il manque la moitié des
ingrédients.

## 6.4 Ce qu'un modèle ne peut pas faire

Le serveur ouvre la base en `mode=ro` et sa connexion porte `PRAGMA
query_only`, exactement comme le site public
([docs/04 §4.2](04-vitrine.md#42-pourquoi-la-lecture-seule-tient)).

Le seul outil qui modifie quelque chose est `ajouter_aux_courses`, et il
n'écrit pas non plus : il relaie au serveur du terminal, qui reste l'unique
écrivain de la base. **Aucun outil ne touche au stock.** Un modèle ne peut ni
ajouter ni retirer une unité du frigo — au pire, il inscrit du beurre sur la
liste de courses.

Si le serveur du terminal est arrêté, l'outil le dit et la lecture continue de
fonctionner.

## 6.5 Sous compose

Le service `mcp` est dans la pile, sur le port 8082 :

```bash
./demarrer.sh
curl -s http://127.0.0.1:8082/sante
```

```json
{"ok": true, "protocole": "2025-06-18", "lecture_seule": true, "outils": 7}
```

`FRIGO_PORT_MCP` change le port publié, `FRIGO_FILS_MCP` le nombre d'appels
d'outils simultanés. Voir [docs/05-production.md](05-production.md).

> **Le port 8082 n'a aucune authentification.** C'est voulu : il est fait pour
> un modèle qui tourne sur le même réseau domestique. Ne l'exposez pas sur
> Internet — tout ce que contient le frigo y serait lisible, et n'importe qui
> pourrait allonger la liste de courses.

## 6.6 Quand ça ne va pas

| Symptôme | Cause |
|---|---|
| Le client ne voit aucun outil | il attend du `stdio` ; ce serveur est en HTTP, vérifiez `"type": "http"` |
| `405` sur l'URL | le client tente un `GET` pour ouvrir un flux SSE ; ce serveur n'en ouvre pas, ce qui est permis |
| `base absente` au démarrage | lancez `backend/serveur.py` d'abord, il crée la base |
| `Impossible d'ecrire sur la liste` | le serveur du terminal ne répond pas ; `--serveur` ou `FRIGO_SERVEUR` pointe au mauvais endroit |
| Le modèle invente des produits | il n'a pas appelé les outils : les invites de §6.3 lui disent de le faire |
