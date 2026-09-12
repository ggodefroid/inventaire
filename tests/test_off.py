"""Traduction d'une fiche Open Food Facts vers les colonnes maison."""

import unittest

from tests.commun import fermer_tout, FICHE_NUTELLA, application_de_test  # noqa: F401

from inventaire import off
from inventaire.off import normaliser


class TestNormalisation(unittest.TestCase):

    def test_champs_principaux(self):
        f = normaliser(FICHE_NUTELLA)
        self.assertEqual(f["nom"], "Nutella")
        self.assertEqual(f["marque"], "Ferrero")       # premiere marque seulement
        self.assertEqual(f["quantite"], "400 g")
        self.assertEqual(f["nutriscore"], "e")
        self.assertEqual(f["nova"], 4)
        self.assertEqual(f["ecoscore"], "d")

    def test_temoins_nutritionnels(self):
        f = normaliser(FICHE_NUTELLA)
        self.assertEqual(f["niv_graisses"], "high")
        self.assertEqual(f["niv_satures"], "high")
        self.assertEqual(f["niv_sucres"], "high")
        self.assertEqual(f["niv_sel"], "low")

    def test_valeurs_pour_cent_grammes(self):
        f = normaliser(FICHE_NUTELLA)
        self.assertEqual(f["kcal"], 539)
        self.assertEqual(f["lipides"], 30.9)
        self.assertEqual(f["sel"], 0.11)               # arrondi a deux decimales

    def test_allergenes_traduits_en_francais(self):
        # La taxonomie d'Open Food Facts est en anglais quelle que soit la
        # langue du produit ; « nuts » sur une etagere ne dit rien a personne.
        f = normaliser(FICHE_NUTELLA)
        self.assertEqual(f["allergenes"], "lait, fruits a coque, soja")

    def test_categories_depuis_le_champ_libre_sinon_la_taxonomie(self):
        f = normaliser(FICHE_NUTELLA)
        self.assertEqual(f["categories"], "breakfasts, spreads, sweet spreads")
        f = normaliser(dict(FICHE_NUTELLA, categories="Pates a tartiner"))
        self.assertEqual(f["categories"], "Pates a tartiner")

    def test_labels_traduits_et_debruites(self):
        f = normaliser(dict(FICHE_NUTELLA, labels_tags=[
            "en:organic", "en:eu-organic", "fr:ab-agriculture-biologique",
            "en:nutriscore", "en:nutriscore-grade-e", "en:triman",
            "en:no-gluten", "en:green-dot"]))
        # bio trois fois de suite ne fait qu'un ; la note et les consignes de
        # tri sont deja ailleurs ou sans interet devant un frigo.
        self.assertEqual(f["labels"], "bio, bio UE, AB, sans gluten")

    def test_sans_caractere_hors_latin1(self):
        # La Tahoma d'une image Windows CE industrielle n'est pas garantie
        # complete : « oeufs » plutot que le e dans l'o.
        from inventaire.off import ALLERGENES_FR, LABELS_FR
        for table in (ALLERGENES_FR, LABELS_FR):
            for valeur in table.values():
                for caractere in valeur:
                    self.assertLess(ord(caractere), 256, f"{valeur!r}")

    def test_note_inconnue_devient_rien(self):
        # Open Food Facts emet 'unknown' et 'not-applicable' : le terminal
        # afficherait une pastille grise absurde.
        f = normaliser(dict(FICHE_NUTELLA, nutriscore_grade="unknown",
                            ecoscore_grade="not-applicable"))
        self.assertIsNone(f["nutriscore"])
        self.assertIsNone(f["ecoscore"])

    def test_nova_hors_bornes_ignore(self):
        self.assertIsNone(normaliser(dict(FICHE_NUTELLA, nova_group=9))["nova"])
        self.assertIsNone(normaliser(dict(FICHE_NUTELLA, nova_group=None))["nova"])

    def test_nom_francais_prioritaire(self):
        f = normaliser({"product_name": "Spread", "product_name_fr": "Pate a tartiner"})
        self.assertEqual(f["nom"], "Pate a tartiner")

    def test_fiche_vide_ne_casse_pas(self):
        f = normaliser({})
        self.assertIsNone(f["nom"])
        self.assertIsNone(f["kcal"])

    def test_nutriments_illisibles_ignores(self):
        f = normaliser({"nutriments": {"energy-kcal_100g": "n/a", "fat_100g": -3}})
        self.assertIsNone(f["kcal"])
        self.assertIsNone(f["lipides"])

    def test_image_prend_la_petite(self):
        f = normaliser(FICHE_NUTELLA)
        self.assertIn("200.jpg", f["image_url"])


class TestCache(unittest.TestCase):
    """Le cache doit etre autoritaire : rebipper ne doit rien retelecharger."""

    def test_une_seule_interrogation_pour_deux_scans(self):
        application, base = application_de_test({"3017620422003": FICHE_NUTELLA})
        appels = []
        vrai = off.interroger

        def compter(code, delai=8.0, agent=""):
            appels.append(code)
            return vrai(code, delai=delai, agent=agent)

        off.interroger = compter
        try:
            application.scan({"code": "3017620422003"})
            application.scan({"code": "3017620422003"})
        finally:
            off.interroger = vrai
        self.assertEqual(len(appels), 1)

    def test_code_absent_memorise_comme_inconnu(self):
        application, base = application_de_test({})
        application.scan({"code": "0000000000017"})
        self.assertEqual(base.produit("0000000000017")["source"], "inconnu")

    def test_libelle_manuel_survit_a_une_fiche_open_food_facts(self):
        application, base = application_de_test({"3017620422003": FICHE_NUTELLA})
        base.nommer("3017620422003", "Le pot de Paul")
        vue = application.scan({"code": "3017620422003", "forcer": "1"})
        self.assertEqual(vue["nom"], "Le pot de Paul")
        self.assertEqual(vue["nutriscore"], "e")       # les indicateurs, eux, arrivent


def tearDownModule():
    fermer_tout()


if __name__ == "__main__":
    unittest.main()
