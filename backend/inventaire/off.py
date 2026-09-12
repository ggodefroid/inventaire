"""Interrogation d'Open Food Facts, et traduction vers les colonnes maison.

Le terminal ne sort jamais sur Internet : il n'a ni le TLS moderne qu'exige
openfoodfacts.org, ni les certificats racines actuels dans son magasin de 2005.
C'est donc ce module qui fait l'appel HTTPS, reduit la reponse -- une fiche
complete depasse les 100 ko -- aux quelques champs affichables sur 240 pixels,
et range le resultat dans SQLite.

Le cache est autoritaire : une fois une fiche recuperee, bipper le meme produit
ne provoque plus aucun trafic pendant la duree de validite. Un code absent du
catalogue est memorise comme tel, sinon chaque bip d'un produit sans code-barres
public relancerait une requete inutile.
"""

from __future__ import annotations

import datetime as dt
import json
import logging
import urllib.error
import urllib.request

from . import codebarres
from .db import Base

__all__ = ["ErreurOff", "interroger", "resoudre", "AGENT"]

log = logging.getLogger("inventaire.off")

POINT_ENTREE = "https://world.openfoodfacts.org/api/v2/product/{code}.json"
# lc/cc orientent les champs libres et les taxonomies vers le francais.
LANGUE = "lc=fr&cc=fr"

# Demander explicitement les champs divise la reponse par vingt. Sur une
# liaison domestique ca ne se voit pas, mais l'API publique est gratuite et
# mutualisee : ne pas tirer 100 ko pour en garder 800 octets est la moindre
# des politesses.
CHAMPS = ",".join([
    "code", "product_name", "product_name_fr", "generic_name", "generic_name_fr",
    "abbreviated_product_name", "brands", "quantity", "serving_size",
    "image_front_small_url", "image_front_thumb_url", "image_front_url",
    "image_small_url", "image_url",
    "nutriscore_grade", "nutrition_grades", "nova_group",
    "ecoscore_grade", "environmental_score_grade",
    "nutrient_levels", "nutriments",
    "allergens_tags", "traces_tags", "additives_tags", "labels_tags",
    "categories", "categories_tags", "origins", "countries_tags",
    "ingredients_text_fr", "ingredients_text",
])

# Open Food Facts limite les clients anonymes : un agent identifiable evite
# de se faire refouler, et permet de nous signaler en cas d'abus.
AGENT = "inventaire-frigo/1.0 (terminal code-barres, usage domestique)"

VALIDITE_TROUVE = dt.timedelta(days=60)
VALIDITE_INCONNU = dt.timedelta(days=3)

NIVEAUX = {"low": "low", "moderate": "moderate", "high": "high"}

# Open Food Facts range ses etiquettes sous une taxonomie canonique anglaise,
# quelle que soit la langue du produit. On la traduit ici : l'ecran du terminal
# est en francais, et « nuts » sur une etagere ne dit pas « fruits a coque » a
# quelqu'un qui cherche ce qu'il peut manger.
#
# Sans caracteres hors Latin-1 : la Tahoma d'une image Windows CE n'est pas
# garantie complete, et « oeufs » vaut mieux qu'un carre vide.
ALLERGENES_FR = {
    "milk": "lait", "gluten": "gluten", "nuts": "fruits a coque",
    "peanuts": "arachides", "soybeans": "soja", "eggs": "oeufs",
    "fish": "poisson", "crustaceans": "crustaces", "molluscs": "mollusques",
    "celery": "celeri", "mustard": "moutarde", "sesame seeds": "sesame",
    "sesame": "sesame", "lupin": "lupin",
    "sulphur dioxide and sulphites": "sulfites", "sulphites": "sulfites",
}

LABELS_FR = {
    "organic": "bio", "eu organic": "bio UE",
    "ab agriculture biologique": "AB", "bio": "bio",
    "no gluten": "sans gluten", "gluten free": "sans gluten",
    "vegetarian": "vegetarien", "vegan": "vegan",
    "fair trade": "commerce equitable", "max havelaar": "Max Havelaar",
    "palm oil free": "sans huile de palme",
    "no preservatives": "sans conservateur", "no colorings": "sans colorant",
    "no added sugar": "sans sucres ajoutes", "sugar free": "sans sucre",
    "source of fibre": "source de fibres", "high in fibre": "riche en fibres",
    "no artificial flavors": "sans arome artificiel",
    "no artificial colors": "sans colorant artificiel",
    "certified by ecocert": "Ecocert", "rainforest alliance": "Rainforest Alliance",
    "utz certified": "UTZ", "msc": "MSC", "asc": "ASC",
    "lactose free": "sans lactose", "halal": "halal", "kosher": "casher",
    "pdo": "AOP", "pgi": "IGP", "label rouge": "Label Rouge",
    "made in france": "fabrique en France", "produit en france": "produit en France",
    "nutriscore": "", "sans ogm": "sans OGM", "no gmos": "sans OGM",
}

# Mentions d'emballage ou de tri : vraies, mais sans interet devant un frigo.
LABELS_IGNORES = frozenset([
    "triman", "green dot", "point vert", "sorting instructions",
    "recyclable", "recycling instructions", "ecoscore", "eco score",
    "carbon footprint", "eu agriculture", "non eu agriculture",
    "eu non eu agriculture", "sgs", "iso 9001", "iso 14001",
])

# Toute etiquette commencant par l'un de ces prefixes est ecartee : Open Food
# Facts y range la note deja affichee en pastille, et les mentions de tri.
LABELS_PREFIXES_IGNORES = ("nutriscore", "ecoscore", "green score",
                           "nutrition grade", "packaging")


class ErreurOff(RuntimeError):
    """Panne reseau ou reponse illisible -- a distinguer d'un produit absent."""


def _texte(valeur, taille: int) -> str | None:
    if valeur is None:
        return None
    texte = str(valeur).strip()
    return texte[:taille] or None


def _nom(fiche: dict) -> str | None:
    for cle in ("product_name_fr", "product_name", "abbreviated_product_name",
                "generic_name_fr", "generic_name"):
        valeur = (fiche.get(cle) or "").strip()
        if valeur:
            return valeur[:120]
    return None


def _note(valeur) -> str | None:
    """Une note a..e, ou rien. Open Food Facts emet aussi 'unknown'."""
    lettre = (str(valeur or "")).strip().lower()
    return lettre if lettre in ("a", "b", "c", "d", "e") else None


def _nombre(nutriments: dict, *cles) -> float | None:
    for cle in cles:
        valeur = nutriments.get(cle)
        if valeur in (None, ""):
            continue
        try:
            nombre = float(valeur)
        except (TypeError, ValueError):
            continue
        if nombre >= 0:
            return round(nombre, 2)
    return None


def _etiquettes(tags, maximum: int = 4, taille: int = 120,
                traduction: dict | None = None,
                ignores: frozenset = frozenset()) -> str | None:
    """'en:milk' -> 'lait'. Traduit, dedoublonne, et ecarte le bruit."""
    if not isinstance(tags, list):
        return None
    propres: list[str] = []
    for tag in tags:
        brut = str(tag).split(":", 1)[-1].replace("-", " ").strip().lower()
        if not brut or brut in ignores:
            continue
        if ignores and brut.startswith(LABELS_PREFIXES_IGNORES):
            continue
        texte = traduction.get(brut, brut) if traduction else brut
        # Open Food Facts empile souvent 'organic', 'eu-organic' et
        # 'fr:ab-agriculture-biologique' : trois facons de dire bio.
        if texte and texte not in propres:
            propres.append(texte)
        if len(propres) >= maximum:
            break
    return ", ".join(propres)[:taille] or None


def _additifs(tags) -> str | None:
    """'en:e322i' -> 'E322i'. Les numeros E se lisent mieux en majuscules."""
    if not isinstance(tags, list):
        return None
    vus: list[str] = []
    for tag in tags:
        code = str(tag).split(":", 1)[-1].strip()
        if not code:
            continue
        code = code[0].upper() + code[1:]
        # e322 et e322i designent la meme lecithine : garder la racine suffit.
        racine = code.rstrip("abcdefghijklmnopqrstuvwxyz")
        if any(deja.startswith(racine) for deja in vus):
            continue
        vus.append(code)
        if len(vus) >= 8:
            break
    return ", ".join(vus)[:160] or None


def _ingredients(fiche: dict) -> str | None:
    for cle in ("ingredients_text_fr", "ingredients_text"):
        texte = (fiche.get(cle) or "").strip()
        if texte:
            # Une liste d'ingredients depasse couramment 500 caracteres ;
            # au-dela, l'ecran du terminal ne suit plus de toute facon.
            return " ".join(texte.split())[:400]
    return None


def _image(fiche: dict) -> str | None:
    """La 'small' (200 px) suffit largement : l'ecran fait 240 px de large."""
    for cle in ("image_front_small_url", "image_front_url", "image_small_url",
                "image_url", "image_front_thumb_url"):
        url = (fiche.get(cle) or "").strip()
        if url.startswith("http"):
            return url[:400]
    return None


def normaliser(fiche: dict) -> dict:
    """Reduit une fiche Open Food Facts aux colonnes de la table produit."""
    niveaux = fiche.get("nutrient_levels") or {}
    nutriments = fiche.get("nutriments") or {}
    return {
        "nom": _nom(fiche),
        "marque": _texte((fiche.get("brands") or "").split(",")[0], 60),
        "quantite": _texte(fiche.get("quantity"), 30),
        "image_url": _image(fiche),
        "nutriscore": _note(fiche.get("nutriscore_grade") or fiche.get("nutrition_grades")),
        "nova": _entier_nova(fiche.get("nova_group")),
        "ecoscore": _note(fiche.get("ecoscore_grade")
                          or fiche.get("environmental_score_grade")),
        "niv_graisses": NIVEAUX.get(niveaux.get("fat")),
        "niv_satures": NIVEAUX.get(niveaux.get("saturated-fat")),
        "niv_sucres": NIVEAUX.get(niveaux.get("sugars")),
        "niv_sel": NIVEAUX.get(niveaux.get("salt")),
        "kcal": _nombre(nutriments, "energy-kcal_100g", "energy-kcal_serving"),
        "proteines": _nombre(nutriments, "proteins_100g"),
        "glucides": _nombre(nutriments, "carbohydrates_100g"),
        "sucres": _nombre(nutriments, "sugars_100g"),
        "lipides": _nombre(nutriments, "fat_100g"),
        "satures": _nombre(nutriments, "saturated-fat_100g"),
        "sel": _nombre(nutriments, "salt_100g"),
        "fibres": _nombre(nutriments, "fiber_100g"),
        "energie_kj": _nombre(nutriments, "energy-kj_100g", "energy_100g"),
        "allergenes": _etiquettes(fiche.get("allergens_tags"),
                                  traduction=ALLERGENES_FR),
        "traces": _etiquettes(fiche.get("traces_tags"), traduction=ALLERGENES_FR),
        "additifs": _additifs(fiche.get("additives_tags")),
        "labels": _etiquettes(fiche.get("labels_tags"), 5, traduction=LABELS_FR,
                              ignores=LABELS_IGNORES),
        # Les categories sont souvent deja en francais dans le champ libre ;
        # la taxonomie, elle, est toujours en anglais.
        "categories": (_texte(fiche.get("categories"), 120)
                       or _etiquettes(fiche.get("categories_tags"), 3)),
        "portion": _texte(fiche.get("serving_size"), 40),
        "origine": (_texte(fiche.get("origins"), 60)
                    or _etiquettes(fiche.get("countries_tags"), 2, 60)),
        "ingredients": _ingredients(fiche),
    }


def _entier_nova(valeur) -> int | None:
    try:
        nova = int(valeur)
    except (TypeError, ValueError):
        return None
    return nova if 1 <= nova <= 4 else None


def interroger(code: str, *, delai: float = 8.0, agent: str = AGENT) -> dict | None:
    """Appelle Open Food Facts pour une forme de code. None = absent."""
    url = POINT_ENTREE.format(code=code) + "?" + LANGUE + "&fields=" + CHAMPS
    requete = urllib.request.Request(url, headers={
        "User-Agent": agent,
        "Accept": "application/json",
        "Accept-Encoding": "identity",
    })
    try:
        with urllib.request.urlopen(requete, timeout=delai) as reponse:
            charge = json.loads(reponse.read().decode("utf-8", "replace"))
    except urllib.error.HTTPError as exc:
        if exc.code == 404:
            return None
        raise ErreurOff(f"HTTP {exc.code}") from exc
    except (urllib.error.URLError, TimeoutError, OSError) as exc:
        raise ErreurOff(f"reseau indisponible : {exc}") from exc
    except json.JSONDecodeError as exc:
        raise ErreurOff("reponse illisible") from exc

    if charge.get("status") == 0 or not charge.get("product"):
        return None
    fiche = normaliser(charge["product"])
    return fiche if fiche.get("nom") else None


def _perimee(maj: str | None, source: str | None) -> bool:
    if not maj:
        return True
    try:
        pose = dt.datetime.fromisoformat(maj)
    except ValueError:
        return True
    validite = VALIDITE_INCONNU if source == "inconnu" else VALIDITE_TROUVE
    return dt.datetime.now() - pose > validite


def resoudre(base: Base, code_lu: str, *, en_ligne: bool = True,
             forcer: bool = False, delai: float = 8.0,
             agent: str = AGENT) -> tuple[dict, str]:
    """(fiche produit, code canonique) a partir du code tel qu'il a ete lu.

    Trois temps, du moins couteux au plus couteux :

    1. le cache. Une fiche **nommee** trouvee sous n'importe quelle forme du
       code fait autorite, et rien ne part sur le reseau ;
    2. le catalogue, interroge sur chaque forme plausible. C'est lui qui
       tranche quand l'arithmetique ne peut pas -- un EAN-13 ampute a une
       chance sur dix de ressembler a un UPC-A valide ;
    3. l'arithmetique seule, quand le reseau est absent ou le produit inconnu.

    Ne leve jamais : un frigo doit rester utilisable quand la box est en panne.
    """
    connu = base.alias(code_lu)
    formes = [connu] if connu else codebarres.variantes(code_lu,
                                                        base.troncatures() >= 2)

    if not forcer:
        for essai in formes:
            ligne = base.produit(essai)
            if ligne is None or not ligne["nom"]:
                continue
            if ligne["source"] == "manuel" or not _perimee(ligne["maj"],
                                                           ligne["source"]):
                return dict(ligne), essai

        # Produit deja cherche en vain il y a peu : on ne recommence pas.
        provisoire = connu or codebarres.reparer(code_lu)
        marque = base.produit(provisoire)
        if marque is not None and marque["source"] == "inconnu" \
                and not _perimee(marque["maj"], "inconnu"):
            return dict(marque), provisoire

    if en_ligne:
        for essai in formes:
            try:
                fiche = interroger(essai, delai=delai, agent=agent)
            except ErreurOff as exc:
                log.info("Open Food Facts injoignable pour %s : %s", essai, exc)
                break               # liaison tombee : insister ne sert a rien
            if fiche is None:
                continue
            ancienne = base.produit(essai)
            if ancienne is not None and ancienne["source"] == "manuel":
                # Un libelle saisi a la main ne doit pas etre ecrase, mais les
                # indicateurs et la photo, eux, sont bons a prendre.
                fiche.pop("nom", None)
                base.enregistrer_produit(essai, source="manuel", **fiche)
            else:
                base.enregistrer_produit(essai, source="openfoodfacts", **fiche)
            return dict(base.produit(essai)), essai

    canonique = connu or codebarres.reparer(code_lu)
    ligne = base.produit(canonique)
    if ligne is None:
        if not en_ligne:
            return {"code": canonique, "source": "hors-ligne"}, canonique
        base.enregistrer_produit(canonique, source="inconnu")
        log.info("code %s absent d'Open Food Facts", canonique)
        ligne = base.produit(canonique)
    return dict(ligne or {"code": canonique, "source": "reseau"}), canonique
