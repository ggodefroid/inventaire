"""Cles de controle EAN / UPC, et reparation d'un code ampute.

Un decodeur peut etre configure pour ne pas transmettre la cle de controle.
Le code part alors ampute de son dernier chiffre, sans erreur ni trace, et
aucun catalogue ne le reconnait. Ces tests fixent la regle qui rattrape le cas.
"""

import unittest

from tests.commun import RACINE  # noqa: F401

from inventaire.codebarres import cle, reparer, valide, variantes


class TestCle(unittest.TestCase):

    def test_ean13(self):
        # Reference de la norme GS1, puis le cas observe sur le terminal.
        self.assertEqual(cle("400638133393"), "1")
        self.assertEqual(cle("301762042200"), "3")      # Nutella
        self.assertEqual(cle("000000000000"), "0")

    def test_ean8(self):
        self.assertEqual(cle("9638507"), "4")           # reference GS1

    def test_upca(self):
        self.assertEqual(cle("01234567890"), "5")

    def test_cle_est_celle_du_code_reel(self):
        # Verification croisee : recalculer la cle d'un code complet connu
        # doit redonner son dernier chiffre.
        for code in ("3017620422003", "4006381333931", "96385074",
                     "012345678905", "3229820129488"):
            self.assertEqual(cle(code[:-1]), code[-1], code)

    def test_valide(self):
        self.assertTrue(valide("3017620422003"))
        self.assertTrue(valide("012345678905"))
        self.assertFalse(valide("3017620422004"))
        self.assertFalse(valide(""))
        self.assertFalse(valide("7"))
        self.assertFalse(valide("abc"))


class TestReparation(unittest.TestCase):

    def test_ean13_ampute_est_complete(self):
        # Le cas observe sur le terminal : douze chiffres qui ne forment pas
        # un UPC-A valide, donc un EAN-13 prive de sa cle.
        self.assertEqual(reparer("301762042200"), "3017620422003")

    def test_upca_valide_reste_intact(self):
        # Douze chiffres qui forment un UPC-A valide en sont un : y ajouter une
        # cle inventerait un produit qui n'existe pas.
        self.assertEqual(reparer("012345678905"), "012345678905")

    def test_code_complet_reste_intact(self):
        self.assertEqual(reparer("3017620422003"), "3017620422003")

    def test_longueurs_non_concernees(self):
        # Sept chiffres : un EAN-8 ampute et un code interne sont
        # indiscernables. On ne devine pas.
        for code in ("2012345", "20123456", "1234", "20000000000123"):
            self.assertEqual(reparer(code), code)

    def test_code_non_numerique_reste_intact(self):
        self.assertEqual(reparer("ABC123456789"), "ABC123456789")

    def test_reparation_idempotente(self):
        # Rejouer la reparation sur son propre resultat ne doit rien ajouter :
        # le terminal renvoie le code corrige a l'ajout et au retrait.
        une_fois = reparer("301762042200")
        self.assertEqual(reparer(une_fois), une_fois)


class TestVariantes(unittest.TestCase):

    def test_upca_valide_propose_ses_trois_lectures(self):
        # Un UPC-A authentique d'abord, son equivalent EAN-13 a zero initial
        # ensuite -- c'est sous cette forme qu'Open Food Facts le range -- et
        # en dernier la lecture « EAN-13 ampute », qui reste possible.
        self.assertEqual(variantes("012345678905"),
                         ["012345678905", "0012345678905", "0123456789050"])

    def test_troncature_certaine_passe_en_tete(self):
        # Douze chiffres qui ne forment pas un UPC-A valide : la completion
        # n'est plus une hypothese, c'est la seule lecture possible.
        self.assertEqual(variantes("301762042200")[0], "3017620422003")

    def test_troncature_constatee_reordonne_les_lectures(self):
        # Cas reel : 359671035508 est un UPC-A arithmetiquement valide, et
        # pourtant c'est 3596710355082 ampute. Quand le serveur a deja vu ce
        # lecteur retenir des cles, on essaie la completion en premier.
        self.assertEqual(variantes("359671035508")[0], "359671035508")
        self.assertEqual(variantes("359671035508", True)[0], "3596710355082")

    def test_ean13_a_zero_initial_essaie_sa_forme_upca(self):
        self.assertEqual(variantes("0012345678905"),
                         ["0012345678905", "012345678905"])

    def test_ean8_et_upca_amputes(self):
        self.assertEqual(variantes("2012345")[0], "20123451")
        self.assertEqual(variantes("35967103550")[0], "359671035508")

    def test_cas_ordinaire_une_seule_forme(self):
        self.assertEqual(variantes("3017620422003"), ["3017620422003"])
        self.assertEqual(variantes("ABC"), ["ABC"])


class TestBoutEnBout(unittest.TestCase):
    """Un code ampute et le code complet doivent designer le meme stock."""

    def test_meme_cle_de_stock(self):
        from tests.commun import FICHE_NUTELLA, application_de_test
        app, base = application_de_test({"3017620422003": FICHE_NUTELLA})

        vue = app.ajouter({"code": "301762042200", "qte": "2"})
        self.assertEqual(vue["code"], "3017620422003")
        self.assertEqual(vue["code_lu"], "301762042200")
        self.assertEqual(vue["stock"], 2)

        # Le meme produit bippe par un lecteur correctement regle.
        self.assertEqual(app.scan({"code": "3017620422003"})["stock"], 2)
        # Et le retrait retrouve bien le stock, quelle que soit la forme lue.
        self.assertEqual(app.retirer({"code": "301762042200"})["stock"], 1)

    def test_troncature_ambigue_tranchee_par_le_catalogue(self):
        """Cas reel : 359671035508 passe pour un UPC-A valide, et n'en est pas.

        L'arithmetique ne peut pas trancher. Seul le catalogue le peut : la
        completion y figure, la lecture UPC-A non.
        """
        from tests.commun import application_de_test
        jus = {"code": "3596710355082", "product_name": "Pur jus d'orange",
               "brands": "Marque"}
        app, base = application_de_test({"3596710355082": jus})

        vue = app.scan({"code": "359671035508"})
        self.assertEqual(vue["code"], "3596710355082")
        self.assertEqual(vue["code_lu"], "359671035508")
        self.assertEqual(vue["nom"], "Pur jus d'orange")

    def test_la_correspondance_est_retenue(self):
        from tests.commun import application_de_test
        from inventaire import off
        jus = {"code": "3596710355082", "product_name": "Pur jus d'orange"}
        app, base = application_de_test({"3596710355082": jus})
        app.scan({"code": "359671035508"})
        self.assertEqual(base.alias("359671035508"), "3596710355082")

        # Seconde lecture : plus aucune interrogation du catalogue.
        appels = []
        vrai = off.interroger

        def compter(code, delai=8.0, agent=""):
            appels.append(code)
            return vrai(code, delai=delai, agent=agent)

        off.interroger = compter
        try:
            vue = app.scan({"code": "359671035508"})
        finally:
            off.interroger = vrai
        self.assertEqual(appels, [])
        self.assertEqual(vue["code"], "3596710355082")

    def test_deux_troncatures_constatees_reordonnent_les_essais(self):
        from tests.commun import FICHE_NUTELLA, application_de_test
        from inventaire import off
        jus = {"code": "3596710355082", "product_name": "Pur jus d'orange"}
        app, base = application_de_test({"3017620422003": FICHE_NUTELLA,
                                         "3596710355082": jus})
        app.scan({"code": "301762042200"})      # troncature certaine
        app.scan({"code": "359671035508"})      # troncature ambigue
        self.assertGreaterEqual(base.troncatures(), 2)

        # Un troisieme code ambigu doit desormais partir sur la completion.
        appels = []
        vrai = off.interroger

        def compter(code, delai=8.0, agent=""):
            appels.append(code)
            return vrai(code, delai=delai, agent=agent)

        off.interroger = compter
        try:
            app.scan({"code": "123456789012"})
        finally:
            off.interroger = vrai
        self.assertEqual(appels[0], "1234567890128")


if __name__ == "__main__":
    unittest.main()
