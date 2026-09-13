using System;
using System.Drawing;
using System.Windows.Forms;

namespace Inventaire
{
    /// <summary>
    /// La liste de courses, sur le terminal.
    ///
    /// Le geste qui compte est celui-ci : on constate qu'il ne reste plus de
    /// beurre, on bippe le paquet vide au-dessus de la poubelle, et c'est
    /// inscrit. Pas de clavier, pas de menu -- la gachette suffit, comme pour
    /// le reste de l'application. Un article deja sur la liste voit sa
    /// quantite augmenter au lieu d'apparaitre deux fois.
    ///
    /// Ce qui n'a pas de code-barres -- le pain, la salade -- se tape au pave
    /// alphanumerique quand le terminal en a un ; sur un modele a 28 touches,
    /// ces articles se posent depuis le site public, qui ecrit dans la meme
    /// liste.
    ///
    /// Cocher plutot que supprimer : un article coche reste visible, barre,
    /// jusqu'a ce qu'on vide le panier. C'est ce qui permet de verifier qu'on
    /// n'a rien oublie avant de passer en caisse.
    /// </summary>
    internal sealed class EcranCourses : Ecran
    {
        private const int Cocher_ = 0, Retirer_ = 1;

        private Course[] _articles = new Course[0];
        private int _index;
        private int _colonne = Cocher_;
        private int _premier;
        private bool _charge;

        private const int HauteurLigne = 32;
        private const int HauteurBarre = 30;
        private const int LargeurBouton = 30;
        public const int TailleVignette = 24;

        public EcranCourses(Fenetre fenetre) : base(fenetre) { }

        protected override bool RaccourcisActifs { get { return true; } }

        public override void Entrer(object argument)
        {
            Recharger(_charge);
        }

        private void Recharger(bool garderPosition)
        {
            int index = _index, premier = _premier;
            Fenetre.Appeler("courses", new FonctionReseau(delegate()
            {
                return Api.Courses();
            }), new SuiteReseau(delegate(Reponse reponse)
            {
                if (!reponse.Joint || !reponse.Ok)
                {
                    Fenetre.Dire(reponse.Erreur, false);
                    return;
                }
                _articles = Course.Depuis(reponse.Donnees);
                _charge = true;
                int dernier = Math.Max(0, _articles.Length - 1);
                _index = garderPosition ? Math.Min(index, dernier) : 0;
                _premier = garderPosition ? Math.Min(premier, dernier) : 0;
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
                    Deplacer(-1);
                    return true;
                case Keys.Down:
                    Deplacer(1);
                    return true;
                case Keys.Left:
                    Colonne(Cocher_);
                    return true;
                case Keys.Right:
                    Colonne(Retirer_);
                    return true;
            }
            return false;
        }

        protected override bool Valider()
        {
            if (_colonne == Retirer_)
                Retirer(_index);
            else
                Cocher(_index);
            return true;
        }

        protected override void Raccourci(char c)
        {
            if (c == '1') Cocher(_index);
            else if (c == '2') Retirer(_index);
            else if (c == '3') Recharger(true);
            else if (c == '9') ViderPanier();
        }

        /// <summary>Un bip inscrit le produit sur la liste.</summary>
        protected override void CodeLu(string code)
        {
            Fenetre.Appeler("course", new FonctionReseau(delegate()
            {
                return Api.CoursesAjouter(code, null, 1);
            }), new SuiteReseau(delegate(Reponse reponse)
            {
                if (reponse.Joint && reponse.Ok)
                {
                    Fenetre.Dire(reponse.Donnees.S("message"), true, Sons.Ajout);
                    Recharger(true);
                }
                else
                {
                    Fenetre.Dire(reponse.Erreur, false);
                }
            }));
        }

        protected override void Activer(string id)
        {
            if (id == "haut") Deplacer(-Visibles());
            else if (id == "bas") Deplacer(Visibles());
            else if (id == "vider") ViderPanier();
            else if (id.Length > 7 && id.Substring(0, 7) == "cocher.")
                Cocher(int.Parse(id.Substring(7)));
            else if (id.Length > 8 && id.Substring(0, 8) == "retirer.")
                Retirer(int.Parse(id.Substring(8)));
            else if (id.Length > 6 && id.Substring(0, 6) == "ligne.")
                Cocher(int.Parse(id.Substring(6)));
        }

        private int Visibles()
        {
            int hauteur = Height - HauteurBandeau - HauteurBarre - 3;
            int n = hauteur / HauteurLigne;
            return n < 1 ? 1 : n;
        }

        private void Colonne(int colonne)
        {
            if (_colonne == colonne)
                return;
            _colonne = colonne;
            Sons.Jouer(Sons.Clic);
            Invalidate();
        }

        private void Deplacer(int delta)
        {
            if (_articles.Length == 0)
                return;
            _index += delta;
            if (_index < 0) _index = 0;
            if (_index >= _articles.Length) _index = _articles.Length - 1;
            int visibles = Visibles();
            if (_index < _premier) _premier = _index;
            if (_index >= _premier + visibles) _premier = _index - visibles + 1;
            Sons.Jouer(Sons.Clic);
            Invalidate();
        }

        // --------------------------------------------------------- actions

        private void Cocher(int index)
        {
            if (index < 0 || index >= _articles.Length)
                return;
            _index = index;
            Course article = _articles[index];
            int id = article.Id;
            bool pris = !article.Pris;
            // L'oeil doit suivre le doigt : on bascule la ligne tout de suite
            // et le serveur confirme derriere. Un echec recharge, ce qui remet
            // l'affichage d'aplomb.
            article.Pris = pris;
            Invalidate();
            Fenetre.Appeler("coche", new FonctionReseau(delegate()
            {
                return Api.CoursesCocher(id, pris);
            }), new SuiteReseau(delegate(Reponse reponse)
            {
                if (reponse.Joint && reponse.Ok)
                {
                    Sons.Jouer(pris ? Sons.Ajout : Sons.Clic);
                    Recharger(true);
                }
                else
                {
                    Fenetre.Dire(reponse.Erreur, false);
                    Recharger(true);
                }
            }));
        }

        private void Retirer(int index)
        {
            if (index < 0 || index >= _articles.Length)
                return;
            _index = index;
            Course article = _articles[index];
            int id = article.Id;
            string libelle = article.Libelle;
            Fenetre.Appeler("retrait", new FonctionReseau(delegate()
            {
                return Api.CoursesRetirer(id);
            }), new SuiteReseau(delegate(Reponse reponse)
            {
                if (reponse.Joint && reponse.Ok)
                {
                    Fenetre.Dire(libelle + " retire", true, Sons.Retrait);
                    Recharger(true);
                }
                else
                {
                    Fenetre.Dire(reponse.Erreur, false);
                }
            }));
        }

        private void ViderPanier()
        {
            Fenetre.Appeler("vidage", new FonctionReseau(delegate()
            {
                return Api.CoursesVider(false);
            }), new SuiteReseau(delegate(Reponse reponse)
            {
                if (reponse.Joint && reponse.Ok)
                {
                    Fenetre.Dire(reponse.Donnees.S("message"), true, Sons.Retrait);
                    Recharger(false);
                }
                else
                {
                    Fenetre.Dire(reponse.Erreur, false);
                }
            }));
        }

        // ------------------------------------------------------------ rendu

        protected override void Peindre(Graphics g, Rectangle zone)
        {
            PeindreBandeau(g, "Liste de courses", true);

            int marge = 3;
            int y = HauteurBandeau + 2;
            int visibles = Visibles();

            PoserCurseur(_colonne == Retirer_ ? "retirer." + _index : "cocher." + _index);

            if (_articles.Length == 0)
            {
                Theme.TexteCentre(g, _charge ? "Liste vide." : "Chargement...",
                                  Theme.Normale, Theme.Doux, zone, y + 30);
                if (_charge)
                    Theme.TexteCentre(g, "Bippez un produit pour l'inscrire.",
                                      Theme.Minuscule, Theme.Doux, zone, y + 52);
            }

            for (int i = _premier; i < _articles.Length && i < _premier + visibles; i++)
            {
                PeindreLigne(g, _articles[i], i, marge, y);
                y += HauteurLigne;
            }

            PeindreBarre(g, marge);
            PeindreFrappe(g);
        }

        private void PeindreLigne(Graphics g, Course article, int index, int marge, int y)
        {
            Rectangle r = new Rectangle(marge, y, Width - 2 * marge, HauteurLigne - 3);
            Zones.Add(new Zone(r, "ligne." + index));
            if (index == _index)
            {
                Theme.Remplir(g, r, Theme.FondDoux);
                Theme.Cadre(g, r, Theme.Barre);
            }

            // La case a cocher est la premiere chose sous le pouce : c'est
            // l'action faite des dizaines de fois dans un magasin.
            Rectangle case_ = new Rectangle(r.X + 4, r.Y + 5, 18, r.Height - 10);
            BoutonZone(g, case_, "cocher." + index, article.Pris ? "X" : "",
                       article.Pris ? Theme.Vert : Theme.FondDoux,
                       article.Pris ? Color.White : Theme.Texte,
                       Theme.PetiteGras, false);

            int xTexte = case_.Right + 5;
            if (Reglages.Photos && article.Image && article.Code.Length > 0)
            {
                Rectangle vignette = new Rectangle(xTexte, r.Y + 2, TailleVignette,
                                                   TailleVignette);
                Fenetre.Photos.Demander(article.Code, TailleVignette);
                Bitmap image = Fenetre.Photos.Obtenir(article.Code, TailleVignette);
                if (image != null)
                    g.DrawImage(image, vignette.X, vignette.Y);
                else
                    Theme.Remplir(g, vignette, Theme.FondDoux);
                Theme.Cadre(g, vignette, Theme.Trait);
                xTexte = vignette.Right + 5;
            }

            Rectangle retrait = new Rectangle(r.Right - LargeurBouton - 3, r.Y + 4,
                                              LargeurBouton, r.Height - 8);
            BoutonZone(g, retrait, "retirer." + index, "X", Theme.Rouge, Color.White,
                       Theme.PetiteGras, false);

            string qte = article.Qte > 1 ? "×" + article.Qte : "";
            int lq = qte.Length > 0 ? Theme.Largeur(g, qte, Theme.NormaleGras) : 0;
            if (lq > 0)
                Theme.Texte_(g, qte, Theme.NormaleGras, Theme.Texte,
                             retrait.X - lq - 5, r.Y + 7);

            Color encre = article.Pris ? Theme.Doux : Theme.Texte;
            int largeur = retrait.X - lq - 10 - xTexte;
            string libelle = Theme.Tronquer(g, article.Libelle, Theme.PetiteGras, largeur);
            Theme.Texte_(g, libelle, Theme.PetiteGras, encre, xTexte, r.Y + 2);

            // Un trait en travers du nom marque ce qui est deja au panier : sur
            // un ecran de cette taille, c'est plus lisible qu'un gris de plus.
            if (article.Pris)
            {
                int milieu = r.Y + 2 + Theme.Hauteur(g, Theme.PetiteGras) / 2;
                Theme.Ligne(g, xTexte, milieu,
                            xTexte + Theme.Largeur(g, libelle, Theme.PetiteGras), encre);
            }

            if (article.Detail.Length > 0)
                Theme.Texte_(g, Theme.Tronquer(g, article.Detail, Theme.Minuscule, largeur),
                             Theme.Minuscule, Theme.Doux, xTexte, r.Y + 16);
        }

        private void PeindreBarre(Graphics g, int marge)
        {
            int y = BasUtile - HauteurBarre;
            Theme.Ligne(g, 0, y, Width, Theme.Trait);

            int aPrendre = 0, panier = 0;
            for (int i = 0; i < _articles.Length; i++)
            {
                if (_articles[i].Pris) panier += _articles[i].Qte;
                else aPrendre += _articles[i].Qte;
            }

            Theme.Texte_(g, aPrendre + " a prendre · " + panier + " au panier",
                         Theme.Minuscule, Theme.Barre, marge + 2, y + 3);
            Theme.Texte_(g, "Bippez pour inscrire · 9 vide le panier",
                         Theme.Minuscule, Theme.Doux, marge + 2, y + 15);

            int fleche = 32;
            Rectangle vider = new Rectangle(Width - marge - 2 * fleche - 42, y + 2, 38,
                                            HauteurBarre - 5);
            BoutonZone(g, vider, "vider", "vider", Theme.FondDoux, Theme.Doux,
                       Theme.Minuscule, false);

            Rectangle haut = new Rectangle(Width - marge - 2 * fleche - 4, y + 2, fleche,
                                           HauteurBarre - 5);
            BoutonZone(g, haut, "haut", "", Theme.FondDoux, Theme.Texte, Theme.Normale,
                       false);
            Theme.Triangle(g, haut, true, Theme.Texte);
            Rectangle bas = new Rectangle(haut.Right + 4, y + 2, fleche,
                                          HauteurBarre - 5);
            BoutonZone(g, bas, "bas", "", Theme.FondDoux, Theme.Texte, Theme.Normale,
                       false);
            Theme.Triangle(g, bas, false, Theme.Texte);
        }
    }
}
