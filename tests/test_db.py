"""Fusion des lots, sorties FEFO, duree habituelle."""

import datetime as dt
import tempfile
import unittest
from pathlib import Path

from tests.commun import RACINE  # noqa: F401

from inventaire.db import Base, jours_restants


class TestBase(unittest.TestCase):

    def setUp(self):
        self.base = Base(Path(tempfile.mkdtemp(prefix="frigo-db-")) / "t.db")
        self.addCleanup(self.base.fermer)
        self.demain = (dt.date.today() + dt.timedelta(days=1)).isoformat()
        self.semaine = (dt.date.today() + dt.timedelta(days=7)).isoformat()

    def test_ajouts_de_meme_date_fusionnent(self):
        self.base.ajouter("111", 2, self.semaine)
        self.base.ajouter("111", 3, self.semaine)
        self.assertEqual(self.base.stock("111"), 5)
        self.assertEqual(len(self.base.lots("111")), 1)

    def test_dates_differentes_font_des_lots_distincts(self):
        self.base.ajouter("111", 1, self.demain)
        self.base.ajouter("111", 1, self.semaine)
        self.base.ajouter("111", 1, None)
        self.assertEqual(len(self.base.lots("111")), 3)

    def test_lots_tries_par_urgence_sans_date_en_dernier(self):
        self.base.ajouter("111", 1, None)
        self.base.ajouter("111", 1, self.semaine)
        self.base.ajouter("111", 1, self.demain)
        dates = [lot["peremption"] for lot in self.base.lots("111")]
        self.assertEqual(dates, [self.demain, self.semaine, None])

    def test_retrait_sort_le_plus_urgent(self):
        self.base.ajouter("111", 1, self.semaine)
        self.base.ajouter("111", 1, self.demain)
        self.base.retirer("111", 1)
        restants = [lot["peremption"] for lot in self.base.lots("111")]
        self.assertEqual(restants, [self.semaine])

    def test_retrait_traverse_plusieurs_lots(self):
        self.base.ajouter("111", 2, self.demain)
        self.base.ajouter("111", 2, self.semaine)
        retires, restant = self.base.retirer("111", 3)
        self.assertEqual((retires, restant), (3, 1))
        self.assertEqual([l["peremption"] for l in self.base.lots("111")], [self.semaine])

    def test_retrait_plus_grand_que_le_stock_prend_ce_qu_il_y_a(self):
        self.base.ajouter("111", 2, self.demain)
        self.assertEqual(self.base.retirer("111", 9), (2, 0))

    def test_retrait_cible_un_lot(self):
        self.base.ajouter("111", 1, self.demain)
        self.base.ajouter("111", 1, self.semaine)
        vise = [l for l in self.base.lots("111") if l["peremption"] == self.semaine][0]
        self.base.retirer("111", 1, lot_id=vise["id"])
        self.assertEqual([l["peremption"] for l in self.base.lots("111")], [self.demain])

    def test_lot_vide_disparait(self):
        self.base.ajouter("111", 1, self.demain)
        self.base.retirer("111", 1)
        self.assertEqual(self.base.lots("111"), [])

    def test_correction_vers_une_date_existante_absorbe_le_jumeau(self):
        self.base.ajouter("111", 2, self.demain)
        self.base.ajouter("111", 3, self.semaine)
        lot = [l for l in self.base.lots("111") if l["peremption"] == self.demain][0]
        self.base.corriger_lot(lot["id"], peremption=self.semaine)
        lots = self.base.lots("111")
        self.assertEqual(len(lots), 1)
        self.assertEqual(lots[0]["qte"], 5)

    def test_correction_a_zero_supprime(self):
        self.base.ajouter("111", 2, self.demain)
        lot = self.base.lots("111")[0]
        self.base.corriger_lot(lot["id"], qte=0)
        self.assertEqual(self.base.stock("111"), 0)

    def test_duree_habituelle_est_la_mediane(self):
        for jours in (5, 7, 30):
            date = (dt.date.today() + dt.timedelta(days=jours)).isoformat()
            self.base.ajouter("222", 1, date)
            self.base.retirer("222", 1)
        self.assertEqual(self.base.duree_habituelle("222"), 7)

    def test_duree_habituelle_absente_sans_historique(self):
        self.assertIsNone(self.base.duree_habituelle("999"))

    def test_libelle_manuel_remplace_le_code(self):
        self.base.nommer("333", "Restes de gratin")
        self.assertEqual(self.base.libelle("333"), "Restes de gratin")
        self.assertEqual(self.base.libelle("444"), "444")

    def test_compteurs(self):
        self.base.ajouter("111", 2, self.demain)
        self.base.ajouter("222", 1, None)
        c = self.base.compteurs()
        self.assertEqual((c["unites"], c["references"], c["lots"]), (3, 2, 2))

    def test_perimes_comptes_a_part(self):
        hier = (dt.date.today() - dt.timedelta(days=1)).isoformat()
        self.base.ajouter("111", 2, hier)
        self.assertEqual(self.base.compteurs()["perimes"], 2)

    def test_jours_restants(self):
        self.assertIsNone(jours_restants(None))
        self.assertIsNone(jours_restants("pas-une-date"))
        self.assertEqual(jours_restants(dt.date.today().isoformat()), 0)
        self.assertEqual(jours_restants(self.demain), 1)

    def test_lots_negatifs_herites_sont_effaces_a_l_ouverture(self):
        # Le schema neuf interdit une quantite negative par contrainte ; une
        # base creee par une version anterieure, elle, a pu en accumuler.
        # On en fabrique une, sans contrainte, pour verifier le nettoyage.
        import sqlite3
        chemin = Path(tempfile.mkdtemp(prefix="frigo-vieux-")) / "v1.db"
        cx = sqlite3.connect(chemin)
        cx.executescript("""
            CREATE TABLE lot (id INTEGER PRIMARY KEY AUTOINCREMENT,
              code TEXT NOT NULL, qte INTEGER NOT NULL, peremption TEXT,
              ajoute_le TEXT NOT NULL, note TEXT);
            CREATE UNIQUE INDEX lot_fusion ON lot(code, IFNULL(peremption, ''));
        """)
        cx.execute("INSERT INTO lot (code, qte, peremption, ajoute_le) "
                   "VALUES ('111', -3, NULL, '2026-01-01')")
        cx.execute("INSERT INTO lot (code, qte, peremption, ajoute_le) "
                   "VALUES ('222', 4, NULL, '2026-01-01')")
        cx.commit()
        cx.close()

        rouverte = Base(chemin)
        self.addCleanup(rouverte.fermer)
        self.assertEqual(rouverte.stock("111"), 0)
        self.assertEqual(rouverte.lots("111"), [])
        # Le reste du stock est intact : on ne nettoie que ce qui est aberrant.
        self.assertEqual(rouverte.stock("222"), 4)

    def test_precision_au_mois_conservee(self):
        from inventaire.db import fin_de_mois
        self.base.ajouter("111", 1, fin_de_mois(2026, 10), precision="mois")
        self.assertEqual(self.base.lots("111")[0]["precision"], "mois")
        self.assertEqual(fin_de_mois(2026, 2), "2026-02-28")
        self.assertEqual(fin_de_mois(2028, 2), "2028-02-29")

    def test_quantite_nulle_refusee(self):
        self.assertRaises(ValueError, self.base.ajouter, "111", 0)
        self.assertRaises(ValueError, self.base.retirer, "111", -1)


if __name__ == "__main__":
    unittest.main()
