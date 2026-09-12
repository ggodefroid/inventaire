using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Inventaire
{
    /// <summary>
    /// La fiche complete : tout ce qu'Open Food Facts sait du produit.
    ///
    /// L'ecran d'article ne montre que ce qui sert a decider en trois secondes
    /// devant une porte ouverte. Le reste -- table nutritionnelle detaillee,
    /// additifs, liste d'ingredients, origine -- vit ici, a une touche de
    /// distance, et se lit en defilant.
    ///
    /// Le contenu est monte en rubriques mesurees a l'avance, puis peint avec
    /// un decalage. Sur un PXA270, recalculer la mise en page a chaque cran de
    /// defilement serait perceptible.
    /// </summary>
    internal sealed class EcranDetail : Ecran
    {
        private sealed class Rubrique
        {
            public string Titre;        // non nul : intertitre de section
            public string Libelle;
            public string Valeur;
            public Color Couleur;
            public int Hauteur;
            public string[] Lignes;     // valeur repliee, si elle ne tient pas
        }

        private Article _article = new Article();
        private Detail _detail;
        private readonly List<Rubrique> _rubriques = new List<Rubrique>();
        private int _defilement;
        private int _total;
        private bool _mesure;
        private string _erreur = "";

        private const int HauteurBarre = 30;

        public EcranDetail(Fenetre fenetre) : base(fenetre) { }

        protected override bool RaccourcisActifs { get { return true; } }

        public override void Entrer(object argument)
        {
            Article article = argument as Article;
            if (article != null && (_detail == null || _detail.Code != article.Code))
            {
                _article = article;
                _detail = null;
                _erreur = "";
                _rubriques.Clear();
                Charger();
            }
            _defilement = 0;
            _mesure = false;
        }

        public override void Retour()
        {
            Fenetre.Aller(Fenetre.Article, _article);
        }

        private void Charger()
        {
            string code = _article.Code;
            Fenetre.Appeler("fiche", new FonctionReseau(delegate()
            {
                return Api.Detail(code);
            }), new SuiteReseau(delegate(Reponse reponse)
            {
                if (reponse.Joint && reponse.Ok)
                {
                    _detail = Detail.Depuis(reponse.Donnees);
                    _mesure = false;
                }
                else
                {
                    _erreur = reponse.Erreur.Length > 0 ? reponse.Erreur
                        : "fiche indisponible";
                }
                Invalidate();
            }));
        }

        // --------------------------------------------------------- clavier

        public override bool Touche(Keys touche)
        {
            if (ToucheBase(touche))
                return true;
            switch (touche)
            {
                case Keys.Escape:
                    Retour();
                    return true;
                case Keys.Up:
                    Defiler(-28);
                    return true;
                case Keys.Down:
                    Defiler(28);
                    return true;
                case Keys.Left:
                    Defiler(-Fenetre_Hauteur());
                    return true;
                case Keys.Right:
                    Defiler(Fenetre_Hauteur());
                    return true;
            }
            return false;
        }

        protected override void Raccourci(char c)
        {
            if (c == '1') Fenetre.Aller(Fenetre.Saisie, _article);
            else if (c == '2') Retour();
        }

        protected override void CodeLu(string code)
        {
            Fenetre.Appeler("lecture", new FonctionReseau(delegate()
            {
                return Api.Scan(code);
            }), new SuiteReseau(delegate(Reponse reponse)
            {
                if (reponse.Joint && reponse.Ok)
                    Fenetre.Aller(Fenetre.Article, Article.Depuis(reponse.Donnees));
                else
                    Fenetre.Dire(reponse.Erreur, false);
            }));
        }

        protected override void Activer(string id)
        {
            if (id == "haut") Defiler(-Fenetre_Hauteur());
            else if (id == "bas") Defiler(Fenetre_Hauteur());
            else if (id == "article") Retour();
        }

        private int Fenetre_Hauteur()
        {
            return Height - HauteurBandeau - HauteurBarre - 6;
        }

        private void Defiler(int delta)
        {
            int maximum = Math.Max(0, _total - Fenetre_Hauteur());
            int cible = _defilement + delta;
            if (cible < 0) cible = 0;
            if (cible > maximum) cible = maximum;
            if (cible != _defilement)
            {
                _defilement = cible;
                Sons.Jouer(Sons.Clic);
                Invalidate();
            }
        }

        // ---------------------------------------------------------- contenu

        private void Monter(Graphics g)
        {
            _rubriques.Clear();
            _total = 0;
            if (_detail == null)
                return;
            Detail d = _detail;
            int largeur = Width - 12;

            Titre("PRODUIT");
            Valeur(g, "Nom", d.Nom.Length > 0 ? d.Nom : d.Code, Theme.Texte, largeur);
            if (d.Marque.Length > 0) Valeur(g, "Marque", d.Marque, Theme.Texte, largeur);
            if (d.Contenance.Length > 0)
                Valeur(g, "Contenance", d.Contenance, Theme.Texte, largeur);
            if (d.Portion.Length > 0) Valeur(g, "Portion", d.Portion, Theme.Texte, largeur);
            if (d.Origine.Length > 0) Valeur(g, "Origine", d.Origine, Theme.Texte, largeur);
            if (d.Categories.Length > 0)
                Valeur(g, "Categories", d.Categories, Theme.Doux, largeur);

            Titre("NOTES");
            Valeur(g, "Nutri-Score", Note(d.Nutriscore), Theme.CouleurNutri(d.Nutriscore),
                   largeur);
            Valeur(g, "Groupe NOVA", d.Nova > 0 ? d.Nova + " · " + Nova(d.Nova) : "-",
                   Theme.CouleurNova(d.Nova), largeur);
            if (d.Ecoscore.Length > 0)
                Valeur(g, "Eco-Score", Note(d.Ecoscore), Theme.CouleurNutri(d.Ecoscore),
                       largeur);

            Titre("POUR 100 g / ml");
            Energie(g, d, largeur);
            Nutriment(g, "Matieres grasses", d.Lipides, d.NivGraisses, largeur);
            Nutriment(g, "  dont satures", d.Satures, d.NivSatures, largeur);
            Nutriment(g, "Glucides", d.Glucides, "", largeur);
            Nutriment(g, "  dont sucres", d.Sucres, d.NivSucres, largeur);
            Nutriment(g, "Fibres", d.Fibres, "", largeur);
            Nutriment(g, "Proteines", d.Proteines, "", largeur);
            Nutriment(g, "Sel", d.Sel, d.NivSel, largeur);

            if (d.Labels.Length > 0 || d.Allergenes.Length > 0 || d.Traces.Length > 0
                || d.Additifs.Length > 0)
            {
                Titre("COMPOSITION");
                if (d.Labels.Length > 0) Valeur(g, "Labels", d.Labels, Theme.Vert, largeur);
                if (d.Allergenes.Length > 0)
                    Valeur(g, "Allergenes", d.Allergenes, Theme.Rouge, largeur);
                if (d.Traces.Length > 0)
                    Valeur(g, "Traces", d.Traces, Theme.Orange, largeur);
                if (d.Additifs.Length > 0)
                    Valeur(g, "Additifs", d.Additifs, Theme.Texte, largeur);
            }

            if (d.Ingredients.Length > 0)
            {
                Titre("INGREDIENTS");
                Paragraphe(g, d.Ingredients, largeur);
            }

            Titre("SOURCE");
            Valeur(g, "Code-barres", d.Code, Theme.Texte, largeur);
            Valeur(g, "Donnees", d.Source == "openfoodfacts" ? "Open Food Facts"
                   : d.Source == "manuel" ? "saisie manuelle" : d.Source,
                   Theme.Doux, largeur);
        }

        private static string Note(string lettre)
        {
            return lettre.Length > 0 ? lettre.ToUpper() : "-";
        }

        private static string Nova(int groupe)
        {
            switch (groupe)
            {
                case 1: return "non transforme";
                case 2: return "ingredient culinaire";
                case 3: return "transforme";
                case 4: return "ultra-transforme";
            }
            return "";
        }

        private void Titre(string texte)
        {
            Rubrique r = new Rubrique();
            r.Titre = texte;
            r.Hauteur = 20;
            _rubriques.Add(r);
            _total += r.Hauteur;
        }

        private void Valeur(Graphics g, string libelle, string valeur, Color couleur,
                            int largeur)
        {
            Rubrique r = new Rubrique();
            r.Libelle = libelle;
            r.Valeur = valeur;
            r.Couleur = couleur;
            int largeurLibelle = Theme.Largeur(g, libelle, Theme.Petite) + 8;
            // Quatre pixels de reserve : une valeur qui touche le bord droit
            // se lit mal, autant la faire passer a la ligne.
            if (Theme.Largeur(g, valeur, Theme.PetiteGras) <= largeur - largeurLibelle - 4)
            {
                r.Hauteur = 15;
            }
            else
            {
                // Trop long pour la colonne de droite : la valeur passe a la
                // ligne, sur toute la largeur.
                r.Lignes = Theme.Replier(g, valeur, Theme.Petite, largeur, 3);
                r.Hauteur = 14 + r.Lignes.Length * 12;
            }
            _rubriques.Add(r);
            _total += r.Hauteur;
        }

        private void Paragraphe(Graphics g, string texte, int largeur)
        {
            Rubrique r = new Rubrique();
            r.Couleur = Theme.Texte;
            r.Lignes = Theme.Replier(g, texte, Theme.Petite, largeur, 14);
            r.Hauteur = r.Lignes.Length * 12 + 4;
            _rubriques.Add(r);
            _total += r.Hauteur;
        }

        private void Energie(Graphics g, Detail d, int largeur)
        {
            if (d.Kcal < 0 && d.Kj < 0)
                return;
            string texte = "";
            if (d.Kcal >= 0) texte = ((int)Math.Round(d.Kcal)) + " kcal";
            if (d.Kj >= 0)
                texte += (texte.Length > 0 ? " · " : "") + ((int)Math.Round(d.Kj)) + " kJ";
            Valeur(g, "Energie", texte, Theme.Texte, largeur);
        }

        private void Nutriment(Graphics g, string libelle, double valeur, string niveau,
                               int largeur)
        {
            if (valeur < 0)
                return;
            string texte = valeur.ToString("0.##") + " g";
            if (niveau.Length > 0)
                texte += "  " + Mot(niveau);
            Valeur(g, libelle, texte,
                   niveau.Length > 0 ? Theme.CouleurNiveau(niveau) : Theme.Texte, largeur);
        }

        private static string Mot(string niveau)
        {
            if (niveau == "low") return "(faible)";
            if (niveau == "moderate") return "(modere)";
            if (niveau == "high") return "(eleve)";
            return "";
        }

        // ------------------------------------------------------------ rendu

        protected override void Peindre(Graphics g, Rectangle zone)
        {
            PeindreBandeau(g, _detail != null && _detail.Nom.Length > 0
                           ? _detail.Nom : _article.Libelle, true);

            if (!_mesure && _detail != null)
            {
                Monter(g);
                _mesure = true;
            }

            int haut = HauteurBandeau + 2;
            int bas = BasUtile - HauteurBarre;

            if (_detail == null)
            {
                Theme.TexteCentre(g, _erreur.Length > 0 ? _erreur : "Chargement...",
                                  Theme.Normale, _erreur.Length > 0 ? Theme.Rouge
                                  : Theme.Doux, zone, haut + 40);
                PeindreBarre(g, bas);
                return;
            }

            int y = haut - _defilement;
            for (int i = 0; i < _rubriques.Count; i++)
            {
                Rubrique r = _rubriques[i];
                if (y + r.Hauteur >= haut && y < bas)
                    PeindreRubrique(g, r, y, haut, bas);
                y += r.Hauteur;
            }

            // Les rubriques peuvent depasser en haut et en bas de la zone :
            // on repeint les bandeaux par-dessus plutot que de decouper chaque
            // ligne, ce que le CF ne sait pas faire sans SetClip.
            Theme.Remplir(g, new Rectangle(0, 0, Width, haut), Theme.Fond);
            PeindreBandeau(g, _detail.Nom.Length > 0 ? _detail.Nom : _article.Libelle,
                           true);
            Theme.Remplir(g, new Rectangle(0, bas, Width, HauteurBarre), Theme.Fond);
            PeindreBarre(g, bas);
            PeindreFrappe(g);
        }

        private void PeindreRubrique(Graphics g, Rubrique r, int y, int haut, int bas)
        {
            int marge = 6;
            if (r.Titre != null)
            {
                Theme.Texte_(g, r.Titre, Theme.Minuscule, Theme.Barre, marge, y + 6);
                int l = Theme.Largeur(g, r.Titre, Theme.Minuscule);
                Theme.Ligne(g, marge + l + 5, y + 11, Width - marge, Theme.Trait);
                return;
            }
            if (r.Libelle == null)
            {
                for (int i = 0; i < r.Lignes.Length; i++)
                    Theme.Texte_(g, r.Lignes[i], Theme.Petite, r.Couleur, marge,
                                 y + 2 + i * 12);
                return;
            }
            Theme.Texte_(g, r.Libelle, Theme.Petite, Theme.Doux, marge, y + 1);
            if (r.Lignes == null)
            {
                int l = Theme.Largeur(g, r.Valeur, Theme.PetiteGras);
                Theme.Texte_(g, r.Valeur, Theme.PetiteGras, r.Couleur,
                             Width - marge - l, y + 1);
                return;
            }
            for (int i = 0; i < r.Lignes.Length; i++)
                Theme.Texte_(g, r.Lignes[i], Theme.Petite, r.Couleur, marge + 6,
                             y + 13 + i * 12);
        }

        private void PeindreBarre(Graphics g, int y)
        {
            Theme.Ligne(g, 0, y, Width, Theme.Trait);
            int marge = 3;
            int fleche = 40;
            int largeur = Width - 2 * marge - 2 * fleche - 8;
            BoutonZone(g, new Rectangle(marge, y + 2, largeur, HauteurBarre - 5),
                       "article", "Retour a l'article", Theme.FondDoux, Theme.Texte,
                       Theme.Petite);
            Rectangle haut = new Rectangle(marge + largeur + 4, y + 2, fleche,
                                           HauteurBarre - 5);
            BoutonZone(g, haut, "haut", "", Theme.FondDoux, Theme.Texte, Theme.Normale);
            Theme.Triangle(g, haut, true, Theme.Texte);
            Rectangle bas = new Rectangle(haut.Right + 4, y + 2, fleche,
                                          HauteurBarre - 5);
            BoutonZone(g, bas, "bas", "", Theme.FondDoux, Theme.Texte, Theme.Normale);
            Theme.Triangle(g, bas, false, Theme.Texte);
        }
    }
}
