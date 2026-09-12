"""Le site public : lecture seule, mesures, routes, temps reel.

Les dependances du site (flask, flask-sock) ne sont pas celles du serveur du
terminal. Si elles manquent, les tests qui les reclament sont sautes plutot
qu'en echec : le serveur doit rester testable sur une machine ou seul Python
est installe.
"""

from __future__ import annotations

import datetime as dt
import json
import sqlite3
import tempfile
import time
import unittest
from pathlib import Path

from tests.commun import fermer_tout  # noqa: F401

from inventaire.db import Base
from inventaire.vitrine import mesures
from inventaire.vitrine.lecture import Lecture

try:
    import flask  # noqa: F401
    import flask_sock  # noqa: F401
    FLASK = True
except ImportError:                                  # pragma: no cover
    FLASK = False

AUJOURDHUI = dt.date.today()


def _jour(delta: int) -> str:
    return (AUJOURDHUI + dt.timedelta(days=delta)).isoformat()


def base_garnie() -> tuple[Base, Path]:
    """Un frigo de test : trois references, un perime, un sans date."""
    dossier = Path(tempfile.mkdtemp(prefix="frigo-vitrine-"))
    base = Base(dossier / "test.db")
    base.enregistrer_produit(
        "3017620422003", nom="Nutella", marque="Ferrero", quantite="400 g",
        nutriscore="e", nova=4, ecoscore="d", kcal=539.0, proteines=6.3,
        glucides=57.5, sucres=56.3, lipides=30.9, satures=10.6, sel=0.107,
        fibres=0.0, niv_graisses="high", niv_satures="high", niv_sucres="high",
        niv_sel="low", allergenes="lait, fruits a coque", additifs="E322",
        labels="sans gluten", categories="pates a tartiner",
        image_url="https://exemple.invalid/nutella.jpg", source="openfoodfacts")
    base.enregistrer_produit(
        "5449000000996", nom="Coca-Cola", marque="Coca-Cola", quantite="1,5 L",
        nutriscore="e", nova=4, kcal=42.0, sucres=10.6, glucides=10.6,
        source="openfoodfacts")
    base.enregistrer_produit("0000000000017", nom="Restes de gratin", source="manuel")

    base.ajouter("3017620422003", 2, _jour(18), origine="terminal")
    base.ajouter("3017620422003", 1, _jour(-4), origine="terminal")
    base.ajouter("5449000000996", 6, _jour(2), origine="terminal")
    base.ajouter("0000000000017", 1, None, origine="web")
    return base, dossier / "test.db"


class TestMasse(unittest.TestCase):
    """Le conditionnement est une chaine libre : on lit ce qui se lit."""

    def test_formes_courantes(self):
        for texte, attendu in [("400 g", 400.0), ("1 kg", 1000.0), ("75 cl", 750.0),
                               ("1,5 L", 1500.0), ("250ml", 250.0), ("500 mg", 0.5),
                               ("800 gram", 800.0), ("6 x 125 g", 750.0),
                               ("300 g e", 300.0)]:
            with self.subTest(texte=texte):
                self.assertEqual(mesures.masse_unitaire(texte)[0], attendu)

    def test_liquides_reconnus(self):
        self.assertTrue(mesures.masse_unitaire("1 L")[1])
        self.assertTrue(mesures.masse_unitaire("33 cl")[1])
        self.assertFalse(mesures.masse_unitaire("400 g")[1])

    def test_illisible_rend_rien(self):
        for texte in ("", "1 piece", "sachet", "environ", "0 g", "999 kg"):
            self.assertIsNone(mesures.masse_unitaire(texte)[0], texte)


class TestStatistiques(unittest.TestCase):

    def test_entropie_maximale_quand_tout_est_egal(self):
        bits, pielou = mesures._entropie([4, 4, 4, 4])
        self.assertAlmostEqual(bits, 2.0, places=6)
        self.assertAlmostEqual(pielou, 1.0, places=6)

    def test_entropie_nulle_sur_une_seule_reference(self):
        self.assertEqual(mesures._entropie([9]), (0.0, 0.0))

    def test_gini_borne(self):
        self.assertAlmostEqual(mesures._gini([5, 5, 5, 5]), 0.0, places=6)
        self.assertGreater(mesures._gini([1, 1, 1, 97]), 0.7)

    def test_charge_remonte_le_temps(self):
        """Le stock d'hier, c'est celui d'aujourd'hui moins ce qui a bouge."""
        hier = (AUJOURDHUI - dt.timedelta(days=1)).isoformat() + " 10:00:00"
        journal = [(hier, 5, "ajout"),
                   (AUJOURDHUI.isoformat() + " 09:00:00", -2, "retrait")]
        serie = mesures._charge(journal, unites=3, profondeur=3)
        self.assertEqual(serie[-1]["unites"], 3)          # aujourd'hui
        self.assertEqual(serie[-2]["unites"], 5)          # avant le retrait
        self.assertEqual(serie[-3]["unites"], 0)          # avant l'ajout


class TestLectureSeule(unittest.TestCase):

    def setUp(self):
        self.base, self.chemin = base_garnie()
        self.lecture = Lecture(self.chemin)

    def tearDown(self):
        self.lecture.fermer()
        self.base.fermer()

    def test_ecriture_refusee(self):
        with self.lecture.cx() as cx:
            for ordre in ("DELETE FROM lot",
                          "UPDATE lot SET qte = 99",
                          "INSERT INTO lot (code, qte, ajoute_le) VALUES ('x', 1, 'x')",
                          "DROP TABLE lot"):
                with self.subTest(ordre=ordre):
                    with self.assertRaises(sqlite3.Error):
                        cx.execute(ordre)

    def test_stock_intact_apres_tentative(self):
        with self.lecture.cx() as cx:
            try:
                cx.execute("DELETE FROM lot")
            except sqlite3.Error:
                pass
        self.assertEqual(self.base.stock("5449000000996"), 6)

    def test_version_donnees_bouge_sur_ecriture_externe(self):
        """C'est ce compteur qui declenche la diffusion temps reel."""
        avant = self.lecture.version_donnees()
        self.base.ajouter("5449000000996", 1, _jour(30))
        self.assertNotEqual(self.lecture.version_donnees(), avant)

    def test_postes_et_lots(self):
        postes = {p["code"]: p for p in self.lecture.postes()}
        self.assertEqual(len(postes), 3)
        nutella = postes["3017620422003"]
        self.assertEqual(nutella["total"], 3)
        self.assertEqual(nutella["nb_lots"], 2)
        self.assertEqual(nutella["jours"], -4)            # le lot le plus urgent
        self.assertEqual(nutella["allergenes"], "lait, fruits a coque")

    def test_article_complet(self):
        article = self.lecture.article("3017620422003")
        self.assertEqual(article["nom"], "Nutella")
        self.assertEqual(len(article["lots"]), 2)
        self.assertEqual(len(article["mouvements"]), 2)
        self.assertEqual(article["lots"][0]["jours"], -4)  # FEFO : le perime d'abord

    def test_article_inconnu(self):
        self.assertIsNone(self.lecture.article("1111111111116"))


class TestInstantane(unittest.TestCase):

    def setUp(self):
        self.base, self.chemin = base_garnie()
        self.lecture = Lecture(self.chemin)
        self.etat = mesures.instantane(self.lecture)

    def tearDown(self):
        self.lecture.fermer()
        self.base.fermer()

    def test_structure(self):
        for cle in ("horloge", "compteurs", "kpi", "chiffres", "curiosites",
                    "donuts", "barres", "series", "niveaux", "urgents",
                    "postes", "journal", "systeme"):
            self.assertIn(cle, self.etat)

    def test_serialisable(self):
        json.dumps(self.etat, ensure_ascii=False)

    def test_dix_kpi_numerotes_avec_formule(self):
        self.assertEqual(len(self.etat["kpi"]), 10)
        self.assertEqual([k["rang"] for k in self.etat["kpi"]],
                         [f"{i:02d}" for i in range(1, 11)])
        for kpi in self.etat["kpi"]:
            self.assertTrue(kpi["formule"], kpi["code"])
            self.assertTrue(kpi["lecture"], kpi["code"])
            self.assertIn(kpi["etat"], ("bon", "moyen", "mauvais"))

    def test_gaspillage_compte_le_perime(self):
        kpi = {k["code"]: k for k in self.etat["kpi"]}
        self.assertGreater(kpi["GASPILLAGE"]["valeur"], 0)
        self.assertEqual(self.etat["compteurs"]["perimes"], 1)

    def test_masse_et_energie(self):
        """3 Nutella de 400 g et 6 Coca de 1,5 L font 10,2 kg."""
        masse = next(c for c in self.etat["chiffres"] if c["titre"] == "Masse en stock")
        self.assertAlmostEqual(masse["valeur"], 10.2, places=1)
        energie = next(c for c in self.etat["chiffres"] if c["titre"] == "Énergie totale")
        self.assertEqual(energie["valeur"], int(539 * 12 + 42 * 90))

    def test_libelles_accentues(self):
        """Ces chaines partent dans un navigateur, pas sur l'ecran du terminal.

        Le navigateur affiche ce qu'on lui donne ; le terminal, lui, n'a pas
        forcement la police pour. Les deux jeux de libelles ne suivent donc
        pas la meme regle, et rien dans le code ne le rappelle a la lecture.
        """
        sans_accent = []
        for carte in self.etat["chiffres"] + self.etat["curiosites"]:
            for champ in ("titre", "note"):
                texte = carte[champ]
                for mot in ("unites", "references", "peremption", "perime",
                            "diversite", "cles", "libelles", "declares"):
                    if mot in texte.lower():
                        sans_accent.append(f"{carte['titre']} / {champ} : {texte}")
        for kpi in self.etat["kpi"]:
            for champ in ("titre", "formule", "lecture"):
                for mot in ("unites", "references", "peremption", "densite"):
                    if mot in kpi[champ].lower():
                        sans_accent.append(f"KPI {kpi['rang']} / {champ} : {kpi[champ]}")
        self.assertEqual(sans_accent, [])

    def test_echeances_nommees_comme_le_navigateur_les_attend(self):
        """Ces libelles servent aussi de cle de couleur cote page."""
        noms = {x["cle"] for x in self.etat["donuts"]["urgence"]}
        connus = {"périmé", "sous 72 h", "sous 7 j", "sous 15 j", "sous 30 j",
                  "au-delà", "sans date"}
        self.assertTrue(noms <= connus, noms - connus)

    def test_urgences_couvrent_tout_le_stock(self):
        total = sum(x["valeur"] for x in self.etat["donuts"]["urgence"])
        self.assertEqual(total, self.etat["compteurs"]["unites"])

    def test_series_dimensionnees(self):
        s = self.etat["series"]
        self.assertEqual(len(s["mouvements"]), 30)
        self.assertEqual(len(s["charge"]), 60)
        self.assertEqual(len(s["heures"]), 24)
        self.assertEqual(len(s["semaine"]), 7)
        self.assertEqual(len(s["grille"]), 7)
        self.assertTrue(all(len(ligne) == 24 for ligne in s["grille"]))

    def test_index_de_recherche(self):
        """Le champ agrege nom, marque, code et etiquettes : la recherche du
        navigateur ne fait qu'un `includes` dessus."""
        nutella = next(p for p in self.etat["postes"] if p["nom"] == "Nutella")
        for terme in ("nutella", "ferrero", "3017620422003", "pates a tartiner"):
            self.assertIn(terme, nutella["recherche"], terme)

    def test_curiosites_toutes_remplies(self):
        self.assertGreaterEqual(len(self.etat["curiosites"]), 20)
        for carte in self.etat["curiosites"]:
            self.assertTrue(carte["titre"])
            self.assertTrue(carte["note"])

    def test_frigo_vide_ne_leve_pas(self):
        with self.lecture.cx() as cx:
            pass
        self.base.retirer("3017620422003", 3)
        self.base.retirer("5449000000996", 6)
        self.base.retirer("0000000000017", 1)
        etat = mesures.instantane(self.lecture)
        self.assertEqual(etat["compteurs"]["unites"], 0)
        self.assertEqual(len(etat["kpi"]), 10)


@unittest.skipUnless(FLASK, "flask et flask-sock absents")
class TestRoutes(unittest.TestCase):

    @classmethod
    def setUpClass(cls):
        from inventaire.vitrine import creer_app
        cls.base, cls.chemin = base_garnie()
        cls.app = creer_app(db=cls.chemin,
                            cache=cls.chemin.parent / "cache")
        cls.client = cls.app.test_client()

    @classmethod
    def tearDownClass(cls):
        cls.app.extensions["frigo"]["veille"].arreter()
        cls.base.fermer()

    def test_page_daccueil(self):
        reponse = self.client.get("/")
        self.assertEqual(reponse.status_code, 200)
        corps = reponse.get_data(as_text=True)
        self.assertIn("vitrine.js", corps)
        self.assertIn("poste de contrôle", corps)

    def test_entetes_de_securite(self):
        entetes = self.client.get("/").headers
        csp = entetes["Content-Security-Policy"]
        self.assertIn("default-src 'self'", csp)
        self.assertEqual(entetes["X-Frame-Options"], "DENY")
        # Le script reste strict ; seul le style tolere l'inline, parce que
        # jauges et pastilles tirent leur taille et leur couleur des donnees.
        self.assertIn("script-src 'self'", csp)
        self.assertNotIn("script-src 'self' 'unsafe-inline'", csp)
        self.assertIn("style-src 'self' 'unsafe-inline'", csp)

    def test_etat_json(self):
        etat = self.client.get("/api/etat").get_json()
        self.assertEqual(len(etat["postes"]), 3)
        self.assertEqual(len(etat["kpi"]), 10)
        self.assertIn("revision", etat)

    def test_article(self):
        article = self.client.get("/api/article/3017620422003").get_json()
        self.assertEqual(article["ok"], 1)
        self.assertEqual(article["nom"], "Nutella")
        self.assertEqual(article["masse_unitaire"], 400.0)
        self.assertEqual(len(article["lots"]), 2)

    def test_article_inconnu_en_404(self):
        self.assertEqual(self.client.get("/api/article/1111111111116").status_code, 404)

    def test_code_nettoye(self):
        """Une route publique ne fait pas confiance a ce qu'on lui donne."""
        reponse = self.client.get("/api/article/3017-620/422003")
        self.assertEqual(reponse.status_code, 404)

    def test_sante(self):
        sante = self.client.get("/sante").get_json()
        self.assertTrue(sante["ok"])
        self.assertTrue(sante["lecture_seule"])

    def test_photo_de_remplacement(self):
        """Un code sans photo rend une silhouette, jamais une case vide."""
        reponse = self.client.get("/photo/0000000000017?c=96")
        self.assertEqual(reponse.status_code, 200)
        self.assertIn("svg", reponse.headers["Content-Type"])

    def test_aucune_ecriture_exposee(self):
        for regle in self.app.url_map.iter_rules():
            with self.subTest(regle=str(regle)):
                self.assertFalse({"POST", "PUT", "PATCH", "DELETE"} & regle.methods)

    def test_post_refuse(self):
        for route in ("/", "/api/etat", "/api/article/3017620422003"):
            self.assertEqual(self.client.post(route).status_code, 405, route)


@unittest.skipUnless(FLASK, "flask et flask-sock absents")
class TestVeille(unittest.TestCase):

    def setUp(self):
        from inventaire.vitrine.veille import Veille
        self.base, self.chemin = base_garnie()
        self.lecture = Lecture(self.chemin)
        self.veille = Veille(self.lecture, periode=0.1)

    def tearDown(self):
        self.veille.arreter()
        self.base.fermer()

    def test_revision_augmente_quand_la_base_change(self):
        avant = self.veille.revision
        self.base.ajouter("5449000000996", 3, _jour(40))
        self.veille.rafraichir()
        self.assertGreater(self.veille.revision, avant)

    def test_revision_stable_sans_changement(self):
        """L'horloge tourne, mais l'etat du frigo, lui, n'a pas bouge."""
        self.veille.rafraichir()
        avant = self.veille.revision
        time.sleep(1.05)
        self.veille.rafraichir()
        self.assertEqual(self.veille.revision, avant)

    def test_les_abonnes_recoivent_la_diffusion(self):
        fil = self.veille.abonner()
        self.veille.demarrer()
        self.base.ajouter("0000000000017", 1, _jour(9))
        fin = time.monotonic() + 8
        message = None
        while time.monotonic() < fin:           # le battement peut passer avant
            candidat = json.loads(fil.get(timeout=6))
            if candidat["type"] == "maj":
                message = candidat
                break
        self.veille.desabonner(fil)
        self.assertIsNotNone(message, "aucune mise a jour diffusee")
        self.assertEqual(message["compteurs"]["unites"], 11)

    def test_file_pleine_ne_bloque_pas(self):
        """Un client a la traine ne doit pas retenir les autres."""
        fil = self.veille.abonner()
        for _ in range(50):
            self.veille._diffuser('{"type":"maj"}')
        self.assertLessEqual(fil.qsize(), 8)
        self.veille.desabonner(fil)

    def test_pouls_porte_la_revision(self):
        pouls = json.loads(self.veille.pouls())
        self.assertEqual(pouls["type"], "pouls")
        self.assertEqual(pouls["revision"], self.veille.revision)
        self.assertIn("heure", pouls)


def tearDownModule():
    fermer_tout()


if __name__ == "__main__":
    unittest.main()
