"""Verifie que le client C# et le serveur Python parlent bien la meme langue."""

from __future__ import annotations

import datetime as dt
import logging
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from frigo import protocol
from frigo.dates import ExpiryError, parse_expiry
from tests.csharp_mirror import (
    CsFormatError, cs_checksum, cs_decode, cs_encode, cs_sanitize,
    cs_to_iso, cs_try_parse,
)

TODAY = dt.date(2026, 9, 7)

VECTORS = [
    ("SCAN", ["1", "3017620422003", "2026-10-25", "1", "frigo", "2026-09-07T18:04:11", ""]),
    ("SCAN", ["42", "0036000291452", "", "2", "congelo", "2026-09-07T09:00:00", "yaourts"]),
    ("HELLO", ["SKORPIO1", "1", "0.1.0"]),
    ("LOOK", ["3017620422003"]),
    ("BYE", ["17"]),
    ("PING", []),
    # Champs hostiles : separateur, etoile, caracteres de controle, accents.
    ("SCAN", ["3", "ABC|DEF", "2026-01-01", "1", "fri*go", "x\ty", "note\r\nsuite"]),
    ("INFO", ["3017620422003", "Pate a tartiner 400 g", "Ferrero"]),
]


class TestProtocoleIdentique(unittest.TestCase):
    def test_checksum_identique(self):
        for payload in ("SCAN|1|3017620422003", "", "PONG", "e accent : ete", "|||"):
            self.assertEqual(cs_checksum(payload), protocol.checksum(payload),
                             f"checksum divergente sur {payload!r}")

    def test_sanitize_identique(self):
        for value in ("ABC|DEF", "x*y", "a\tb\r\nc", "  espaces  ", "", "accents ete"):
            self.assertEqual(cs_sanitize(value), protocol.sanitize(value),
                             f"sanitize divergente sur {value!r}")

    def test_encodage_identique(self):
        for verb, args in VECTORS:
            self.assertEqual(cs_encode(verb, *args), protocol.encode(verb, *args),
                             f"encodage divergent sur {verb}")

    def test_le_serveur_decode_le_client(self):
        for verb, args in VECTORS:
            line = cs_encode(verb, *args)
            msg = protocol.decode(line)
            self.assertEqual(msg.verb, verb)
            self.assertEqual(msg.args, [cs_sanitize(a) for a in args])

    def test_le_client_decode_le_serveur(self):
        for verb, args in VECTORS:
            line = protocol.encode(verb, *args)
            fields = cs_decode(line)
            self.assertEqual(fields[0], verb)
            self.assertEqual(fields[1:], [protocol.sanitize(a) for a in args])

    def test_les_deux_rejettent_une_checksum_fausse(self):
        line = protocol.encode("SCAN", 1, "3017620422003", "2026-10-25", 1, "frigo", "", "")
        corrupted = line[:-2] + ("00" if not line.endswith("00") else "11")
        with self.assertRaises(protocol.ProtocolError):
            protocol.decode(corrupted)
        with self.assertRaises(CsFormatError):
            cs_decode(corrupted)

    def test_une_ligne_sans_checksum_reste_acceptee(self):
        # Utile pour un fichier de tampon retouche a la main sur le PC.
        line = protocol.encode("SCAN", 9, "3017620422003", "", 1, "frigo", "", "",
                               with_checksum=False)
        self.assertEqual(protocol.decode(line).verb, "SCAN")
        self.assertEqual(cs_decode(line)[0], "SCAN")


class TestDatesIdentiques(unittest.TestCase):
    """Le terminal envoie de l'ISO : la conversion locale doit donner la meme date."""

    ABSOLUES = ["2510", "251026", "25102026", "0103", "3112", "29022028", "0101"]
    RELATIVES = ["0", "1", "7", "30", "365"]

    def test_saisies_absolues(self):
        for text in self.ABSOLUES:
            cote_client = cs_try_parse(text, False, TODAY)
            cote_serveur = parse_expiry(text, TODAY)
            self.assertEqual(cote_client, cote_serveur, f"divergence sur {text!r}")

    def test_saisies_relatives(self):
        for text in self.RELATIVES:
            cote_client = cs_try_parse(text, True, TODAY)
            # Cote serveur, le mode relatif s'exprime avec un '+' explicite.
            cote_serveur = parse_expiry("+" + text, TODAY)
            self.assertEqual(cote_client, cote_serveur, f"divergence sur J+{text}")

    def test_iso_du_client_relu_par_le_serveur(self):
        """Chemin reel : le client convertit en ISO, le serveur relit l'ISO."""
        for text in self.ABSOLUES:
            iso = cs_to_iso(cs_try_parse(text, False, TODAY))
            self.assertEqual(parse_expiry(iso, TODAY), cs_try_parse(text, False, TODAY))

    def test_champ_vide_signifie_date_inconnue(self):
        self.assertIsNone(cs_try_parse("", False, TODAY))
        self.assertIsNone(parse_expiry("", TODAY))
        self.assertEqual(cs_to_iso(None), "")

    def test_les_deux_refusent_une_date_impossible(self):
        for text in ("3202", "31022026", "0013"):
            with self.assertRaises(CsFormatError, msg=f"client accepte {text}"):
                cs_try_parse(text, False, TODAY)
            with self.assertRaises(ExpiryError, msg=f"serveur accepte {text}"):
                parse_expiry(text, TODAY)

    def test_asymetrie_assumee_sur_le_format_mois_annee(self):
        """Le serveur accepte "10/2026" (fin de mois), le pave numerique non.

        C'est voulu : ce format vient d'une saisie au clavier sur le PC, jamais
        du terminal. On le verifie pour que la difference reste consciente.
        """
        self.assertEqual(parse_expiry("10/2026", TODAY), dt.date(2026, 10, 31))
        with self.assertRaises(CsFormatError):
            cs_try_parse("10/2026", False, TODAY)


# Les tests negatifs journalisent volontairement des rejets : on les tait.
logging.disable(logging.WARNING)

if __name__ == "__main__":
    unittest.main(verbosity=2)
