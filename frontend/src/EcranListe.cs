using System;
using System.Drawing;
using System.Windows.Forms;

namespace Inventaire
{
    /// <summary>
    /// Parcourir le frigo, du plus urgent au moins urgent.
    ///
    /// La barre de couleur a gauche de chaque ligne porte l'urgence. C'est
    /// volontairement la premiere chose que l'oeil rencontre en balayant la
    /// liste de haut en bas : on cherche du rouge, pas un nom. La liste etant
    /// deja triee par echeance, son debut est ce qui perime le plus tot -- un
    /// second ecran « bientot » n'aurait rien montre de plus.
    ///
    /// Tout se fait au clavier. Les fleches haut et bas parcourent les
    /// articles, gauche et droite choisissent entre ouvrir la fiche et retirer
    /// une unite, Entree execute. Vider le frigo apres un repas, c'est donc
    /// descendre la liste et appuyer sur Entree devant ce qu'on a sorti, sans
    /// jamais poser l'appareil pour prendre le stylet.
    /// </summary>
    internal sealed class EcranListe : Ecran
    {
        private const int Ouvrir_ = 0, Retirer_ = 1;

        private Ligne[] _lignes = new Ligne[0];
        private int _index;
        private int _colonne = Ouvrir_;
        private int _premier;
        private bool _charge;

        private const int HauteurLigne = 34;
        private const int HauteurBarre = 30;
        private const int LargeurRetrait = 32;
        /// <summary>Cote de la vignette de liste, en pixels.</summary>
        public const int TailleVignette = 26;

        public EcranListe(Fenetre fenetre) : base(fenetre) { }

        protected override bool RaccourcisActifs { get { return true; } }

        public override void Entrer(object argument)
        {
            Recharger(_charge);
        }

        private void Recharger(bool garderPosition)
        {
            int index = _index, premier = _premier;
            Fenetre.Appeler("inventaire", new FonctionReseau(delegate()
            {
                return Api.Inventaire("peremption");
            }), new SuiteReseau(delegate(Reponse reponse)
            {
                if (!reponse.Joint || !reponse.Ok)
                {
                    Fenetre.Dire(reponse.Erreur, false);
                    return;
                }
                _lignes = Ligne.Inventaire(reponse.Donnees);
                _charge = true;
                int dernier = Math.Max(0, _lignes.Length - 1);
                // Apres un retrait, revenir en tete de liste ferait perdre sa
                // place a chaque geste.
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
                    Colonne(Ouvrir_);
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
                Ouvrir(_index);
            return true;
        }

        protected override void Raccourci(char c)
        {
            if (c == '1') Ouvrir(_index);
            else if (c == '2') Retirer(_index);
            else if (c == '3') Recharger(true);
            else if (c == '5') Agrandir(_index);     // meme touche que sur la fiche
        }

        protected override void CodeLu(string code)
        {
            Consulter(code);
        }

        protected override void Activer(string id)
        {
            if (id == "haut") Deplacer(-Visibles());
            else if (id == "bas") Deplacer(Visibles());
            else if (id.Length > 6 && id.Substring(0, 6) == "photo.")
                Agrandir(int.Parse(id.Substring(6)));
            else if (id.Length > 8 && id.Substring(0, 8) == "retirer.")
                Retirer(int.Parse(id.Substring(8)));
            else if (id.Length > 6 && id.Substring(0, 6) == "ligne.")
                Ouvrir(int.Parse(id.Substring(6)));
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
            if (_lignes.Length == 0)
                return;
            _index += delta;
            if (_index < 0) _index = 0;
            if (_index >= _lignes.Length) _index = _lignes.Length - 1;
            int visibles = Visibles();
            if (_index < _premier) _premier = _index;
            if (_index >= _premier + visibles) _premier = _index - visibles + 1;
            Sons.Jouer(Sons.Clic);
            Invalidate();
        }

        private void Ouvrir(int index)
        {
            if (index < 0 || index >= _lignes.Length)
                return;
            _index = index;
            Invalidate();
            Consulter(_lignes[index].Code);
        }

        /// <summary>Sort une unite sans quitter la liste.</summary>
        private void Retirer(int index)
        {
            if (index < 0 || index >= _lignes.Length)
                return;
            _index = index;
            Ligne ligne = _lignes[index];
            string code = ligne.Code;
            string nom = ligne.Nom;
            Fenetre.Appeler("retrait", new FonctionReseau(delegate()
            {
                return Api.Retirer(code, 1, 0);     // FEFO : le plus urgent d'abord
            }), new SuiteReseau(delegate(Reponse reponse)
            {
                if (reponse.Joint && reponse.Ok)
                {
                    Fenetre.Dire("-1 " + nom, true, Sons.Retrait);
                    Recharger(true);
                }
                else
                {
                    Fenetre.Dire(reponse.Erreur, false);
                }
            }));
        }

        /// <summary>Ouvre la photo en grand, et revient ici en sortant.</summary>
        private void Agrandir(int index)
        {
            if (index < 0 || index >= _lignes.Length)
                return;
            _index = index;
            Ligne ligne = _lignes[index];
            if (!ligne.Image || !Reglages.Photos)
            {
                Ouvrir(index);      // pas de photo : le geste ouvre la fiche
                return;
            }
            Sons.Jouer(Sons.Obturateur);
            Fenetre.Aller(Fenetre.Visionneuse,
                          new DemandePhoto(ligne.Code, ligne.Nom, Fenetre.Liste, null));
        }

        private void Consulter(string code)
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

        // ------------------------------------------------------------ rendu

        protected override void Peindre(Graphics g, Rectangle zone)
        {
            PeindreBandeau(g, "Tout le frigo", true);

            int marge = 3;
            int y = HauteurBandeau + 2;
            int visibles = Visibles();

            // Le curseur clavier se pose sur le bouton de retrait de la ligne
            // courante, ou sur rien quand c'est la ligne elle-meme qui est visee.
            PoserCurseur(_colonne == Retirer_ ? "retirer." + _index : null);

            if (_lignes.Length == 0)
            {
                Theme.TexteCentre(g, _charge ? "Le frigo est vide."
                                  : "Chargement...", Theme.Normale, Theme.Doux,
                                  zone, y + 40);
            }

            for (int i = _premier; i < _lignes.Length && i < _premier + visibles; i++)
            {
                PeindreLigne(g, _lignes[i], i, marge, y);
                y += HauteurLigne;
            }

            PeindreBarre(g, marge);
            PeindreFrappe(g);
        }

        private void PeindreLigne(Graphics g, Ligne ligne, int index, int marge, int y)
        {
            Rectangle r = new Rectangle(marge, y, Width - 2 * marge, HauteurLigne - 3);
            Zones.Add(new Zone(r, "ligne." + index));
            if (index == _index)
            {
                Theme.Remplir(g, r, Theme.FondDoux);
                if (_colonne == Ouvrir_)
                    Theme.AnneauFocus(g, r);
                else
                    Theme.Cadre(g, r, Theme.Barre);
            }

            Color urgence = Theme.CouleurJours(ligne.Jours, ligne.SansDate);
            Theme.Remplir(g, new Rectangle(r.X + 4, r.Y + 5, 4, r.Height - 10), urgence);

            int xTexte = r.X + 12;
            if (Reglages.Photos)
            {
                Rectangle vignette = new Rectangle(r.X + 11, r.Y + 2, TailleVignette,
                                                   TailleVignette);
                PeindreVignette(g, ligne, vignette, index);
                xTexte = vignette.Right + 5;
            }

            // Bouton de retrait : declare apres la ligne, donc prioritaire au
            // toucher, et hors du cycle automatique des fleches puisque c'est
            // la colonne courante qui le designe.
            Rectangle retrait = new Rectangle(r.Right - LargeurRetrait - 3, r.Y + 4,
                                              LargeurRetrait, r.Height - 8);
            BoutonZone(g, retrait, "retirer." + index, "-1", Theme.Rouge, Color.White,
                       Theme.PetiteGras, false);

            string qte = "×" + ligne.Qte;
            int lq = Theme.Largeur(g, qte, Theme.NormaleGras);
            Theme.Texte_(g, qte, Theme.NormaleGras, Theme.Texte,
                         retrait.X - lq - 5, r.Y + 8);

            int largeurNom = retrait.X - lq - 10 - xTexte;
            Theme.Texte_(g, Theme.Tronquer(g, ligne.Nom, Theme.PetiteGras, largeurNom),
                         Theme.PetiteGras, Theme.Texte, xTexte, r.Y + 2);

            string etat = Theme.TexteJours(ligne.Jours, ligne.SansDate);
            Theme.Texte_(g, etat, Theme.Minuscule, urgence, xTexte, r.Y + 17);
            int le = Theme.Largeur(g, etat, Theme.Minuscule);
            if (ligne.Detail.Length > 0)
                Theme.Texte_(g, Theme.Tronquer(g, "· " + ligne.Detail, Theme.Minuscule,
                                               largeurNom - le - 4),
                             Theme.Minuscule, Theme.Doux, xTexte + le + 4, r.Y + 17);
        }

        /// <summary>
        /// Vignette du produit, avec sa note en pastille dans le coin.
        ///
        /// Superposer la pastille a l'image plutot que de la poser a cote rend
        /// une quinzaine de pixels au nom du produit -- et sur 240 de large,
        /// quinze pixels font la difference entre un nom lisible et un nom
        /// tronque.
        /// </summary>
        private void PeindreVignette(Graphics g, Ligne ligne, Rectangle cadre, int index)
        {
            Bitmap image = null;
            if (ligne.Image)
            {
                Fenetre.Photos.Demander(ligne.Code, TailleVignette);
                image = Fenetre.Photos.Obtenir(ligne.Code, TailleVignette);
            }
            if (image != null)
                g.DrawImage(image, cadre.X, cadre.Y);
            else
                Theme.Remplir(g, cadre, Theme.FondDoux);
            Theme.Cadre(g, cadre, Theme.Trait);

            if (ligne.Nutriscore.Length > 0)
                Theme.Pastille(g, new Rectangle(cadre.X, cadre.Bottom - 11, 11, 11),
                               Theme.CouleurNutri(ligne.Nutriscore),
                               ligne.Nutriscore.ToUpper(), Theme.Minuscule);
            if (image != null)
                Zones.Add(new Zone(cadre, "photo." + index));
        }

        private void PeindreBarre(Graphics g, int marge)
        {
            int y = BasUtile - HauteurBarre;
            Theme.Ligne(g, 0, y, Width, Theme.Trait);

            string aide = _colonne == Retirer_ ? "Entree retire 1"
                                               : "Entree ouvre la fiche";
            Theme.Texte_(g, aide, Theme.Minuscule,
                         _colonne == Retirer_ ? Theme.Rouge : Theme.Barre, marge + 2,
                         y + 3);
            if (_lignes.Length > 0)
            {
                string position = (_index + 1) + " / " + _lignes.Length
                    + "   < > choisir · 5 photo";
                Theme.Texte_(g, position, Theme.Minuscule, Theme.Doux, marge + 2, y + 15);
            }

            int fleche = 38;
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
