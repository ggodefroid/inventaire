"""Tests du serveur : base, ingestion, transports fichier."""

from __future__ import annotations

import datetime as dt
import logging
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from frigo import barcode as bc
from frigo.dates import ExpiryError, days_left, format_expiry, parse_expiry
from frigo.db import Store
from frigo.ingest import Session
from frigo.protocol import encode
from frigo.transports.fileimport import import_path, sniff_format

TODAY = dt.date(2026, 9, 7)


class BaseTest(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.store = Store(Path(self._tmp.name) / "test.db")

    def tearDown(self) -> None:
        self.store.close()
        self._tmp.cleanup()


class TestCodesBarres(unittest.TestCase):
    def test_upca_devient_ean13(self):
        self.assertEqual(bc.normalize("036000291452"), "0036000291452")

    def test_nettoyage_des_caracteres_du_wedge(self):
        self.assertEqual(bc.normalize("  3017620422003\r\n"), "3017620422003")

    def test_zeros_de_tete_parasites(self):
        self.assertEqual(bc.normalize("003017620422003"), "3017620422003")

    def test_cle_de_controle(self):
        self.assertTrue(bc.checksum_ok("3017620422003"))
        self.assertFalse(bc.checksum_ok("3017620422004"))

    def test_code_maison_non_gtin_conserve(self):
        self.assertEqual(bc.normalize("FRIGO-BAC1"), "FRIGO-BAC1")
        self.assertEqual(bc.describe("FRIGO-BAC1"), "alphanum-10")


class TestDates(unittest.TestCase):
    def test_formats_du_pave_numerique(self):
        cas = {
            "2510": dt.date(2026, 10, 25),
            "251026": dt.date(2026, 10, 25),
            "25102026": dt.date(2026, 10, 25),
            "+7": dt.date(2026, 9, 14),
            "10/2026": dt.date(2026, 10, 31),
            "2026-10-25": dt.date(2026, 10, 25),
        }
        for text, attendu in cas.items():
            self.assertEqual(parse_expiry(text, TODAY), attendu, text)

    def test_jjmm_passe_a_l_annee_suivante_si_deja_passe(self):
        # Le 1er mars 2026 est derriere nous : une DDM designe mars 2027.
        self.assertEqual(parse_expiry("0103", TODAY), dt.date(2027, 3, 1))

    def test_dates_impossibles_refusees(self):
        for text in ("31022026", "3202", "0013"):
            with self.assertRaises(ExpiryError, msg=text):
                parse_expiry(text, TODAY)

    def test_affichage_et_jours_restants(self):
        self.assertEqual(format_expiry("2026-10-25"), "25/10/26")
        self.assertEqual(format_expiry(None), "-")
        self.assertEqual(days_left("2026-09-10", TODAY), 3)
        self.assertEqual(days_left("2026-09-01", TODAY), -6)


class TestStockage(BaseTest):
    def test_rejeu_du_tampon_ne_cree_pas_de_doublon(self):
        self.assertEqual(self.store.add_item("3017620422003", device="S1", seq=1)[0], "ok")
        self.assertEqual(self.store.add_item("3017620422003", device="S1", seq=1)[0], "dup")
        self.assertEqual(len(self.store.inventory()), 1)

    def test_deux_terminaux_peuvent_utiliser_la_meme_sequence(self):
        self.assertEqual(self.store.add_item("111", device="S1", seq=1)[0], "ok")
        self.assertEqual(self.store.add_item("222", device="S2", seq=1)[0], "ok")
        self.assertEqual(len(self.store.inventory()), 2)

    def test_saisie_manuelle_sans_sequence_reste_possible(self):
        self.assertEqual(self.store.add_item("111", device="web")[0], "ok")
        self.assertEqual(self.store.add_item("111", device="web")[0], "ok")
        self.assertEqual(len(self.store.inventory()), 2)

    def test_consommation_sort_le_plus_urgent(self):
        self.store.add_item("111", expiry="2026-12-01", device="S1", seq=1)
        _, urgent = self.store.add_item("111", expiry="2026-09-10", device="S1", seq=2)
        self.assertEqual(self.store.consume_barcode("111"), urgent)

    def test_last_seq_indique_ou_reprendre(self):
        self.store.add_item("111", device="S1", seq=3)
        self.store.add_item("111", device="S1", seq=7)
        self.assertEqual(self.store.last_seq("S1"), 7)
        self.assertEqual(self.store.last_seq("inconnu"), 0)

    def test_bientot_exclut_ce_qui_est_trop_loin(self):
        self.store.add_item("111", expiry=(TODAY + dt.timedelta(days=2)).isoformat())
        self.store.add_item("222", expiry=(TODAY + dt.timedelta(days=40)).isoformat())
        codes = [r["barcode"] for r in self.store.expiring(5)]
        self.assertEqual(codes, ["111"])

    def test_libelle_tronque_pour_l_ecran_du_terminal(self):
        self.store.product_upsert("111", name="N" * 80, pack_size="400 g")
        self.assertLessEqual(len(self.store.product_label("111")), 40)


class TestIngestion(BaseTest):
    def _session(self) -> Session:
        return Session(self.store, channel="tcp", online=False)

    def test_sequence_complete(self):
        s = self._session()
        self.assertTrue(s.handle_line(encode("HELLO", "S1", "1", "0.1"))[0].startswith("READY"))
        self.assertTrue(s.handle_line(
            encode("SCAN", 1, "3017620422003", "251026", 1, "frigo", "", ""))[0].startswith("OK"))
        self.assertTrue(s.handle_line(
            encode("SCAN", 1, "3017620422003", "251026", 1, "frigo", "", ""))[0].startswith("DUP"))
        self.assertEqual(s.stats.ok, 1)
        self.assertEqual(s.stats.dup, 1)

    def test_hello_annonce_le_dernier_numero_connu(self):
        self.store.add_item("111", device="S1", seq=12)
        reply = self._session().handle_line(encode("HELLO", "S1", "1", "0.1"))[0]
        self.assertIn("|12|", reply)

    def test_protocole_incompatible_refuse(self):
        reply = self._session().handle_line(encode("HELLO", "S1", "99", "0.1"))[0]
        self.assertTrue(reply.startswith("ERR"))

    def test_lignes_invalides_rejetees_sans_casser_la_session(self):
        s = self._session()
        mauvaises = [
            encode("SCAN", 1, "", "2510", 1, "", "", ""),          # code vide
            encode("SCAN", 2, "111", "99/99/99", 1, "", "", ""),   # date impossible
            encode("SCAN", 3, "111", "", "abc", "", "", ""),       # quantite illisible
            encode("SCAN", 4, "111", "", 0, "", "", ""),           # quantite nulle
            encode("BLURP", "x"),                                  # verbe inconnu
            "SCAN|5|111|2510|1|frigo||*00",                        # checksum fausse
        ]
        for line in mauvaises:
            self.assertTrue(s.handle_line(line)[0].startswith("ERR"), line)
        # La session reste utilisable ensuite.
        self.assertTrue(s.handle_line(
            encode("SCAN", 9, "3017620422003", "", 1, "", "", ""))[0].startswith("OK"))
        self.assertEqual(s.stats.ok, 1)
        self.assertEqual(s.stats.err, 6)

    def test_scan_sans_date_est_accepte(self):
        s = self._session()
        self.assertTrue(s.handle_line(
            encode("SCAN", 1, "3017620422003", "", 1, "frigo", "", ""))[0].startswith("OK"))
        self.assertIsNone(self.store.inventory()[0]["expiry"])


class TestImportFichier(BaseTest):
    def _write(self, name: str, content: str) -> Path:
        path = Path(self._tmp.name) / name
        path.write_text(content, encoding="utf-8")
        return path

    def test_detection_du_format(self):
        self.assertEqual(sniff_format(encode("SCAN", 1, "111", "", 1, "", "", "")), "protocole")
        self.assertEqual(sniff_format("3017620422003;25/10/2026;1"), "csv")

    def test_import_protocole_idempotent(self):
        lines = "\n".join([
            encode("HELLO", "S1", "1", "0.1"),
            encode("SCAN", 1, "3017620422003", "251026", 1, "frigo", "", ""),
            encode("SCAN", 2, "3229820129488", "0110", 2, "frigo", "", ""),
        ])
        path = self._write("tampon.txt", lines)
        first = import_path(self.store, path, online=False)
        self.assertEqual(first.stats.ok, 2)
        second = import_path(self.store, path, online=False)
        self.assertEqual((second.stats.ok, second.stats.dup), (0, 2))
        self.assertEqual(len(self.store.inventory()), 2)

    def test_import_csv_a_la_main(self):
        path = self._write("liste.csv",
                           "code;peremption;qte;lieu\n"
                           "3017620422003;25/10/2026;1;frigo\n"
                           "3229820129488;+10;2;congelo\n")
        session = import_path(self.store, path, online=False)
        self.assertEqual(session.stats.ok, 2)
        lieux = sorted(r["location"] for r in self.store.inventory())
        self.assertEqual(lieux, ["congelo", "frigo"])

    def test_archivage_apres_import(self):
        path = self._write("t.txt", encode("SCAN", 1, "111", "", 1, "", "", ""))
        import_path(self.store, path, online=False, archive=True)
        self.assertFalse(path.exists())
        self.assertTrue(Path(str(path) + ".importe").exists())


# Les tests negatifs journalisent volontairement des rejets : on les tait.
logging.disable(logging.WARNING)

if __name__ == "__main__":
    unittest.main(verbosity=2)
