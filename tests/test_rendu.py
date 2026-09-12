"""Le format `cle=valeur` est le contrat avec le terminal : il se teste."""

import unittest

from tests.commun import RACINE  # noqa: F401  (pose sys.path)

from inventaire.rendu import aplatir, en_kv, en_json


class TestRendu(unittest.TestCase):

    def lignes(self, objet):
        return en_kv(objet).decode("utf-8").splitlines()

    def test_scalaires(self):
        self.assertEqual(self.lignes({"a": 1, "b": "x"}), ["a=1", "b=x"])

    def test_booleen_devient_un_chiffre(self):
        # Le C# lit `ok` avec B() qui teste l'egalite a "1" : true/false
        # casserait tout silencieusement.
        self.assertEqual(self.lignes({"ok": True, "ko": False}), ["ok=1", "ko=0"])

    def test_none_devient_vide(self):
        self.assertEqual(self.lignes({"jours": None}), ["jours="])

    def test_flottant_sans_bruit_binaire(self):
        self.assertEqual(self.lignes({"x": 0.1 + 0.2}), ["x=0.3"])
        self.assertEqual(self.lignes({"x": 539.0}), ["x=539"])

    def test_liste_annonce_sa_longueur_puis_s_indexe(self):
        sortie = self.lignes({"lots": [{"qte": 2}, {"qte": 1}]})
        self.assertEqual(sortie, ["lots=2", "lot.0.qte=2", "lot.1.qte=1"])

    def test_liste_vide(self):
        self.assertEqual(self.lignes({"lots": []}), ["lots=0"])

    def test_imbrication(self):
        self.assertEqual(self.lignes({"niveaux": {"sel": "low"}}), ["niveaux.sel=low"])

    def test_saut_de_ligne_echappe(self):
        # Une valeur multiligne couperait l'analyse du terminal en deux.
        sortie = self.lignes({"nom": "a\nb"})
        self.assertEqual(sortie, ["nom=a\\nb"])

    def test_antislash_echappe(self):
        self.assertEqual(self.lignes({"n": "c:\\x"}), ["n=c:\\\\x"])

    def test_valeur_avec_egal_reste_sur_une_ligne(self):
        # Le C# coupe au premier '=' : le reste doit survivre tel quel.
        sortie = self.lignes({"n": "a=b=c"})
        self.assertEqual(sortie, ["n=a=b=c"])

    def test_json_et_kv_decrivent_le_meme_objet(self):
        import json
        objet = {"ok": True, "lots": [{"id": 1}], "n": None}
        plat = dict(aplatir(objet))
        recharge = json.loads(en_json(objet))
        self.assertEqual(recharge["lots"][0]["id"], 1)
        self.assertEqual(plat["lot.0.id"], "1")


if __name__ == "__main__":
    unittest.main()
