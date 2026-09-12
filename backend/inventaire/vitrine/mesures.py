"""Tout ce qu'on peut tirer d'un frigo : indicateurs, series, curiosites.

Le module ne lit rien lui-meme. Il recoit les postes et le journal deja
extraits par `lecture`, et rend une structure unique -- l'instantane -- que
le navigateur consomme telle quelle. Un seul calcul par changement de base,
diffuse a tous les visiteurs connectes.

Trois familles de chiffres, volontairement separees a l'affichage :

* les **KPI**, numerotes, avec leur formule et une cible. Ce sont les seuls
  qui pretendent dire quelque chose de l'etat du frigo.
* les **chiffres**, exacts et utiles, mais sans jugement : tonnage, macros,
  marques, couverture du catalogue.
* les **curiosites**, exactes aussi, mais sans le moindre interet pratique.
  Elles sont la parce qu'une base de donnees d'aliments contient de quoi
  calculer la distance de marche que represente un frigo plein, et que ne pas
  le faire serait du gachis.
"""

from __future__ import annotations

import datetime as dt
import math
import re
from collections import Counter, defaultdict

from .lecture import aujourdhui, jours_restants

__all__ = ["instantane", "masse_unitaire"]

# Les libelles de ce module s'affichent dans un navigateur : ils portent leurs
# accents. Ceux de `off.py`, qui finissent sur l'ecran du terminal, n'en ont
# pas -- rien ne garantit que la Tahoma d'une image Windows CE soit complete.

# --- conversions de comptoir, pour les curiosites -------------------------
KCAL_JOUR = 2000.0            # ration journaliere de reference (reglement UE 1169/2011)
MORCEAU_SUCRE_G = 4.0         # un morceau n_4
CUILLERE_SEL_G = 5.8          # une cuillere a cafe rase
KCAL_PAR_KM = 62.0            # marche a 5 km/h, 70 kg
POT_NUTELLA_KCAL = 539.0 * 4  # un pot de 400 g
CHAT_KG = 4.2
BAGUETTE_G = 250.0
DUREE_BIP_S = 0.35            # gachette pressee, decodage, bip

NOTES = ("a", "b", "c", "d", "e")
NIVEAUX_ORDRE = {"low": 1, "moderate": 2, "high": 3}

# L'ordre des alternatives compte : « 800 gram » doit trouver `gram` avant
# `g`, sinon la limite de mot fait echouer toute la lecture.
_MASSE = re.compile(
    r"(?:(\d+)\s*[x×*]\s*)?"
    r"(\d+(?:[.,]\d+)?)\s*"
    r"(kg|mg|cl|dl|ml|gramme|gram|gr|g|litre|l)s?\b", re.I)

_EN_GRAMMES = {"mg": 0.001, "g": 1.0, "gr": 1.0, "gram": 1.0, "gramme": 1.0,
               "kg": 1000.0, "ml": 1.0, "cl": 10.0, "dl": 100.0,
               "l": 1000.0, "litre": 1000.0}
_LIQUIDES = {"ml", "cl", "dl", "l", "litre"}


def masse_unitaire(contenance: str) -> tuple[float | None, bool]:
    """« 6 x 125 g » -> (750.0, False). Un litre d'eau pese un kilo, ca suffit.

    Open Food Facts n'a pas de champ masse : le conditionnement est une chaine
    libre saisie par des contributeurs. On y lit ce qui se lit, et on renonce
    sur le reste plutot que d'inventer un poids.
    """
    if not contenance:
        return None, False
    trouve = _MASSE.search(contenance.replace(",", "."))
    if trouve is None:
        return None, False
    multiple = int(trouve.group(1)) if trouve.group(1) else 1
    try:
        valeur = float(trouve.group(2))
    except ValueError:
        return None, False
    unite = trouve.group(3).lower()
    grammes = valeur * _EN_GRAMMES.get(unite, 1.0) * max(1, min(multiple, 99))
    if not 0 < grammes <= 50_000:
        return None, False
    return round(grammes, 1), unite in _LIQUIDES


def _somme(valeurs) -> float:
    return float(sum(v for v in valeurs if v))


def _taux(numerateur: float, denominateur: float, defaut: float = 0.0) -> float:
    return numerateur / denominateur if denominateur else defaut


def _arrondi(valeur: float, decimales: int = 1) -> float:
    if valeur is None or not math.isfinite(valeur):
        return 0.0
    return round(valeur, decimales)


def _entropie(quantites: list[int]) -> tuple[float, float]:
    """(bits de Shannon, equitabilite de Pielou en 0..1).

    Un frigo avec douze references en une unite chacune est plus « divers »
    que le meme frigo dont onze douziemes sont des yaourts. C'est exactement
    ce que mesure l'entropie, et c'est la seule facon honnete de mettre un
    chiffre sur la variete d'un stock.
    """
    total = sum(quantites)
    if total <= 0 or len(quantites) < 2:
        return 0.0, 0.0
    bits = -sum((q / total) * math.log2(q / total) for q in quantites if q > 0)
    return bits, bits / math.log2(len(quantites))


def _gini(quantites: list[int]) -> float:
    """0 = stock reparti a l'identique, 1 = tout sur une seule reference."""
    valeurs = sorted(q for q in quantites if q > 0)
    n = len(valeurs)
    total = sum(valeurs)
    if n < 2 or total <= 0:
        return 0.0
    cumul = sum((i + 1) * q for i, q in enumerate(valeurs))
    return (2 * cumul) / (n * total) - (n + 1) / n


def _date(horodatage: str) -> dt.date | None:
    try:
        return dt.date.fromisoformat(horodatage[:10])
    except (ValueError, TypeError, IndexError):
        return None


def _horodatage(brut: str) -> dt.datetime | None:
    try:
        return dt.datetime.fromisoformat(brut)
    except (ValueError, TypeError):
        return None


# ---------------------------------------------------------------- agregats

def _nutriments(postes: list[dict]) -> dict:
    """Ce que contient le frigo, en grammes et en calories, tout confondu.

    Les fiches donnent des valeurs pour 100 g ; il faut donc une masse pour
    chaque poste. Les produits dont le conditionnement n'est pas lisible sont
    comptes a part : mieux vaut un total honnete sur 80 % du stock qu'un total
    complet et faux.
    """
    total = {"masse": 0.0, "kcal": 0.0, "proteines": 0.0, "glucides": 0.0,
             "sucres": 0.0, "lipides": 0.0, "satures": 0.0, "sel": 0.0,
             "fibres": 0.0, "liquides": 0.0}
    peses = couverts = 0
    for poste in postes:
        grammes, liquide = masse_unitaire(poste["contenance"])
        poste["masse_unitaire"] = grammes
        poste["liquide"] = liquide
        if grammes is None:
            continue
        peses += poste["total"]
        masse = grammes * poste["total"]
        total["masse"] += masse
        if liquide:
            total["liquides"] += masse
        nutrition = poste["nutrition"]
        if nutrition.get("kcal"):
            couverts += poste["total"]
        for cle in ("kcal", "proteines", "glucides", "sucres", "lipides",
                    "satures", "sel", "fibres"):
            valeur = nutrition.get(cle)
            if valeur:
                total[cle] += float(valeur) * masse / 100.0
    total["peses"] = peses
    total["couverts"] = couverts
    return total


def _repartition(postes: list[dict], cle: str, ordre: tuple) -> list[dict]:
    compte = Counter()
    for poste in postes:
        valeur = poste.get(cle) or ""
        compte[str(valeur).lower() if valeur else ""] += poste["total"]
    lignes = [{"cle": str(v), "valeur": compte.get(str(v), 0)} for v in ordre]
    inconnus = compte.get("", 0) + compte.get("0", 0)
    if inconnus:
        lignes.append({"cle": "?", "valeur": inconnus})
    return [ligne for ligne in lignes if ligne["valeur"]]


def _urgences(postes: list[dict]) -> list[dict]:
    seaux = {"perime": 0, "72h": 0, "semaine": 0, "quinzaine": 0,
             "mois": 0, "large": 0, "sansdate": 0}
    for poste in postes:
        for lot in poste["lots"]:
            jours = lot["jours"]
            if jours is None:
                seaux["sansdate"] += lot["qte"]
            elif jours < 0:
                seaux["perime"] += lot["qte"]
            elif jours <= 2:
                seaux["72h"] += lot["qte"]
            elif jours <= 6:
                seaux["semaine"] += lot["qte"]
            elif jours <= 14:
                seaux["quinzaine"] += lot["qte"]
            elif jours <= 30:
                seaux["mois"] += lot["qte"]
            else:
                seaux["large"] += lot["qte"]
    etiquettes = {
        "perime": "périmé", "72h": "sous 72 h", "semaine": "sous 7 j",
        "quinzaine": "sous 15 j", "mois": "sous 30 j", "large": "au-delà",
        "sansdate": "sans date",
    }
    return [{"cle": etiquettes[c], "valeur": v} for c, v in seaux.items() if v]


def _liste_etiquettes(postes: list[dict], champ: str, maximum: int = 10) -> list[dict]:
    compte = Counter()
    for poste in postes:
        for brut in (poste.get(champ) or "").split(","):
            texte = brut.strip()
            if texte:
                compte[texte] += poste["total"]
    return [{"cle": cle, "valeur": valeur}
            for cle, valeur in compte.most_common(maximum)]


# ------------------------------------------------------------------ series

def _mouvements_par_jour(journal: list[tuple], profondeur: int = 30) -> list[dict]:
    fin = aujourdhui()
    debut = fin - dt.timedelta(days=profondeur - 1)
    ajouts: Counter = Counter()
    retraits: Counter = Counter()
    for ts, delta, _action in journal:
        jour = _date(ts)
        if jour is None or jour < debut or jour > fin:
            continue
        if delta > 0:
            ajouts[jour] += delta
        elif delta < 0:
            retraits[jour] += -delta
    serie = []
    for i in range(profondeur):
        jour = debut + dt.timedelta(days=i)
        serie.append({"jour": jour.isoformat(),
                      "ajouts": ajouts.get(jour, 0),
                      "retraits": retraits.get(jour, 0)})
    return serie


def _charge(journal: list[tuple], unites: int, profondeur: int = 60) -> list[dict]:
    """Le stock jour par jour, reconstitue a rebours depuis l'etat actuel.

    Aucune table ne garde l'historique du niveau : on connait le stock de
    maintenant et tous les mouvements. Le stock d'hier soir, c'est celui de
    maintenant moins ce qui a bouge depuis. On remonte ainsi de jour en jour.
    """
    fin = aujourdhui()
    ordonne = [(_date(ts), delta) for ts, delta, _ in journal]
    ordonne = [(jour, delta) for jour, delta in ordonne if jour is not None]
    ordonne.sort()
    curseur = len(ordonne) - 1
    posterieur = 0
    serie = []
    for i in range(profondeur):
        jour = fin - dt.timedelta(days=i)
        while curseur >= 0 and ordonne[curseur][0] > jour:
            posterieur += ordonne[curseur][1]
            curseur -= 1
        serie.append({"jour": jour.isoformat(), "unites": max(0, unites - posterieur)})
    serie.reverse()
    return serie


def _horizon(postes: list[dict], profondeur: int = 45) -> list[dict]:
    compte: Counter = Counter()
    for poste in postes:
        for lot in poste["lots"]:
            if lot["jours"] is None:
                continue
            compte[max(-1, min(lot["jours"], profondeur))] += lot["qte"]
    return [{"jours": j, "unites": compte.get(j, 0)} for j in range(-1, profondeur + 1)]


def _rythme(journal: list[tuple]) -> tuple[list[int], list[int], list[list[int]]]:
    """Heures de la journee, jours de la semaine, et le croisement des deux."""
    heures = [0] * 24
    semaine = [0] * 7
    grille = [[0] * 24 for _ in range(7)]
    for ts, _delta, _action in journal:
        moment = _horodatage(ts)
        if moment is None:
            continue
        heures[moment.hour] += 1
        semaine[moment.weekday()] += 1
        grille[moment.weekday()][moment.hour] += 1
    return heures, semaine, grille


# -------------------------------------------------------------------- KPI

def _etat(valeur: float, bon: float, moyen: float, sens: str = "haut") -> str:
    if sens == "haut":
        return "bon" if valeur >= bon else "moyen" if valeur >= moyen else "mauvais"
    return "bon" if valeur <= bon else "moyen" if valeur <= moyen else "mauvais"


def _kpis(postes, compteurs, nutriments, journal) -> list[dict]:
    unites = compteurs["unites"] or 0
    refs = compteurs["references"] or 0

    # KPI 01 : fraicheur. Chaque unite vaut sa marge avant peremption,
    # plafonnee a trente jours. Sans date, on suppose la moitie du chemin.
    points = 0.0
    for poste in postes:
        for lot in poste["lots"]:
            jours = lot["jours"]
            if jours is None:
                points += 0.5 * lot["qte"]
            else:
                points += max(0.0, min(jours / 30.0, 1.0)) * lot["qte"]
    fraicheur = 100.0 * _taux(points, unites)

    autonomie = _taux(nutriments["kcal"], KCAL_JOUR)
    couverture = 100.0 * _taux(
        sum(1 for p in postes if p["source"] == "openfoodfacts"), refs)

    pression = 100.0 * _taux(
        sum(lot["qte"] for p in postes for lot in p["lots"]
            if lot["jours"] is not None and lot["jours"] <= 2), unites)

    nova_pondere = _taux(
        sum(p["nova"] * p["total"] for p in postes if p["nova"]),
        sum(p["total"] for p in postes if p["nova"]))

    recents = [(jour, delta) for jour, delta in
               ((_date(ts), delta) for ts, delta, _ in journal)
               if jour is not None and (aujourdhui() - jour).days <= 30]
    sorties = sum(-delta for _, delta in recents if delta < 0)
    rotation = sorties / 30.0 * 7.0

    densite = 100.0 * _taux(nutriments["kcal"], nutriments["masse"])
    _bits, equitabilite = _entropie([p["total"] for p in postes])
    profondeur = _taux(unites, refs)
    gaspillage = 100.0 * _taux(compteurs["perimes"], unites)

    return [
        {"rang": "01", "code": "FRAICHEUR", "titre": "Indice de fraîcheur",
         "valeur": _arrondi(fraicheur), "unite": "/100",
         "formule": "moy( min(jours/30, 1) ) x 100, pondéré par unité",
         "lecture": "marge restante avant péremption, tout le stock confondu",
         "etat": _etat(fraicheur, 65, 40), "jauge": min(100.0, fraicheur)},
        {"rang": "02", "code": "AUTONOMIE", "titre": "Autonomie calorique",
         "valeur": _arrondi(autonomie), "unite": "jours",
         "formule": f"kcal totales / {KCAL_JOUR:.0f} kcal par jour",
         "lecture": "tenue du frigo pour une personne, si tout était comestible",
         "etat": _etat(autonomie, 7, 3), "jauge": min(100.0, autonomie / 14 * 100)},
        {"rang": "03", "code": "COUVERTURE", "titre": "Couverture catalogue",
         "valeur": _arrondi(couverture), "unite": "%",
         "formule": "références avec fiche Open Food Facts / références",
         "lecture": "part du stock que le catalogue documente",
         "etat": _etat(couverture, 85, 60), "jauge": couverture},
        {"rang": "04", "code": "PRESSION", "titre": "Pression 72 h",
         "valeur": _arrondi(pression), "unite": "%",
         "formule": "unités périmant sous 72 h / unités",
         "lecture": "ce qu'il faut manger cette semaine",
         "etat": _etat(pression, 10, 25, sens="bas"), "jauge": pression},
        {"rang": "05", "code": "NOVA", "titre": "Transformation moyenne",
         "valeur": _arrondi(nova_pondere, 2), "unite": "/4",
         "formule": "moyenne du groupe NOVA pondérée par les unités",
         "lecture": "1 brut, 4 ultra transformé",
         "etat": _etat(nova_pondere, 2.5, 3.4, sens="bas"),
         "jauge": nova_pondere / 4 * 100},
        {"rang": "06", "code": "ROTATION", "titre": "Rotation",
         "valeur": _arrondi(rotation), "unite": "u/sem",
         "formule": "unités sorties sur 30 j / 30 x 7",
         "lecture": "débit de consommation constaté",
         "etat": _etat(rotation, 5, 1), "jauge": min(100.0, rotation / 20 * 100)},
        {"rang": "07", "code": "DENSITE", "titre": "Densité calorique",
         "valeur": _arrondi(densite), "unite": "kcal/100g",
         "formule": "kcal totales / masse totale x 100",
         "lecture": "au-dessus de 250, le frigo est gras ou sucré",
         "etat": _etat(densite, 180, 300, sens="bas"),
         "jauge": min(100.0, densite / 500 * 100)},
        {"rang": "08", "code": "DIVERSITE", "titre": "Diversité du stock",
         "valeur": _arrondi(equitabilite * 100), "unite": "/100",
         "formule": "entropie de Shannon / log2(références)",
         "lecture": "100 = toutes les références en quantité égale",
         "etat": _etat(equitabilite * 100, 80, 55), "jauge": equitabilite * 100},
        {"rang": "09", "code": "PROFONDEUR", "titre": "Profondeur de stock",
         "valeur": _arrondi(profondeur, 2), "unite": "u/ref",
         "formule": "unités / références",
         "lecture": "nombre moyen d'exemplaires par produit",
         "etat": _etat(profondeur, 1.5, 1.1), "jauge": min(100.0, profondeur / 6 * 100)},
        {"rang": "10", "code": "GASPILLAGE", "titre": "Exposition au gaspillage",
         "valeur": _arrondi(gaspillage), "unite": "%",
         "formule": "unités déjà périmées / unités",
         "lecture": "zéro est la seule valeur acceptable",
         "etat": _etat(gaspillage, 0.01, 8, sens="bas"), "jauge": gaspillage},
    ]


# ------------------------------------------------------------- curiosites

def _curiosites(postes, compteurs, nutriments, journal, poids_base) -> list[dict]:
    unites = compteurs["unites"] or 0
    codes = [p["code"] for p in postes]
    chiffres = "".join(c for c in "".join(codes) if c.isdigit())
    palindromes = sum(1 for c in codes if c == c[::-1] and len(c) > 1)
    quantites = [p["total"] for p in postes]
    bits, _ = _entropie(quantites)

    ingredients = [p["ingredients"] for p in postes if p["ingredients"]]
    mots = set()
    for texte in ingredients:
        for morceau in re.split(r"[,;()\[\]]", texte.lower()):
            propre = morceau.strip(" .*_%0123456789")
            if 2 < len(propre) < 40:
                mots.add(propre)

    ages = []
    for poste in postes:
        for lot in poste["lots"]:
            pose = _date(lot["ajoute_le"])
            if pose:
                ages.append(((aujourdhui() - pose).days, lot["qte"]))
    age_moyen = _taux(sum(j * q for j, q in ages), sum(q for _, q in ages))
    doyen = max((j for j, _ in ages), default=0)

    sorties = sum(-delta for ts, delta, _ in journal
                  if delta < 0 and (_date(ts) or aujourdhui()) >= aujourdhui() - dt.timedelta(days=30))
    par_jour = sorties / 30.0
    vidage = ""
    if par_jour > 0.05 and unites:
        vidage = (aujourdhui() + dt.timedelta(days=min(3650, unites / par_jour))).isoformat()

    def carte(titre, valeur, unite, note):
        return {"titre": titre, "valeur": valeur, "unite": unite, "note": note}

    return [
        carte("Morceaux de sucre", int(_taux(nutriments["sucres"], MORCEAU_SUCRE_G)),
              "morceaux", f"sucre total / {MORCEAU_SUCRE_G:g} g"),
        carte("Cuillères de sel", _arrondi(_taux(nutriments["sel"], CUILLERE_SEL_G)),
              "c. à café", f"sel total / {CUILLERE_SEL_G:g} g"),
        carte("Marche équivalente", int(_taux(nutriments["kcal"], KCAL_PAR_KM)),
              "km", f"kcal / {KCAL_PAR_KM:g} kcal par km"),
        carte("En pots de Nutella", _arrondi(_taux(nutriments["kcal"], POT_NUTELLA_KCAL), 2),
              "pots de 400 g", "kcal totales / 2156 kcal"),
        carte("Le frigo en chats", _arrondi(_taux(nutriments["masse"] / 1000.0, CHAT_KG), 2),
              "chats", f"masse / {CHAT_KG:g} kg"),
        carte("En baguettes", _arrondi(_taux(nutriments["masse"], BAGUETTE_G), 1),
              "baguettes", "masse / 250 g"),
        carte("Entropie de Shannon", _arrondi(bits, 2), "bits",
              "diversité réelle de l'inventaire"),
        carte("Indice de Gini", _arrondi(_gini(quantites), 3), "",
              "0 stock égalitaire, 1 stock monopolisé"),
        carte("Chiffres scannés", len(chiffres), "chiffres",
              "tous les codes-barres bout à bout"),
        carte("Somme des codes", sum(int(c) for c in chiffres), "",
              "addition de chaque chiffre de chaque code"),
        carte("Codes palindromes", palindromes, "",
              "codes-barres lisibles dans les deux sens"),
        carte("Temps de bip cumulé", _arrondi(compteurs["mouvements"] * DUREE_BIP_S, 1),
              "s", f"{compteurs['mouvements']} mouvements x {DUREE_BIP_S:g} s"),
        carte("Ingrédients distincts", len(mots), "",
              "listes découpées et dédoublonnées"),
        carte("Texte d'étiquettes", sum(len(t) for t in ingredients), "caractères",
              "longueur cumulée des listes d'ingrédients"),
        carte("Âge moyen du stock", _arrondi(age_moyen), "jours",
              "depuis la mise au frigo, pondéré par unité"),
        carte("Doyen du frigo", doyen, "jours",
              "le lot présent depuis le plus longtemps"),
        carte("Tirage au sort périmé", _arrondi(100.0 * _taux(compteurs["perimes"], unites)),
              "%", "probabilité de tomber sur un périmé les yeux fermés"),
        carte("Frigo vide le", vidage or "jamais", "",
              "au rythme des 30 derniers jours"),
        carte("Part liquide", _arrondi(100.0 * _taux(nutriments["liquides"], nutriments["masse"])),
              "%", "ce qui se boit, en masse"),
        carte("Poids de la base", _arrondi(poids_base / 1024.0), "ko",
              "SQLite, journal WAL compris"),
        carte("Octets par unité", int(_taux(poids_base, unites)), "o",
              "coût de stockage d'une unité de frigo"),
        carte("Longueur de code moyenne", _arrondi(_taux(len(chiffres), len(codes)), 2),
              "chiffres", "EAN 13, UPC 12, et les rescapés"),
    ]


# ------------------------------------------------------------------ chiffres

def _chiffres(postes, compteurs, nutriments) -> list[dict]:
    refs = compteurs["references"] or 0
    marques = {p["marque"] for p in postes if p["marque"]}
    avec_additifs = sum(1 for p in postes if p["additifs"])
    bio = sum(1 for p in postes if "bio" in (p["labels"] or "").lower())
    sans_date = sum(lot["qte"] for p in postes for lot in p["lots"]
                    if lot["jours"] is None)
    allergenes = {a.strip() for p in postes for a in (p["allergenes"] or "").split(",")
                  if a.strip()}
    additifs = {a.strip() for p in postes for a in (p["additifs"] or "").split(",")
                if a.strip()}
    dates = [p["peremption"] for p in postes if p["peremption"]]

    def carte(titre, valeur, unite, note):
        return {"titre": titre, "valeur": valeur, "unite": unite, "note": note}

    return [
        carte("Masse en stock", _arrondi(nutriments["masse"] / 1000.0, 2), "kg",
              f"{nutriments['peses']} unités pesées sur {compteurs['unites']}"),
        carte("Énergie totale", int(nutriments["kcal"]), "kcal",
              f"{int(nutriments['kcal'] * 4.184)} kJ"),
        carte("Protéines", _arrondi(nutriments["proteines"]), "g", "tout le stock"),
        carte("Glucides", _arrondi(nutriments["glucides"]), "g",
              f"dont {_arrondi(nutriments['sucres'])} g de sucres"),
        carte("Lipides", _arrondi(nutriments["lipides"]), "g",
              f"dont {_arrondi(nutriments['satures'])} g saturés"),
        carte("Fibres", _arrondi(nutriments["fibres"]), "g", "tout le stock"),
        carte("Sel", _arrondi(nutriments["sel"], 2), "g", "chlorure de sodium"),
        carte("Marques", len(marques), "", "enseignes distinctes en rayon"),
        carte("Allergènes", len(allergenes), "", "déclarés sur les fiches"),
        carte("Additifs", len(additifs), "numéros E", "tous produits confondus"),
        carte("Produits avec additifs", f"{avec_additifs}/{refs}", "",
              f"{_arrondi(100.0 * _taux(avec_additifs, refs))} % des références"),
        carte("Produits bio", f"{bio}/{refs}", "",
              f"{_arrondi(100.0 * _taux(bio, refs))} % des références"),
        carte("Unités sans date", sans_date, "",
              "péremption jamais saisie ou inconnue"),
        carte("Péremption médiane", sorted(dates)[len(dates) // 2] if dates else "n/a",
              "", "moitié du stock avant, moitié après"),
        carte("Fiches en cache", compteurs["fiches"], "",
              f"{compteurs['manuels']} libellés saisis à la main"),
        carte("Codes reconstitués", compteurs["alias"], "",
              "clés de contrôle recalculées par le serveur"),
    ]


# ---------------------------------------------------------------- instantane

def instantane(lecture, *, journal_max: int = 120) -> dict:
    """La photographie complete du frigo, prete a etre serialisee."""
    maintenant = dt.datetime.now()
    postes = lecture.postes()
    compteurs = lecture.compteurs()
    journal_brut = lecture.journal_complet()
    nutriments = _nutriments(postes)
    heures, semaine, grille = _rythme(journal_brut)

    for poste in postes:
        poste["urgence"] = _classe_urgence(poste["jours"])
        poste["recherche"] = " ".join(filter(None, [
            poste["nom"], poste["marque"], poste["code"], poste["categories"],
            poste["labels"], poste["origine"], poste["allergenes"],
        ])).lower()

    pires = sorted((p for p in postes if p["jours"] is not None),
                   key=lambda p: p["jours"])[:8]

    return {
        "horloge": {
            "iso": maintenant.isoformat(timespec="seconds"),
            "date": maintenant.date().isoformat(),
            "heure": maintenant.strftime("%H:%M:%S"),
            "semaine": maintenant.isocalendar().week,
            "jour_annee": maintenant.timetuple().tm_yday,
        },
        "compteurs": compteurs,
        "kpi": _kpis(postes, compteurs, nutriments, journal_brut),
        "chiffres": _chiffres(postes, compteurs, nutriments),
        "curiosites": _curiosites(postes, compteurs, nutriments, journal_brut,
                                  lecture.poids_fichier()),
        "donuts": {
            "nutriscore": _repartition(postes, "nutriscore", NOTES),
            "nova": _repartition(postes, "nova", (1, 2, 3, 4)),
            "ecoscore": _repartition(postes, "ecoscore", NOTES),
            "urgence": _urgences(postes),
            "macros": [
                {"cle": "proteines", "valeur": int(nutriments["proteines"] * 4)},
                {"cle": "glucides", "valeur": int(nutriments["glucides"] * 4)},
                {"cle": "lipides", "valeur": int(nutriments["lipides"] * 9)},
            ],
        },
        "barres": {
            "marques": _classement(postes, "marque"),
            "categories": _liste_etiquettes(postes, "categories", 8),
            "additifs": _liste_etiquettes(postes, "additifs", 8),
            "allergenes": _liste_etiquettes(postes, "allergenes", 8),
            "labels": _liste_etiquettes(postes, "labels", 8),
            "origines": _liste_etiquettes(postes, "origine", 6),
        },
        "series": {
            "mouvements": _mouvements_par_jour(journal_brut),
            "charge": _charge(journal_brut, compteurs["unites"]),
            "horizon": _horizon(postes),
            "heures": heures,
            "semaine": semaine,
            "grille": grille,
        },
        "niveaux": _niveaux(postes),
        "urgents": pires,
        "postes": postes,
        "journal": lecture.journal(journal_max),
        "systeme": {
            "lecture_seule": lecture.seulement_ro,
            "base": lecture.chemin.name,
            "octets": lecture.poids_fichier(),
        },
    }


def _classe_urgence(jours: int | None) -> str:
    if jours is None:
        return "inconnu"
    if jours < 0:
        return "perime"
    if jours <= 2:
        return "critique"
    if jours <= 6:
        return "tendu"
    if jours <= 20:
        return "correct"
    return "large"


def _classement(postes: list[dict], champ: str, maximum: int = 8) -> list[dict]:
    compte = Counter()
    for poste in postes:
        valeur = (poste.get(champ) or "").strip()
        if valeur:
            compte[valeur] += poste["total"]
    return [{"cle": cle, "valeur": valeur} for cle, valeur in compte.most_common(maximum)]


def _niveaux(postes: list[dict]) -> dict:
    """Les quatre jauges d'Open Food Facts, moyennees sur le stock.

    `low`, `moderate`, `high` valent 1, 2 et 3 : la moyenne n'a pas d'unite,
    elle sert a comparer les quatre entre elles.
    """
    resultat = {}
    for champ in ("graisses", "satures", "sucres", "sel"):
        poids = total = 0
        compte = defaultdict(int)
        for poste in postes:
            niveau = poste["niveaux"].get(champ) or ""
            rang = NIVEAUX_ORDRE.get(niveau)
            if rang:
                poids += rang * poste["total"]
                total += poste["total"]
                compte[niveau] += poste["total"]
        resultat[champ] = {
            "moyenne": _arrondi(_taux(poids, total), 2),
            "repartition": [{"cle": n, "valeur": compte[n]}
                            for n in ("low", "moderate", "high") if compte[n]],
            "couvert": total,
        }
    return resultat
