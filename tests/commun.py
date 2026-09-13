"""Outillage partage par les tests."""

from __future__ import annotations

import sys
import tempfile
from pathlib import Path

RACINE = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(RACINE / "backend"))

from inventaire.api import Application                          # noqa: E402
from inventaire.db import Base                                  # noqa: E402
from inventaire.images import Vignettes                         # noqa: E402

# Fiche Open Food Facts reelle, reduite aux champs demandes par off.CHAMPS.
# Figee ici pour que les tests ne dependent ni du reseau ni du catalogue.
FICHE_NUTELLA = {
    "code": "3017620422003",
    "product_name": "Nutella",
    "product_name_fr": "Nutella",
    "brands": "Ferrero, Nutella",
    "quantity": "400 g",
    "image_front_small_url":
        "https://images.openfoodfacts.org/images/products/301/762/042/2003/front_fr.200.jpg",
    "nutriscore_grade": "e",
    "nova_group": 4,
    "ecoscore_grade": "d",
    "nutrient_levels": {
        "fat": "high", "saturated-fat": "high", "sugars": "high", "salt": "low",
    },
    "nutriments": {
        "energy-kcal_100g": 539, "proteins_100g": 6.3, "carbohydrates_100g": 57.5,
        "sugars_100g": 56.3, "fat_100g": 30.9, "saturated-fat_100g": 10.6,
        "salt_100g": 0.107, "fiber_100g": 0,
    },
    "allergens_tags": ["en:milk", "en:nuts", "en:soybeans"],
    "categories_tags": ["en:breakfasts", "en:spreads", "en:sweet-spreads"],
}


_A_FERMER: list[Base] = []


def fermer_tout() -> None:
    """Referme les bases ouvertes par les tests (appele par tearDownModule)."""
    while _A_FERMER:
        _A_FERMER.pop().fermer()


def application_de_test(reponses: dict | None = None,
                        prechauffage: bool = False) -> tuple[Application, Base]:
    """Application branchee sur une base jetable, sans acces reseau.

    `reponses` associe un code-barres a une fiche Open Food Facts brute ;
    tout code absent de la table est traite comme introuvable au catalogue.

    Le prechauffage des photos est coupe par defaut : c'est un travail de
    fond, et un test qui compte les entrees du cache ne doit pas voir
    apparaitre des vignettes qu'il n'a pas demandees.
    """
    dossier = Path(tempfile.mkdtemp(prefix="frigo-test-"))
    base = Base(dossier / "test.db")
    vignettes = Vignettes(dossier / "cache", agent="test/1.0",
                          ouvriers=2 if prechauffage else 0)
    application = Application(base, vignettes, en_ligne=True)
    _A_FERMER.append(base)

    table = reponses if reponses is not None else {}

    from inventaire import off
    from inventaire.off import normaliser

    def interroger_factice(code, delai=8.0, agent=""):
        fiche = table.get(code)
        return normaliser(fiche) if fiche else None

    off.interroger = interroger_factice
    return application, base
