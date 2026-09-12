using System;
using System.Drawing;
using System.Windows.Forms;

namespace Inventaire
{
    /// <summary>
    /// L'ecran d'un article, apres le bip. C'est lui qui doit tenir la promesse
    /// du programme : dire en un coup d'oeil si le produit est la, jusqu'a
    /// quand il est bon, et ce qu'il vaut.
    ///
    /// L'ordre vertical suit l'ordre des questions qu'on se pose devant le
    /// frigo : qu'est-ce que c'est, est-ce que c'est bon pour moi, combien il
    /// m'en reste, et que faire maintenant. Les deux gros boutons du bas ne
    /// bougent jamais de place, quelle que soit la fiche affichee.
    /// </summary>
    internal sealed class EcranArticle : Ecran
    {
        private Article _article = new Article();
        private int _lotChoisi = -1;
        private bool _saisieNom;
        private string _nom = "";

        public EcranArticle(Fenetre fenetre) : base(fenetre) { }

        // La saisie d'un libelle mobilise le clavier : pendant ce temps les
        // raccourcis et la gachette sont mis en sourdine.
        public override bool LectureActive { get { return !_saisieNom; } }
        protected override bool RaccourcisActifs { get { return true; } }
        public override bool ToucheGlobaleAutorisee { get { return !_saisieNom; } }

        public Article Courant { get { return _article; } }

        public override void Entrer(object argument)
        {
            Article article = argument as Article;
            if (article != null)
            {
                _article = article;
                _lotChoisi = -1;
                _saisieNom = false;
                _nom = "";
            }
            PoserCurseur("ajouter");
            DemanderPhoto();
            if (article != null && Perime())
                Sons.Jouer(Sons.Alerte);
        }

        public override void Retour()
        {
            Fenetre.Aller(Fenetre.Scan, null);
        }

        /// <summary>true si au moins un lot a depasse sa date.</summary>
        private bool Perime()
        {
            for (int i = 0; i < _article.Lots.Length; i++)
            {
                if (!_article.Lots[i].SansDate && _article.Lots[i].Jours < 0)
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------ photo

        private int TaillePhoto { get { return Reglages.TailleImage; } }

        private void DemanderPhoto()
        {
            if (_article.Image)
                Fenetre.Photos.Demander(_article.Code, TaillePhoto);
        }

        private void Agrandir()
        {
            if (!_article.Image || !Reglages.Photos)
            {
                Fenetre.Dire("pas de photo pour ce produit", false);
                return;
            }
            Sons.Jouer(Sons.Obturateur);
            Fenetre.Aller(Fenetre.Visionneuse, new DemandePhoto(
                _article.Code, _article.Libelle, Fenetre.Article, _article));
        }

        // ---------------------------------------------------------- clavier

        public override bool Touche(Keys touche)
        {
            if (_saisieNom)
            {
                switch (touche)
                {
                    case Keys.Enter:
                        ValiderNom();
                        return true;
                    case Keys.Escape:
                        _saisieNom = false;
                        Invalidate();
                        return true;
                    case Keys.Back:
                        if (_nom.Length > 0)
                        {
                            _nom = _nom.Substring(0, _nom.Length - 1);
                            Invalidate();
                        }
                        return true;
                }
                return false;
            }

            if (ToucheBase(touche))
                return true;

            switch (touche)
            {
                case Keys.Escape:
                    Retour();
                    return true;
                case Keys.Up:
                    Sons.Jouer(Sons.Clic);
                    Choisir(_lotChoisi - 1);
                    return true;
                case Keys.Down:
                    Sons.Jouer(Sons.Clic);
                    Choisir(_lotChoisi + 1);
                    return true;
                // Haut et bas parcourent les lots, gauche et droite les
                // actions : les deux dimensions de l'ecran, aux deux axes
                // du pave directionnel.
                case Keys.Left:
                    CurseurDeplacer(-1);
                    return true;
                case Keys.Right:
                    CurseurDeplacer(1);
                    return true;
            }
            return false;
        }

        protected override void Tape(char c)
        {
            if (_saisieNom && _nom.Length < 60)
            {
                _nom += c;
                Invalidate();
            }
        }

        protected override void Raccourci(char c)
        {
            if (c == '1') Ajouter();
            else if (c == '2') Retirer();
            else if (c == '3') CommencerNom();
            else if (c == '4') Fenetre.Aller(Fenetre.Detail, _article);
            else if (c == '5') Agrandir();
            else if (c == '0') Vider();
        }

        protected override void CodeLu(string code)
        {
            // Un autre article a ete bippe : on enchaine sans repasser par
            // l'accueil, c'est le geste naturel quand on range des courses.
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

        protected override bool Valider()
        {
            if (CurseurActiver())
                return true;
            Ajouter();
            return true;
        }

        protected override void Activer(string id)
        {
            if (id == "ajouter") Ajouter();
            else if (id == "retirer") Retirer();
            else if (id == "nommer") CommencerNom();
            else if (id == "fiche") Fenetre.Aller(Fenetre.Detail, _article);
            else if (id == "photo") Agrandir();
            else if (id == "vider") Vider();
            else if (id.Length > 4 && id.Substring(0, 4) == "lot.")
                Choisir(int.Parse(id.Substring(4)));
        }

        private void Choisir(int index)
        {
            if (_article.Lots.Length == 0)
                return;
            if (index < -1) index = _article.Lots.Length - 1;
            if (index >= _article.Lots.Length) index = -1;
            _lotChoisi = index == _lotChoisi ? -1 : index;
            Invalidate();
        }

        private Lot LotChoisi
        {
            get
            {
                if (_lotChoisi < 0 || _lotChoisi >= _article.Lots.Length)
                    return null;
                return _article.Lots[_lotChoisi];
            }
        }

        // --------------------------------------------------------- actions

        private void Ajouter()
        {
            Fenetre.Aller(Fenetre.Saisie, _article);
        }

        private void Retirer()
        {
            if (_article.Stock <= 0)
            {
                Fenetre.Dire("rien en stock", false);
                return;
            }
            string code = _article.Code;
            Lot vise = LotChoisi;
            int lot = vise == null ? 0 : vise.Id;
            Fenetre.Appeler("retrait", new FonctionReseau(delegate()
            {
                return Api.Retirer(code, 1, lot);
            }), new SuiteReseau(delegate(Reponse reponse)
            {
                Appliquer(reponse);
            }));
        }

        /// <summary>Supprime le lot choisi d'un coup, sans le decompter unite par unite.</summary>
        private void Vider()
        {
            Lot vise = LotChoisi;
            if (vise == null)
            {
                Fenetre.Dire("choisissez d'abord un lot", false);
                return;
            }
            string code = _article.Code;
            int id = vise.Id;
            int qte = vise.Qte;
            Fenetre.Appeler("suppression", new FonctionReseau(delegate()
            {
                return Api.Lot(id, 0, code);
            }), new SuiteReseau(delegate(Reponse reponse)
            {
                if (reponse.Joint && reponse.Ok)
                {
                    _article = Article.Depuis(reponse.Donnees);
                    _lotChoisi = -1;
                    Fenetre.Dire("lot supprime (-" + qte + ")", true, Sons.Balayage);
                    Invalidate();
                }
                else
                {
                    Fenetre.Dire(reponse.Erreur, false);
                }
            }));
        }

        /// <summary>Reprend la fiche renvoyee par le serveur : elle fait foi.</summary>
        public void Appliquer(Reponse reponse)
        {
            if (!reponse.Joint || !reponse.Ok)
            {
                Fenetre.Dire(reponse.Erreur.Length > 0 ? reponse.Erreur
                             : "refuse par le serveur", false);
                return;
            }
            Article avant = _article;
            _article = Article.Depuis(reponse.Donnees);
            _lotChoisi = -1;
            // Le message commence par + ou - : le son suit le sens du mouvement.
            int son = _article.Message.Length > 0 && _article.Message[0] == '-'
                ? Sons.Retrait : Sons.Ajout;
            if (_article.Stock == avant.Stock)
                son = Sons.Succes;
            Fenetre.Dire(_article.Message, true, son);
            Invalidate();
        }

        private void CommencerNom()
        {
            _saisieNom = true;
            _nom = _article.Nom;
            Invalidate();
        }

        private void ValiderNom()
        {
            string nom = _nom.Trim();
            _saisieNom = false;
            if (nom.Length == 0)
            {
                Invalidate();
                return;
            }
            string code = _article.Code;
            Fenetre.Appeler("libelle", new FonctionReseau(delegate()
            {
                return Api.Nommer(code, nom);
            }), new SuiteReseau(delegate(Reponse reponse)
            {
                if (reponse.Joint && reponse.Ok)
                {
                    _article = Article.Depuis(reponse.Donnees);
                    Fenetre.Dire("libelle enregistre", true);
                }
                else
                {
                    Fenetre.Dire(reponse.Erreur, false);
                }
                Invalidate();
            }));
        }

        // ------------------------------------------------------------ rendu

        protected override void Peindre(Graphics g, Rectangle zone)
        {
            Article a = _article;
            PeindreBandeau(g, a.Code, true);

            int marge = 4;
            int y = HauteurBandeau + 3;
            int hauteurBoutons = 44;
            int yBoutons = BasUtile - 3 - hauteurBoutons;
            int basStock = yBoutons - 4;
            if (LotChoisi != null)
                basStock -= 28;

            y = PeindreIdentite(g, a, marge, y);
            Theme.Ligne(g, marge, y, Width - marge, Theme.Trait);
            y += 4;
            y = PeindreTemoins(g, a, marge, y);
            Theme.Ligne(g, marge, y, Width - marge, Theme.Trait);
            y += 4;
            PeindreStock(g, a, marge, y, basStock);
            if (LotChoisi != null)
            {
                BoutonZone(g, new Rectangle(marge, yBoutons - 28, Width - 2 * marge, 24),
                           "vider", "0   VIDER CE LOT (-" + LotChoisi.Qte + ")",
                           Theme.Rouge, Color.White, Theme.PetiteGras);
            }
            PeindreBoutons(g, a, marge, yBoutons, hauteurBoutons);
            PeindreFrappe(g);
            if (_saisieNom)
                PeindreSaisieNom(g);
        }

        /// <summary>Photo, nom, marque, et les trois notes synthetiques.</summary>
        private int PeindreIdentite(Graphics g, Article a, int marge, int y)
        {
            int taille = Reglages.TailleImage;
            int xTexte = marge;
            int bas = y;

            Rectangle cadrePhoto = Rectangle.Empty;
            if (Reglages.Photos)
            {
                Rectangle cadre = new Rectangle(marge, y, taille, taille);
                Bitmap image = Fenetre.Photos.Obtenir(a.Code, taille);
                if (image != null)
                {
                    g.DrawImage(image, cadre.X, cadre.Y);
                }
                else
                {
                    Theme.Remplir(g, cadre, Theme.FondDoux);
                    Theme.TexteCentre(g, a.Image ? "..." : "?", Theme.Enorme, Theme.Trait,
                                      cadre, y + taille / 2 - 14);
                }
                Theme.Cadre(g, cadre, Theme.Trait);
                if (image != null)
                {
                    // Une loupe discrete dans le coin : sans elle, rien ne dit
                    // que la photo s'agrandit.
                    Rectangle coin = new Rectangle(cadre.Right - 14, cadre.Bottom - 12,
                                                   14, 12);
                    Theme.Remplir(g, coin, Theme.Barre);
                    Theme.TexteCentre(g, "+", Theme.PetiteGras, Color.White, coin,
                                      coin.Y - 1);
                }
                cadrePhoto = cadre;
                xTexte = cadre.Right + 6;
                bas = cadre.Bottom;
            }

            int largeur = Width - xTexte - marge;
            int yt = y;
            string[] lignes = Theme.Replier(g, a.Libelle, Theme.NormaleGras, largeur, 2);
            for (int i = 0; i < lignes.Length; i++)
            {
                Theme.Texte_(g, lignes[i], Theme.NormaleGras, Theme.Texte, xTexte, yt);
                yt += 13;
            }
            yt += 2;

            string sous;
            if (!a.Connu)
            {
                sous = "inconnu · 3 = nommer";
                Zones.Add(new Zone(new Rectangle(xTexte, yt, largeur, 12), "nommer"));
            }
            else
            {
                sous = a.Marque;
                if (a.Contenance.Length > 0)
                    sous = sous.Length > 0 ? sous + " · " + a.Contenance : a.Contenance;
            }
            // « fiche > » a droite : le seul chemin vers le detail Open Food
            // Facts, donc il doit se voir sans encombrer.
            string fiche = "fiche >";
            int largeurFiche = Theme.Largeur(g, fiche, Theme.Minuscule) + 4;
            Theme.Texte_(g, Theme.Tronquer(g, sous, Theme.Petite,
                                           largeur - largeurFiche),
                         Theme.Petite, a.Connu ? Theme.Doux : Theme.Orange, xTexte, yt);
            Theme.Texte_(g, fiche, Theme.Minuscule, Theme.Bleu,
                         Width - marge - largeurFiche + 4, yt + 1);
            yt += 15;

            // Nutri-Score, NOVA, Eco-Score : trois pastilles legendees. La
            // legende compte, un carre colore seul ne veut rien dire pour qui
            // ne connait pas le bareme.
            int x = xTexte;
            x = Pastille(g, x, yt, "Nutri", Theme.CouleurNutri(a.Nutriscore),
                         a.Nutriscore.ToUpper(), a.Nutriscore.Length > 0);
            x = Pastille(g, x, yt, "NOVA", Theme.CouleurNova(a.Nova),
                         a.Nova.ToString(), a.Nova > 0);
            x = Pastille(g, x, yt, "Eco", Theme.CouleurNutri(a.Ecoscore),
                         a.Ecoscore.ToUpper(), a.Ecoscore.Length > 0);
            if (x == xTexte && a.Connu)
                Theme.Texte_(g, "pas de note", Theme.Minuscule, Theme.Trait, xTexte,
                             yt + 6);
            yt += 22;

            // Toute la zone d'identite ouvre la fiche detaillee ; la photo
            // elle-meme l'agrandit. Declaree apres, elle gagne au toucher.
            Zones.Insert(0, new Zone(new Rectangle(0, y, Width, Math.Max(bas, yt) - y),
                                     "fiche"));
            if (cadrePhoto != Rectangle.Empty)
                Zones.Add(new Zone(cadrePhoto, "photo"));
            return Math.Max(bas, yt) + 3;
        }

        private static int Pastille(Graphics g, int x, int y, string legende, Color fond,
                                    string valeur, bool presente)
        {
            if (!presente)
                return x;
            Theme.Texte_(g, legende, Theme.Minuscule, Theme.Doux, x, y + 6);
            x += Theme.Largeur(g, legende, Theme.Minuscule) + 3;
            Theme.Pastille(g, new Rectangle(x, y, 19, 21), fond, valeur,
                           Theme.NormaleGras);
            return x + 24;
        }

        /// <summary>
        /// Les temoins nutritionnels d'Open Food Facts, en jauges a trois crans,
        /// puis ce qui releve de la securite : labels, allergenes, traces.
        /// Trois crans rouges sur le sucre se lisent sans savoir ce qu'est un
        /// gramme pour cent grammes.
        /// </summary>
        private int PeindreTemoins(Graphics g, Article a, int marge, int y)
        {
            string[] libelles = { "Gras", "AG.S", "Sucre", "Sel" };
            string[] niveaux = { a.NivGraisses, a.NivSatures, a.NivSucres, a.NivSel };

            bool aucun = true;
            for (int i = 0; i < niveaux.Length; i++)
            {
                if (niveaux[i].Length > 0)
                    aucun = false;
            }
            if (aucun && a.Kcal < 0)
            {
                Theme.Texte_(g, a.Connu ? "aucune donnee nutritionnelle"
                                        : "produit absent d'Open Food Facts",
                             Theme.Petite, Theme.Doux, marge, y + 2);
                y += 16;
            }
            else
            {
                int largeurKcal = 62;
                int largeurBloc = (Width - 2 * marge - largeurKcal) / 4;
                for (int i = 0; i < 4; i++)
                {
                    int x = marge + i * largeurBloc;
                    Rectangle bloc = new Rectangle(x, y, largeurBloc, 10);
                    Theme.TexteCentre(g, libelles[i], Theme.Minuscule, Theme.Doux, bloc, y);

                    int rang = Theme.RangNiveau(niveaux[i]);
                    Color couleur = Theme.CouleurNiveau(niveaux[i]);
                    int largeurCran = (largeurBloc - 10) / 3;
                    for (int cran = 0; cran < 3; cran++)
                    {
                        Rectangle r = new Rectangle(x + 4 + cran * (largeurCran + 1),
                                                    y + 11, largeurCran, 8);
                        Theme.Remplir(g, r, cran < rang ? couleur : Theme.FondDoux);
                        Theme.Cadre(g, r, cran < rang ? Theme.Assombrir(couleur)
                                                      : Theme.Trait);
                    }
                }
                if (a.Kcal >= 0)
                {
                    Rectangle bloc = new Rectangle(Width - marge - largeurKcal, y,
                                                   largeurKcal, 20);
                    Theme.TexteCentre(g, ((int)Math.Round(a.Kcal)).ToString(),
                                      Theme.NormaleGras, Theme.Texte, bloc, y - 1);
                    Theme.TexteCentre(g, "kcal /100", Theme.Minuscule, Theme.Doux,
                                      bloc, y + 12);
                }
                y += 23;
            }

            if (a.Labels.Length > 0)
            {
                Theme.Texte_(g, Theme.Tronquer(g, a.Labels, Theme.Minuscule,
                                               Width - 2 * marge),
                             Theme.Minuscule, Theme.Vert, marge, y);
                y += 11;
            }
            if (a.Allergenes.Length > 0 || a.Traces.Length > 0)
            {
                string texte = a.Allergenes.Length > 0
                    ? "allergenes : " + a.Allergenes : "";
                if (a.Traces.Length > 0)
                    texte += (texte.Length > 0 ? " · " : "") + "traces : " + a.Traces;
                Theme.Texte_(g, Theme.Tronquer(g, texte, Theme.Minuscule,
                                               Width - 2 * marge),
                             Theme.Minuscule, Theme.Rouge, marge, y);
                y += 11;
            }
            return y + 2;
        }

        /// <summary>Stock et lots, du plus urgent au moins urgent.</summary>
        private void PeindreStock(Graphics g, Article a, int marge, int y, int bas)
        {
            if (a.Stock == 0)
            {
                Theme.TexteCentre(g, "PAS EN STOCK", Theme.Grande, Theme.Doux,
                                  new Rectangle(0, 0, Width, 0), y + 6);
                Theme.TexteCentre(g, "1 pour l'ajouter au frigo", Theme.Petite, Theme.Doux,
                                  new Rectangle(0, 0, Width, 0), y + 26);
                return;
            }

            Theme.Texte_(g, "EN STOCK", Theme.PetiteGras, Theme.Doux, marge, y);
            string total = a.Stock.ToString();
            int l = Theme.Largeur(g, total, Theme.Grande);
            Theme.Texte_(g, total, Theme.Grande, Theme.Texte, Width - marge - l, y - 3);
            y += 15;

            int hauteurLigne = 19;
            for (int i = 0; i < a.Lots.Length; i++)
            {
                if (y + hauteurLigne > bas)
                {
                    Theme.Texte_(g, "+ " + (a.Lots.Length - i) + " autre(s) lot(s)",
                                 Theme.Minuscule, Theme.Doux, marge + 2, y + 3);
                    break;
                }
                Lot lot = a.Lots[i];
                Rectangle ligne = new Rectangle(marge, y, Width - 2 * marge,
                                                hauteurLigne - 2);
                Zones.Add(new Zone(ligne, "lot." + i));
                if (_lotChoisi == i)
                {
                    Theme.Remplir(g, ligne, Theme.FondDoux);
                    Theme.Cadre(g, ligne, Theme.Barre);
                }

                Color couleur = Theme.CouleurJours(lot.Jours, lot.SansDate);
                Theme.Remplir(g, new Rectangle(ligne.X + 3, y + 4, 9, 9), couleur);

                DateTime date = Dates.DepuisIso(lot.Peremption);
                string gauche = Dates.Libelle(date, lot.SansJour);
                Theme.Texte_(g, gauche, Theme.Petite, Theme.Texte, ligne.X + 17, y + 2);

                string droite = "×" + lot.Qte;
                int ld = Theme.Largeur(g, droite, Theme.PetiteGras);
                Theme.Texte_(g, droite, Theme.PetiteGras, Theme.Texte,
                             ligne.Right - ld - 4, y + 2);

                string etat = Theme.TexteJours(lot.Jours, lot.SansDate);
                int le = Theme.Largeur(g, etat, Theme.Petite);
                Theme.Texte_(g, etat, Theme.Petite, couleur,
                             ligne.Right - ld - 12 - le, y + 2);
                y += hauteurLigne;
            }
        }

        private void PeindreBoutons(Graphics g, Article a, int marge, int y, int hauteur)
        {
            int demi = (Width - 3 * marge) / 2;
            BoutonZone(g, new Rectangle(marge, y, demi, hauteur), "ajouter",
                       "1  AJOUTER", Theme.Vert, Color.White, Theme.NormaleGras);
            bool possible = a.Stock > 0;
            string libelle = LotChoisi != null ? "2  RETIRER 1" : "2  RETIRER";
            BoutonZone(g, new Rectangle(marge * 2 + demi, y, demi, hauteur), "retirer",
                       libelle, possible ? Theme.Rouge : Theme.Trait,
                       possible ? Color.White : Theme.Doux, Theme.NormaleGras);
        }

        /// <summary>
        /// Saisie d'un libelle, pour les codes absents d'Open Food Facts.
        /// Le pave numerique du terminal a un mode alphabetique ; a defaut, le
        /// tableau de bord du navigateur fait le meme travail au clavier.
        /// </summary>
        private void PeindreSaisieNom(Graphics g)
        {
            // Le voile recouvre les boutons : leurs zones tactiles doivent
            // disparaitre avec eux, sinon un appui de travers declencherait un
            // ajout pendant qu'on tape un libelle.
            Zones.Clear();
            Rectangle fond = new Rectangle(0, HauteurBandeau, Width,
                                           Height - HauteurBandeau);
            Theme.Remplir(g, fond, Theme.Fond);
            int marge = 8;
            int y = fond.Y + 24;
            Theme.Texte_(g, "Libelle du produit", Theme.Grande, Theme.Texte, marge, y);
            y += 26;
            Rectangle champ = new Rectangle(marge, y, Width - 2 * marge, 46);
            Theme.Remplir(g, champ, Theme.FondDoux);
            Theme.Cadre(g, champ, Theme.Barre);
            string[] lignes = Theme.Replier(g, _nom + "_", Theme.NormaleGras,
                                            champ.Width - 10, 2);
            for (int i = 0; i < lignes.Length; i++)
                Theme.Texte_(g, lignes[i], Theme.NormaleGras, Theme.Texte,
                             champ.X + 5, champ.Y + 6 + i * 15);
            y += 54;
            Theme.Texte_(g, "Entree valide · Echap annule", Theme.Petite, Theme.Doux,
                         marge, y);
            y += 18;
            int large = Width - 2 * marge;
            Theme.Texte_(g, "Sans mode alphabetique sur le pave,", Theme.Minuscule,
                         Theme.Doux, marge, y);
            Theme.Texte_(g, "nommez depuis le navigateur :", Theme.Minuscule,
                         Theme.Doux, marge, y + 11);
            Theme.Texte_(g, Theme.Tronquer(g, "http://" + Reglages.Hote + ":"
                                           + Reglages.Port + "/", Theme.Minuscule, large),
                         Theme.Minuscule, Theme.Barre, marge, y + 22);
        }
    }
}
