"""Le serveur MCP : enveloppe JSON-RPC, outils, ressources, invites.

Le protocole est un contrat avec un logiciel qu'on ne controle pas. Ce qui est
verifie ici, c'est la forme : un client MCP quelconque doit pouvoir negocier,
lister et appeler sans rien savoir de ce projet.
"""

from __future__ import annotations

import datetime as dt
import unittest
from pathlib import Path

from tests.commun import fermer_tout  # noqa: F401

from tests.test_vitrine import base_garnie

try:
    import flask  # noqa: F401
    FLASK = True
except ImportError:                                  # pragma: no cover
    FLASK = False


def _jour(delta: int) -> str:
    return (dt.date.today() + dt.timedelta(days=delta)).isoformat()


@unittest.skipUnless(FLASK, "flask absent")
class TestProtocole(unittest.TestCase):

    def setUp(self):
        from inventaire.mcp import creer_app
        self.base, self.chemin = base_garnie()
        # Un serveur d'ecriture volontairement injoignable : rien dans ces
        # tests ne doit dependre d'un second processus.
        self.app = creer_app(db=self.chemin, serveur="http://127.0.0.1:1")
        self.client = self.app.test_client()

    def tearDown(self):
        self.app.extensions["frigo"]["lecture"].fermer()

    # ------------------------------------------------------------- plomberie

    def appeler(self, methode: str, params: dict | None = None, identifiant: int = 1):
        corps = {"jsonrpc": "2.0", "id": identifiant, "method": methode}
        if params is not None:
            corps["params"] = params
        return self.client.post("/mcp", json=corps).get_json()

    def outil(self, nom: str, **arguments) -> str:
        reponse = self.appeler("tools/call", {"name": nom, "arguments": arguments})
        self.assertNotIn("error", reponse, reponse)
        return reponse["result"]["content"][0]["text"]

    # ------------------------------------------------------------ negociation

    def test_initialize_annonce_le_protocole_et_les_capacites(self):
        from inventaire.mcp import PROTOCOLE
        resultat = self.appeler("initialize", {
            "protocolVersion": PROTOCOLE, "capabilities": {},
            "clientInfo": {"name": "test", "version": "1"},
        })["result"]
        self.assertEqual(resultat["protocolVersion"], PROTOCOLE)
        self.assertEqual(resultat["serverInfo"]["name"], "inventaire-frigo")
        for capacite in ("tools", "resources", "prompts"):
            self.assertIn(capacite, resultat["capabilities"])
        self.assertIn("instructions", resultat)

    def test_une_version_de_client_differente_recoit_quand_meme_la_notre(self):
        from inventaire.mcp import PROTOCOLE
        resultat = self.appeler("initialize", {"protocolVersion": "1999-01-01"})["result"]
        self.assertEqual(resultat["protocolVersion"], PROTOCOLE)

    def test_une_notification_ne_repond_rien(self):
        reponse = self.client.post("/mcp", json={
            "jsonrpc": "2.0", "method": "notifications/initialized"})
        self.assertEqual(reponse.status_code, 202)
        self.assertEqual(reponse.get_data(), b"")

    def test_un_lot_rend_autant_de_reponses_que_de_demandes(self):
        reponses = self.client.post("/mcp", json=[
            {"jsonrpc": "2.0", "id": 1, "method": "ping"},
            {"jsonrpc": "2.0", "method": "notifications/initialized"},
            {"jsonrpc": "2.0", "id": 2, "method": "prompts/list"},
        ]).get_json()
        self.assertEqual([r["id"] for r in reponses], [1, 2])

    def test_en_tete_de_protocole_sur_chaque_reponse(self):
        from inventaire.mcp import PROTOCOLE
        reponse = self.client.post("/mcp", json={
            "jsonrpc": "2.0", "id": 1, "method": "ping"})
        self.assertEqual(reponse.headers["MCP-Protocol-Version"], PROTOCOLE)

    # ---------------------------------------------------------------- erreurs

    def test_methode_inconnue(self):
        self.assertEqual(self.appeler("n_existe_pas")["error"]["code"], -32601)

    def test_enveloppe_refusee_si_ce_n_est_pas_du_jsonrpc_2(self):
        reponse = self.client.post("/mcp", json={
            "jsonrpc": "1.0", "id": 1, "method": "ping"}).get_json()
        self.assertEqual(reponse["error"]["code"], -32600)

    def test_outil_inconnu(self):
        reponse = self.appeler("tools/call", {"name": "rm_rf", "arguments": {}})
        self.assertEqual(reponse["error"]["code"], -32602)

    def test_pas_de_flux_sse(self):
        self.assertEqual(self.client.get("/mcp").status_code, 405)

    # ----------------------------------------------------------------- outils

    def test_chaque_outil_est_declare_completement(self):
        outils = self.appeler("tools/list")["result"]["tools"]
        self.assertGreaterEqual(len(outils), 7)
        for o in outils:
            with self.subTest(outil=o.get("name")):
                for champ in ("name", "description", "inputSchema"):
                    self.assertIn(champ, o)
                schema = o["inputSchema"]
                self.assertEqual(schema["type"], "object")
                # Un schema ouvert laisse un modele inventer des parametres
                # qui seront ignores en silence.
                self.assertFalse(schema.get("additionalProperties", True))

    def test_chaque_outil_declare_est_appelable(self):
        for o in self.appeler("tools/list")["result"]["tools"]:
            with self.subTest(outil=o["name"]):
                arguments = {}
                for nom, champ in (o["inputSchema"].get("properties") or {}).items():
                    if nom in (o["inputSchema"].get("required") or []):
                        arguments[nom] = 1 if champ.get("type") == "integer" else "lait"
                reponse = self.appeler(
                    "tools/call", {"name": o["name"], "arguments": arguments})
                self.assertNotIn("error", reponse, reponse)
                self.assertIsInstance(
                    reponse["result"]["content"][0]["text"], str)

    def test_inventaire_rend_le_stock_en_clair(self):
        texte = self.outil("inventaire")
        self.assertIn("Nutella", texte)
        self.assertIn("references", texte)

    def test_bientot_perime_separe_le_perime_du_reste(self):
        texte = self.outil("bientot_perime", jours=7)
        self.assertIn("PERIME", texte)

    def test_chercher_ne_trouve_pas_l_inexistant(self):
        self.assertIn("Rien", self.outil("chercher", terme="tracteur"))

    def test_resume_porte_la_date_du_jour(self):
        self.assertIn(dt.date.today().isoformat(), self.outil("resume"))

    def test_l_ecriture_sans_serveur_est_un_message_pas_un_plantage(self):
        """Le seul outil qui ecrit relaie ; sans relais, il le dit."""
        texte = self.outil("ajouter_aux_courses", libelle="creme fraiche")
        self.assertIn("Impossible", texte)

    # ------------------------------------------------- ressources et invites

    def test_les_ressources_annoncees_sont_lisibles(self):
        for r in self.appeler("resources/list")["result"]["resources"]:
            with self.subTest(uri=r["uri"]):
                contenus = self.appeler(
                    "resources/read", {"uri": r["uri"]})["result"]["contents"]
                self.assertEqual(contenus[0]["uri"], r["uri"])
                self.assertTrue(contenus[0]["text"])

    def test_ressource_inconnue(self):
        reponse = self.appeler("resources/read", {"uri": "frigo://lune"})
        self.assertEqual(reponse["error"]["code"], -32602)

    def test_chaque_invite_produit_un_message_utilisateur(self):
        for p in self.appeler("prompts/list")["result"]["prompts"]:
            with self.subTest(invite=p["name"]):
                resultat = self.appeler("prompts/get", {"name": p["name"]})["result"]
                message = resultat["messages"][0]
                self.assertEqual(message["role"], "user")
                self.assertGreater(len(message["content"]["text"]), 40)
                # Aucun trou de gabarit ne doit rester.
                self.assertNotIn("{", message["content"]["text"])

    # ----------------------------------------------------------- lecture seule

    def test_la_base_reste_en_lecture_seule(self):
        import sqlite3
        lecture = self.app.extensions["frigo"]["lecture"]
        self.assertTrue(lecture.seulement_ro)
        with lecture.cx() as cx:
            with self.assertRaises(sqlite3.OperationalError):
                cx.execute("DELETE FROM lot")


if __name__ == "__main__":
    unittest.main()
