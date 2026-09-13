"""Routes metier, puis la couche HTTP telle que la voit le terminal."""

import datetime as dt
import io
import threading
import unittest
import urllib.parse
import urllib.request

import tempfile
from pathlib import Path

from tests.commun import (fermer_tout, FICHE_NUTELLA,  # noqa: F401
                          RACINE, application_de_test)

from inventaire.api import Erreur, faire_serveur


class TestRoutes(unittest.TestCase):

    def setUp(self):
        self.app, self.base = application_de_test({"3017620422003": FICHE_NUTELLA})
        self.semaine = (dt.date.today() + dt.timedelta(days=7)).isoformat()

    # ------------------------------------------------------------- lecture

    def test_ping(self):
        vue = self.app.ping({})
        self.assertEqual(vue["ok"], 1)
        self.assertIn("compteurs", vue)
        self.assertEqual(vue["aujourdhui"], dt.date.today().isoformat())

    def test_scan_produit_connu(self):
        vue = self.app.scan({"code": "3017620422003"})
        self.assertEqual(vue["nom"], "Nutella")
        self.assertEqual(vue["connu"], 1)
        self.assertEqual(vue["niveaux"]["sucres"], "high")
        self.assertEqual(vue["stock"], 0)
        self.assertEqual(vue["lots"], [])

    def test_scan_produit_inconnu(self):
        vue = self.app.scan({"code": "0000000000017"})
        self.assertEqual(vue["connu"], 0)
        self.assertEqual(vue["nom"], "")
        self.assertEqual(vue["image"], 0)

    def test_scan_sans_code(self):
        self.assertRaises(Erreur, self.app.scan, {})
        self.assertRaises(Erreur, self.app.scan, {"code": "   "})

    def test_code_nettoye_de_ses_separateurs(self):
        # Certains lecteurs ajoutent un prefixe ou un separateur au flux.
        vue = self.app.scan({"code": " 3017-620 422003 "})
        self.assertEqual(vue["code"], "3017620422003")

    # -------------------------------------------------------------- ecriture

    def test_ajout_puis_retrait(self):
        vue = self.app.ajouter({"code": "3017620422003", "qte": "2",
                                "peremption": self.semaine})
        self.assertEqual(vue["stock"], 2)
        self.assertEqual(vue["jours"], 7)
        self.assertEqual(vue["message"], "+2 Nutella")

        vue = self.app.retirer({"code": "3017620422003"})
        self.assertEqual(vue["stock"], 1)
        self.assertEqual(vue["retires"], 1)

    def test_ajout_en_jours_relatifs(self):
        vue = self.app.ajouter({"code": "111", "jours": "10"})
        self.assertEqual(vue["jours"], 10)

    def test_ajout_sans_date(self):
        vue = self.app.ajouter({"code": "111"})
        self.assertEqual(vue["peremption"], "")
        self.assertIsNone(vue["jours"])

    def test_ajout_d_un_code_inconnu_reste_possible(self):
        # Ranger une course ne doit jamais dependre d'Open Food Facts.
        vue = self.app.ajouter({"code": "0000000000017", "qte": "1"})
        self.assertEqual(vue["stock"], 1)

    def test_retrait_a_vide_refuse(self):
        self.assertRaises(Erreur, self.app.retirer, {"code": "3017620422003"})

    def test_le_stock_ne_descend_jamais_sous_zero(self):
        self.app.ajouter({"code": "111", "qte": "2"})
        self.app.retirer({"code": "111", "qte": "5"})
        self.assertEqual(self.base.stock("111"), 0)
        # Et une fois a zero, le poste disparait : plus rien a retirer.
        self.assertRaises(Erreur, self.app.retirer, {"code": "111"})
        self.assertEqual(self.base.stock("111"), 0)

    def test_correction_a_quantite_negative_supprime_le_lot(self):
        self.app.ajouter({"code": "111", "qte": "3"})
        lot = self.app.scan({"code": "111"})["lots"][0]
        self.app.lot({"lot": str(lot["id"]), "qte": "-4", "code": "111"})
        self.assertEqual(self.base.stock("111"), 0)

    def test_date_mal_formee_refusee(self):
        self.assertRaises(Erreur, self.app.ajouter,
                          {"code": "111", "peremption": "31/02/2026"})
        self.assertRaises(Erreur, self.app.ajouter,
                          {"code": "111", "peremption": "2026-13"})

    def test_peremption_au_mois_seulement(self):
        # « A consommer de preference avant fin 10/2026 » : l'emballage ne
        # porte pas de jour, on n'en invente pas.
        vue = self.app.ajouter({"code": "111", "peremption": "2026-10"})
        self.assertEqual(vue["peremption"], "2026-10-31")
        self.assertEqual(vue["precision"], "mois")
        self.assertEqual(vue["lots"][0]["precision"], "mois")

    def test_peremption_au_jour_par_defaut(self):
        vue = self.app.ajouter({"code": "111", "peremption": "2026-10-05"})
        self.assertEqual(vue["precision"], "jour")

    def test_precision_dans_l_inventaire(self):
        self.app.ajouter({"code": "111", "peremption": "2026-10"})
        poste = self.app.inventaire({})["postes"][0]
        self.assertEqual(poste["precision"], "mois")

    def test_quantite_non_numerique_refusee(self):
        self.assertRaises(Erreur, self.app.ajouter, {"code": "111", "qte": "beaucoup"})

    def test_suggestion_apres_un_premier_passage(self):
        self.app.ajouter({"code": "111", "jours": "12"})
        self.app.retirer({"code": "111"})
        self.assertEqual(self.app.scan({"code": "111"})["suggestion"], 12)

    def test_nommer(self):
        vue = self.app.nommer({"code": "111", "nom": "Gratin de Paul"})
        self.assertEqual(vue["nom"], "Gratin de Paul")
        self.assertRaises(Erreur, self.app.nommer, {"code": "111", "nom": " "})

    def test_correction_de_lot(self):
        self.app.ajouter({"code": "111", "qte": "3", "peremption": self.semaine})
        lot = self.app.scan({"code": "111"})["lots"][0]
        vue = self.app.lot({"lot": str(lot["id"]), "qte": "1", "code": "111"})
        self.assertEqual(vue["stock"], 1)
        self.assertRaises(Erreur, self.app.lot, {"lot": "99999"})

    # ------------------------------------------------------------- listes

    def test_inventaire_trie_par_urgence(self):
        self.app.ajouter({"code": "222", "jours": "30"})
        self.app.ajouter({"code": "111", "jours": "2"})
        codes = [p["code"] for p in self.app.inventaire({})["postes"]]
        self.assertEqual(codes, ["111", "222"])

    def test_inventaire_pagine(self):
        for code in ("111", "222", "333"):
            self.app.ajouter({"code": code, "jours": "5"})
        vue = self.app.inventaire({"limite": "2", "depuis": "1"})
        self.assertEqual(vue["total"], 3)
        self.assertEqual(len(vue["postes"]), 2)

    def test_bientot_ne_prend_que_la_fenetre(self):
        self.app.ajouter({"code": "111", "jours": "2"})
        self.app.ajouter({"code": "222", "jours": "60"})
        lignes = self.app.bientot({"jours": "7"})["lignes"]
        self.assertEqual([l["code"] for l in lignes], ["111"])

    def test_bientot_inclut_les_perimes(self):
        self.app.ajouter({"code": "111", "jours": "-3"})
        lignes = self.app.bientot({"jours": "0"})["lignes"]
        self.assertEqual(lignes[0]["jours"], -3)

    # -------------------------------------------------------------- photo

    def test_photo_convertie_en_bmp(self):
        from PIL import Image
        source = io.BytesIO()
        Image.new("RGB", (200, 150), (10, 120, 200)).save(source, "JPEG")
        octets = source.getvalue()
        self.app.vignettes._original = lambda url: octets

        import struct
        self.app.scan({"code": "3017620422003"})

        # Par defaut : 8 bits palettise. Le tiers des octets du 24 bits, ce qui
        # sur une radio de 2005 fait la difference entre attendre et voir.
        corps, mime = self.app.image({"code": "3017620422003", "l": "80", "h": "80"})
        self.assertEqual(mime, "image/bmp")
        self.assertEqual(corps[:2], b"BM")
        self.assertEqual(struct.unpack_from("<ii", corps, 18), (80, 80))
        self.assertEqual(struct.unpack_from("<H", corps, 28)[0], 8)
        self.assertEqual(struct.unpack_from("<I", corps, 30)[0], 0)    # non compresse
        # Une palette occupe l'espace entre l'entete et les pixels. Sa taille
        # suit l'image : Pillow n'ecrit que les couleurs employees, et cette
        # vignette-ci est unie.
        debut_pixels = struct.unpack_from("<I", corps, 10)[0]
        palette = debut_pixels - 54
        self.assertEqual(palette % 4, 0)
        self.assertGreaterEqual(palette, 4)
        self.assertLessEqual(palette, 256 * 4)

        # Et le 24 bits reste servi a qui le demande : un binaire de terminal
        # anterieur a cette version continue d'afficher ses photos.
        vingt_quatre, _ = self.app.image(
            {"code": "3017620422003", "l": "80", "h": "80", "img": "bmp"})
        self.assertEqual(struct.unpack_from("<H", vingt_quatre, 28)[0], 24)
        self.assertGreater(len(vingt_quatre), len(corps) * 2)

    def test_photo_absente(self):
        self.app.scan({"code": "0000000000017"})
        self.assertIsNone(self.app.image({"code": "0000000000017"}))


class TestMiseAJour(unittest.TestCase):
    """La route qui dit au terminal s'il a la derniere version."""

    def setUp(self):
        import tempfile
        from pathlib import Path
        self.app, self.base = application_de_test({})
        self.dist = Path(tempfile.mkdtemp(prefix="frigo-dist-"))
        self.app.dist = self.dist

    def test_dist_vide(self):
        vue = self.app.maj({})
        self.assertEqual(vue["disponible"], 0)
        self.assertEqual(vue["version"], "")
        self.assertEqual(vue["taille"], 0)

    def test_binaire_et_empreinte(self):
        (self.dist / "version.txt").write_text("abc123def456\n", encoding="utf-8")
        (self.dist / "Inventaire.exe").write_bytes(b"MZ" + b"\0" * 4000)
        vue = self.app.maj({})
        self.assertEqual(vue["version"], "abc123def456")
        self.assertEqual(vue["taille"], 4002)
        self.assertEqual(vue["nom"], "Inventaire.exe")
        self.assertEqual(vue["disponible"], 1)

    def test_empreinte_sans_binaire_ne_compte_pas(self):
        # Annoncer une version sans le fichier ferait tomber le terminal sur
        # un 404 apres lui avoir promis une mise a jour.
        (self.dist / "version.txt").write_text("abc123def456", encoding="utf-8")
        self.assertEqual(self.app.maj({})["disponible"], 0)

    def test_le_binaire_est_servi(self):
        (self.dist / "Inventaire.exe").write_bytes(b"MZ" + b"\0" * 4000)
        corps, mime = self.app.livrable("Inventaire.exe")
        self.assertEqual(len(corps), 4002)
        self.assertEqual(mime, "application/octet-stream")

    def test_liste_blanche(self):
        (self.dist / "secret.txt").write_text("non", encoding="utf-8")
        self.assertIsNone(self.app.livrable("secret.txt"))
        self.assertIsNone(self.app.livrable("../serveur.py"))


class TestHttp(unittest.TestCase):
    """La couche HTTP telle que HttpWebRequest du terminal la rencontre."""

    @classmethod
    def setUpClass(cls):
        cls.app, cls.base = application_de_test({"3017620422003": FICHE_NUTELLA})
        cls.serveur = faire_serveur("127.0.0.1", 0, cls.app)
        cls.port = cls.serveur.server_address[1]
        cls.fil = threading.Thread(target=cls.serveur.serve_forever, daemon=True)
        cls.fil.start()

    @classmethod
    def tearDownClass(cls):
        cls.serveur.shutdown()
        cls.serveur.server_close()

    def url(self, chemin):
        return f"http://127.0.0.1:{self.port}{chemin}"

    def obtenir(self, chemin, donnees=None):
        corps = urllib.parse.urlencode(donnees).encode() if donnees else None
        with urllib.request.urlopen(self.url(chemin), corps, timeout=5) as reponse:
            return reponse.status, reponse.read(), dict(reponse.headers)

    def kv(self, texte):
        return dict(ligne.split("=", 1) for ligne in texte.decode().splitlines())

    def test_ping_en_kv(self):
        statut, corps, entetes = self.obtenir("/api/ping?fmt=kv")
        self.assertEqual(statut, 200)
        self.assertTrue(entetes["Content-Type"].startswith("text/plain"))
        self.assertEqual(self.kv(corps)["ok"], "1")

    def test_content_length_toujours_pose(self):
        # Sans Content-Length, HttpWebRequest du CF 2.0 attend la fermeture de
        # la connexion : chaque appel paierait une seconde de latence.
        _, corps, entetes = self.obtenir("/api/ping?fmt=kv")
        self.assertEqual(int(entetes["Content-Length"]), len(corps))

    def test_post_urlencode(self):
        statut, corps, _ = self.obtenir("/api/ajouter",
                                        {"code": "555", "qte": 2, "fmt": "kv"})
        self.assertEqual(statut, 200)
        self.assertEqual(self.kv(corps)["stock"], "2")

    def test_erreur_metier_reste_en_200(self):
        # Le terminal ne saurait pas distinguer un 4xx d'une panne de liaison :
        # une WebException remonte dans les deux cas.
        statut, corps, _ = self.obtenir("/api/retirer?code=999999&fmt=kv")
        self.assertEqual(statut, 200)
        donnees = self.kv(corps)
        self.assertEqual(donnees["ok"], "0")
        self.assertTrue(donnees["erreur"])

    def test_route_inconnue_en_404(self):
        with self.assertRaises(urllib.error.HTTPError) as piege:
            self.obtenir("/api/nawak")
        self.assertEqual(piege.exception.code, 404)

    def test_tableau_de_bord(self):
        statut, corps, entetes = self.obtenir("/")
        self.assertEqual(statut, 200)
        self.assertIn("text/html", entetes["Content-Type"])
        self.assertIn(b"<title>Inventaire</title>", corps)

    def test_json_par_defaut(self):
        import json
        _, corps, entetes = self.obtenir("/api/ping")
        self.assertIn("application/json", entetes["Content-Type"])
        self.assertEqual(json.loads(corps)["ok"], 1)

    def test_utf8_en_aller_retour(self):
        self.obtenir("/api/nommer", {"code": "777", "nom": "Crème brûlée", "fmt": "kv"})
        _, corps, _ = self.obtenir("/api/scan?code=777&fmt=kv")
        self.assertEqual(self.kv(corps)["nom"], "Crème brûlée")


def tearDownModule():
    fermer_tout()


if __name__ == "__main__":
    unittest.main()


class TestCacheVignettes(unittest.TestCase):
    """Le cache memoire doit servir la deuxieme demande sans retoucher au disque."""

    def setUp(self):
        from tests.commun import FICHE_NUTELLA
        self.app, self.base = application_de_test({"3017620422003": FICHE_NUTELLA})
        from PIL import Image
        source = io.BytesIO()
        Image.new("RGB", (200, 150), (10, 120, 200)).save(source, "JPEG")
        self.octets = source.getvalue()
        self.telechargements = 0

        def original(url):
            self.telechargements += 1
            return self.octets

        self.app.vignettes._original = original
        self.app.scan({"code": "3017620422003"})

    def test_seconde_demande_servie_par_la_memoire(self):
        memoire = self.app.vignettes.memoire
        self.app.image({"code": "3017620422003", "l": "28", "h": "28"})
        self.assertEqual(memoire.compteurs()["vignettes"], 1)
        touches = memoire.compteurs()["touches"]
        self.app.image({"code": "3017620422003", "l": "28", "h": "28"})
        self.assertEqual(memoire.compteurs()["touches"], touches + 1)
        # Et l'original n'a ete telecharge qu'une fois, quoi qu'il arrive.
        self.assertEqual(self.telechargements, 1)

    def test_chaque_taille_a_son_entree(self):
        memoire = self.app.vignettes.memoire
        self.app.image({"code": "3017620422003", "l": "28", "h": "28"})
        self.app.image({"code": "3017620422003", "l": "80", "h": "80"})
        self.assertEqual(memoire.compteurs()["vignettes"], 2)

    def test_une_seule_source_telechargee_malgre_le_prechauffage(self):
        """La regression a ne pas refaire.

        Le prechauffage lance le telechargement des le scan ; l'ecran reclame
        la meme photo une fraction de seconde plus tard. Sans verrou par URL,
        chacun ouvrait sa connexion et le meme fichier descendait plusieurs
        fois -- ce qui ralentissait exactement ce que le prechauffage devait
        accelerer.
        """
        import threading
        import time
        from inventaire.images import Vignettes

        dossier = Path(tempfile.mkdtemp(prefix="frigo-prechauffe-"))
        vignettes = Vignettes(dossier, agent="test/1.0", ouvriers=2)
        self.addCleanup(vignettes.arreter)

        appels = []
        verrou = threading.Lock()

        def telecharger_lentement(url, cache):
            with verrou:
                appels.append(url)
            time.sleep(0.25)                 # le temps qu'un autre fil demande
            cache.write_bytes(self.octets)
            return self.octets

        vignettes._telecharger = telecharger_lentement

        vignettes.prechauffer("3017620422003", "http://exemple/p.jpg")
        # L'ecran demande pendant que le prechauffage est encore en vol.
        corps, _ = vignettes.obtenir("3017620422003", "http://exemple/p.jpg",
                                     80, 80, "bmp8")
        for _ in range(40):
            if not vignettes._en_vol:
                break
            time.sleep(0.05)

        self.assertTrue(corps)
        self.assertEqual(len(appels), 1,
                         f"{len(appels)} telechargements de la meme photo")

    def test_deux_fils_fabriquent_la_meme_vignette_sans_se_marcher_dessus(self):
        """Le fichier provisoire doit etre propre a chaque ecrivain.

        Derive de la seule cible, il etait partage : deux fils ecrivaient le
        meme `.part`, le premier le renommait, et le `replace` du second
        echouait sur un fichier disparu. Trois processus partagent ce cache en
        production -- le serveur, le site et le serveur MCP.
        """
        import threading
        from inventaire.images import Vignettes

        dossier = Path(tempfile.mkdtemp(prefix="frigo-course-"))
        vignettes = Vignettes(dossier, agent="test/1.0", ouvriers=0)
        vignettes._telecharger = lambda url, cache: (cache.write_bytes(self.octets)
                                                     or self.octets)

        soucis = []
        barriere = threading.Barrier(6)

        def fabriquer():
            barriere.wait()
            for _ in range(8):
                try:
                    vignettes.memoire.vider()        # force le passage par le disque
                    vignettes.obtenir("3017620422003", "http://exemple/p.jpg",
                                      80, 80, "bmp8")
                except Exception as exc:
                    soucis.append(repr(exc))

        fils = [threading.Thread(target=fabriquer) for _ in range(6)]
        for f in fils:
            f.start()
        for f in fils:
            f.join(20)
        self.assertEqual(soucis, [])

    def test_le_prechauffage_couvre_les_tailles_que_le_client_demande(self):
        """Prechauffer une taille que personne ne reclame ne sert a rien."""
        import re
        from inventaire.images import TAILLES_TERMINAL

        sources = RACINE / "frontend" / "src"
        demandees = set()
        for fichier in sources.glob("Ecran*.cs"):
            for m in re.finditer(r"(?:const int (?:Taille|TailleVignette)) = (\d+)",
                                 fichier.read_text(encoding="utf-8")):
                demandees.add(int(m.group(1)))
        self.assertTrue(demandees, "aucune taille trouvee dans les sources C#")
        manquantes = demandees - set(TAILLES_TERMINAL)
        self.assertFalse(manquantes,
                         f"tailles demandees par le terminal mais jamais "
                         f"prechauffees : {sorted(manquantes)}")

    def test_eviction_du_plus_ancien(self):
        from inventaire.images import Memoire
        memoire = Memoire(octets_max=100)
        memoire.ecrire("a", b"x" * 60, "image/bmp")
        memoire.ecrire("b", b"y" * 60, "image/bmp")
        self.assertIsNone(memoire.lire("a"))
        self.assertIsNotNone(memoire.lire("b"))

    def test_entree_plus_grande_que_le_cache_est_ignoree(self):
        from inventaire.images import Memoire
        memoire = Memoire(octets_max=10)
        memoire.ecrire("a", b"x" * 50, "image/bmp")
        self.assertEqual(memoire.compteurs()["vignettes"], 0)

    def test_inventaire_annonce_les_photos_disponibles(self):
        self.app.ajouter({"code": "3017620422003", "qte": "1"})
        self.app.ajouter({"code": "0000000000017", "qte": "1"})
        postes = {p["code"]: p for p in self.app.inventaire({})["postes"]}
        self.assertEqual(postes["3017620422003"]["image"], 1)
        self.assertEqual(postes["0000000000017"]["image"], 0)
