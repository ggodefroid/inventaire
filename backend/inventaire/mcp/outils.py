"""Ce que le modele peut demander au frigo.

Chaque outil rend du **texte**, pas du JSON. Un modele lit mieux un tableau
aligne qu'une structure imbriquee, et le texte coute moins de jetons : la
liste complete d'un frigo de trente references tient en une vingtaine de
lignes. Les dates sont ecrites en clair -- « perime dans 2 jours » plutot que
`"jours": 2` -- parce que c'est ce sur quoi le modele doit raisonner.
"""

from __future__ import annotations

import datetime as dt
import json
import urllib.error
import urllib.parse
import urllib.request

DELAI_RELAIS = 6.0


# --------------------------------------------------------------- redaction

def _echeance(jours: int | None, date: str) -> str:
    if jours is None:
        return "sans date"
    if jours < 0:
        return f"PERIME depuis {-jours} j"
    if jours == 0:
        return "perime aujourd'hui"
    if jours == 1:
        return "perime demain"
    if jours <= 31:
        return f"perime dans {jours} j"
    return f"perime le {date}"


def _ligne(poste: dict) -> str:
    morceaux = [f"{poste['total']}x {poste['nom'] or poste['code']}"]
    if poste.get("marque"):
        morceaux.append(f"({poste['marque']})")
    if poste.get("contenance"):
        morceaux.append(poste["contenance"])
    morceaux.append("- " + _echeance(poste.get("jours"), poste.get("peremption") or ""))
    if poste.get("nutriscore"):
        morceaux.append(f"[Nutri-Score {poste['nutriscore'].upper()}]")
    return " ".join(morceaux)


def _categories(poste: dict) -> str:
    return (poste.get("categories") or "").lower()


# ------------------------------------------------------------------ outils

def inventaire(lecture, *, trier: str = "peremption") -> str:
    postes = lecture.postes()
    if not postes:
        return "Le frigo est vide."
    if trier == "nom":
        postes.sort(key=lambda p: (p["nom"] or p["code"]).lower())
    else:
        postes.sort(key=lambda p: (p["jours"] is None, p["jours"] if p["jours"] is not None else 0))

    unites = sum(p["total"] for p in postes)
    lignes = [f"{len(postes)} references, {unites} unites au total.", ""]
    lignes += [f"- {_ligne(p)}" for p in postes]
    return "\n".join(lignes)


def bientot_perime(lecture, *, jours: int = 7) -> str:
    jours = max(0, min(int(jours), 365))
    postes = [p for p in lecture.postes()
              if p["jours"] is not None and p["jours"] <= jours]
    postes.sort(key=lambda p: p["jours"])
    if not postes:
        return f"Rien ne perime dans les {jours} prochains jours."

    perimes = [p for p in postes if p["jours"] < 0]
    reste = [p for p in postes if p["jours"] >= 0]
    lignes = []
    if perimes:
        lignes.append("DEJA PERIME, a jeter ou a verifier :")
        lignes += [f"- {_ligne(p)}" for p in perimes]
        lignes.append("")
    if reste:
        lignes.append(f"A consommer sous {jours} jours :")
        lignes += [f"- {_ligne(p)}" for p in reste]
    return "\n".join(lignes)


def chercher(lecture, *, terme: str) -> str:
    terme = (terme or "").strip().lower()
    if not terme:
        return "Precisez un terme de recherche."
    trouves = [p for p in lecture.postes()
               if terme in " ".join(filter(None, [
                   p["nom"], p["marque"], p["code"], p.get("categories"),
                   p.get("labels"), p.get("origine")])).lower()]
    if not trouves:
        return f"Rien ne correspond a « {terme} » dans le frigo."
    return "\n".join(f"- {_ligne(p)}" for p in trouves)


def details_produit(lecture, *, code: str) -> str:
    article = lecture.article("".join(c for c in (code or "") if c.isalnum())[:32])
    if article is None:
        return "Code inconnu."
    p = article
    lignes = [f"{p.get('nom') or p.get('code')} — {p.get('marque') or 'sans marque'}"]
    if p.get("contenance"):
        lignes.append(f"Conditionnement : {p['contenance']}")
    notes = []
    if p.get("nutriscore"):
        notes.append(f"Nutri-Score {p['nutriscore'].upper()}")
    if p.get("nova"):
        notes.append(f"NOVA {p['nova']}")
    if notes:
        lignes.append(" · ".join(notes))
    n = p.get("nutrition") or {}
    if n:
        lignes.append("Pour 100 g : " + ", ".join(
            f"{cle} {valeur}" for cle, valeur in n.items() if valeur is not None))
    for champ, etiquette in (("ingredients", "Ingredients"), ("allergenes", "Allergenes"),
                             ("labels", "Labels"), ("origine", "Origine")):
        if p.get(champ):
            lignes.append(f"{etiquette} : {p[champ]}")
    lots = p.get("lots") or []
    if lots:
        lignes.append("En stock : " + ", ".join(
            f"{l['qte']} unite(s) {_echeance(l.get('jours'), l.get('peremption') or '')}"
            for l in lots))
    return "\n".join(lignes)


def liste_courses(lecture) -> str:
    articles = lecture.courses()
    if not articles:
        return "La liste de courses est vide."
    a_prendre = [a for a in articles if not a["pris"]]
    panier = [a for a in articles if a["pris"]]
    lignes = []
    if a_prendre:
        lignes.append("A prendre :")
        lignes += [f"- {a['qte']}x {a['libelle']}"
                   + (f" ({a['marque']})" if a["marque"] else "") for a in a_prendre]
    else:
        lignes.append("Rien a prendre.")
    if panier:
        lignes += ["", "Deja dans le panier :"]
        lignes += [f"- {a['qte']}x {a['libelle']}" for a in panier]
    return "\n".join(lignes)


def ajouter_aux_courses(lecture, *, serveur: str, libelle: str = "",
                        code: str = "", qte: int = 1) -> str:
    """Le seul outil qui modifie quelque chose -- et il n'ecrit pas ici.

    La requete part vers le serveur du terminal, unique ecrivain de la base.
    Ce processus-ci reste en lecture seule, quoi que demande le modele.
    """
    libelle = (libelle or "").strip()[:80]
    code = "".join(c for c in (code or "") if c.isalnum())[:32]
    if not libelle and not code:
        return "Precisez un libelle ou un code-barres."
    qte = max(1, min(int(qte or 1), 99))

    parametres = {"origine": "mcp", "qte": str(qte)}
    if code:
        parametres["code"] = code
    if libelle:
        parametres["libelle"] = libelle

    cible = f"{serveur.rstrip('/')}/api/courses/ajouter?" + urllib.parse.urlencode(parametres)
    try:
        requete = urllib.request.Request(cible, headers={"Accept": "application/json"})
        with urllib.request.urlopen(requete, timeout=DELAI_RELAIS) as reponse:
            charge = json.loads(reponse.read(65536) or b"{}")
    except (urllib.error.URLError, TimeoutError, OSError, ValueError) as exc:
        return (f"Impossible d'ecrire sur la liste : le serveur du terminal ne "
                f"repond pas ({exc}). La lecture, elle, fonctionne.")
    if not charge.get("ok"):
        return f"Refus du serveur : {charge.get('erreur') or 'raison inconnue'}"
    return charge.get("message") or "Ajoute."


def resume(lecture) -> str:
    """Une vue d'ensemble courte, pour ouvrir une conversation."""
    compteurs = lecture.compteurs()
    postes = lecture.postes()
    presse = [p for p in postes if p["jours"] is not None and p["jours"] <= 3]
    courses = [a for a in lecture.courses() if not a["pris"]]
    maintenant = dt.datetime.now()
    return "\n".join([
        f"Nous sommes le {maintenant.date().isoformat()}, il est "
        f"{maintenant.strftime('%H:%M')}.",
        f"Frigo : {compteurs['unites']} unites, {compteurs['references']} references.",
        f"Perime : {compteurs['perimes']} unite(s).",
        f"A consommer sous 3 jours : {len(presse)} reference(s)"
        + (" — " + ", ".join(p["nom"] or p["code"] for p in presse[:6]) if presse else ""),
        f"Liste de courses : {len(courses)} ligne(s) a prendre.",
    ])
