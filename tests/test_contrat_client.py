"""Le contrat entre le C# du terminal et le Python du serveur.

Le client est en C#, le serveur en Python, et rien ne les relie a la
compilation. Renommer `contenance` en `quantite` cote serveur laisserait le
terminal afficher une chaine vide : pas d'erreur, pas de journal, juste une
information qui disparait de l'ecran. C'est le mode de panne le plus vicieux
de cette architecture.

Ce test lit les sources C#, en extrait toutes les cles reclamees au format
`cle=valeur`, et verifie que le serveur les emet bel et bien. Il ne remplace
pas un essai sur le terminal, mais il attrape la faute de frappe et l'oubli
de renommage, qui sont l'essentiel des cas.
"""

from __future__ import annotations

import datetime as dt
import re
import unittest
from pathlib import Path

from tests.commun import fermer_tout, FICHE_NUTELLA, RACINE, application_de_test

from inventaire.rendu import aplatir

SOURCES = RACINE / "frontend" / "src"

# kv.S("nom") · kv.I("nova", 0) · kv.B("ok") · kv.D("nutrition.kcal", -1)
# kv.Compte("lots") · reponse.Donnees.S("aujourdhui")
APPEL_LITTERAL = re.compile(r'\.(?:S|I|B|D|Compte)\(\s*"([^"]+)"')
# kv.S(p + "id"), ou p a ete pose juste au-dessus.
APPEL_PREFIXE = re.compile(r'\.(?:S|I|B|D|Compte)\(\s*(\w+)\s*\+\s*"([^"]+)"')
# string p = "lot." + i + ".";
POSE_PREFIXE = re.compile(r'string\s+(\w+)\s*=\s*"([^"]*)"\s*\+\s*\w+\s*\+\s*"([^"]*)"')


def cles_reclamees_par_le_client() -> dict[str, set[str]]:
    """Cle -> fichiers C# qui la lisent. Les index de liste sont ramenes a 0.

    La resolution des prefixes est positionnelle : un meme nom de variable
    sert dans plusieurs methodes d'un meme fichier (`p` vaut « poste.0. »
    dans l'une et « ligne.0. » dans l'autre), et prendre la derniere
    affectation du fichier attribuerait les cles a la mauvaise route.
    """
    reclamees: dict[str, set[str]] = {}
    for source in sorted(SOURCES.rglob("*.cs")):
        texte = source.read_text(encoding="utf-8")
        poses = [(m.start(), m.group(1), m.group(2) + "0" + m.group(3))
                 for m in POSE_PREFIXE.finditer(texte)]
        cles = set(APPEL_LITTERAL.findall(texte))
        for appel in APPEL_PREFIXE.finditer(texte):
            variable, suffixe = appel.group(1), appel.group(2)
            candidates = [valeur for position, nom, valeur in poses
                          if nom == variable and position < appel.start()]
            if candidates:
                cles.add(candidates[-1] + suffixe)
        for cle in cles:
            reclamees.setdefault(cle, set()).add(source.name)
    return reclamees


def cles_emises_par_le_serveur() -> set[str]:
    """Union des cles de toutes les reponses, sur un jeu couvrant chaque route."""
    application, base = application_de_test({"3017620422003": FICHE_NUTELLA})
    demain = (dt.date.today() + dt.timedelta(days=1)).isoformat()

    reponses = [
        application.ping({}),
        application.maj({}),
        application.ajouter({"code": "3017620422003", "qte": "2",
                             "peremption": demain}),
        application.ajouter({"code": "3017620422003", "qte": "1"}),
        application.retirer({"code": "3017620422003", "qte": "1"}),
        application.nommer({"code": "3017620422003", "nom": "Nutella"}),
        application.scan({"code": "3017620422003"}),
        application.detail({"code": "3017620422003"}),
        application.inventaire({}),
        application.bientot({"jours": "30"}),
        {"ok": 0, "erreur": "pour couvrir le chemin d'erreur",
         "aujourdhui": dt.date.today().isoformat()},
    ]
    emises: set[str] = set()
    for reponse in reponses:
        emises.update(cle for cle, _ in aplatir(reponse) if cle)
    return emises


class TestContrat(unittest.TestCase):

    def test_toute_cle_lue_par_le_terminal_est_emise_par_le_serveur(self):
        reclamees = cles_reclamees_par_le_client()
        self.assertGreater(len(reclamees), 25,
                           "l'extraction des cles C# n'a presque rien trouve : "
                           "les expressions regulieres ne collent plus aux sources")
        emises = cles_emises_par_le_serveur()
        manquantes = {cle: sorted(fichiers)
                      for cle, fichiers in reclamees.items() if cle not in emises}
        self.assertEqual(manquantes, {},
                         "cles lues par le client mais jamais emises par le serveur")

    def test_les_cles_structurantes_sont_bien_la(self):
        # Filet de securite au cas ou l'extraction ci-dessus deviendrait muette.
        emises = cles_emises_par_le_serveur()
        for cle in ("ok", "erreur", "aujourdhui", "code", "nom", "stock",
                    "lots", "lot.0.id", "lot.0.qte", "lot.0.peremption",
                    "lot.0.jours", "nutriscore", "nova", "niveaux.sucres",
                    "nutrition.kcal", "suggestion", "postes", "poste.0.code",
                    "lignes", "ligne.0.code", "ligne.0.id", "compteurs.unites",
                    "version", "heure", "portion", "nutrition.kj", "additifs",
                    "taille", "disponible", "nom",
                    "labels", "traces", "ingredients", "categories", "origine"):
            self.assertIn(cle, emises)

    def test_aucune_cle_emise_ne_contient_de_separateur_parasite(self):
        # Le C# coupe au premier '=' : une cle qui en contient casserait
        # l'analyse. Un saut de ligne dans une cle, de meme.
        for cle in cles_emises_par_le_serveur():
            self.assertNotIn("=", cle)
            self.assertNotIn("\n", cle)
            self.assertTrue(cle.strip() == cle)


def tearDownModule():
    fermer_tout()


if __name__ == "__main__":
    unittest.main()
