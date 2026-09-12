using System;
using System.Drawing;
using System.Windows.Forms;

namespace Inventaire
{
    /// <summary>
    /// Saisie de la peremption et de la quantite, avant ajout au frigo.
    ///
    /// C'est le geste le plus repete de tout le programme. Quatre chemins y
    /// menent, du plus rapide au plus precis, et ils cohabitent sans se gener :
    ///
    ///   1. la duree habituelle proposee par le serveur, deja en place --
    ///      il ne reste qu'a valider ;
    ///   2. un raccourci d'echeance (+3 j, +1 semaine, +1 mois) ;
    ///   3. la date au pave, en quatre, six ou huit chiffres ;
    ///   4. le jour, le mois et l'annee pris separement : on se place dessus
    ///      avec haut et bas, on corrige avec gauche et droite.
    ///
    /// Le quatrieme chemin est celui qui manquait. Corriger « le 12 au lieu du
    /// 15 » demandait de retaper la date entiere ; il suffit maintenant de
    /// deux appuis sur une fleche, sans quitter la main de l'appareil.
    ///
    /// La date resolue est toujours relue sous le champ, jour de la semaine
    /// compris : « mar 25/10/26 · dans 43 j » se verifie d'un coup d'oeil,
    /// « 251026 » non.
    /// </summary>
    internal sealed class EcranSaisie : Ecran
    {
        private const int Jour = 0, Mois = 1, Annee = 2, Quantite_ = 3;
        private const int PremierRaccourci = 4;

        private static readonly int[] RaccourcisJours = { 3, 7, 15, 30, 90 };
        private static readonly string[] RaccourcisNoms =
            { "+3 j", "+1 sem", "+2 sem", "+1 mois", "+3 mois" };
        /// <summary>jour, mois, annee, quantite, les cinq echeances, sans date.</summary>
        private const int Positions = PremierRaccourci + 6;
        private const int SansDate = Positions - 1;

        private Article _article = new Article();
        private string _chiffres = "";
        private DateTime _date = Dates.Aucune;
        /// <summary>L'emballage n'indique qu'un mois : on n'invente pas de jour.</summary>
        private bool _sansJour;
        private int _qte = 1;
        private int _segment = Jour;

        public EcranSaisie(Fenetre fenetre) : base(fenetre) { }

        // Ici chaque chiffre est un chiffre de date : pas de detection de
        // rafale, pas de raccourci. Les caracteres arrivent bruts dans Tape.
        public override bool LectureActive { get { return false; } }

        public override void Entrer(object argument)
        {
            Article article = argument as Article;
            if (article != null)
                _article = article;
            _chiffres = "";
            _sansJour = false;
            _qte = Reglages.QuantiteParDefaut;
            _segment = Jour;
            // Duree de conservation deja constatee pour ce code : le serveur
            // la deduit du journal, on la propose en place.
            _date = _article.Suggestion >= 0
                ? Aujourdhui.AddDays(_article.Suggestion)
                : Dates.Aucune;
        }

        public override void Retour()
        {
            Fenetre.Aller(Fenetre.Article, _article);
        }

        // ------------------------------------------------------------ etat

        /// <summary>Date effective, ou Aucune. erreur non nulle = saisie fautive.</summary>
        private DateTime Resolue(out string erreur)
        {
            erreur = null;
            if (_chiffres.Length == 0)
                return _date;
            DateTime valeur;
            string souci;
            if (Dates.Analyser(_chiffres, Aujourdhui, false, out valeur, out souci))
                return valeur;
            // Tant que la saisie est trop courte, ce n'est pas une faute : on
            // ne signale rien, l'utilisateur est simplement en train de taper.
            if (_chiffres.Length >= 4)
                erreur = souci;
            return Dates.Aucune;
        }

        // --------------------------------------------------------- clavier

        public override bool Touche(Keys touche)
        {
            switch (touche)
            {
                case Keys.Enter:
                    if (_segment >= PremierRaccourci)
                    {
                        AppliquerRaccourci(_segment);
                        _segment = Jour;    // un second Entree valide l'ajout
                        Invalidate();
                        return true;
                    }
                    Valider_();
                    return true;
                case Keys.Escape:
                    Retour();
                    return true;
                case Keys.Back:
                    Reculer();
                    return true;
                // Les fleches suivent la disposition : jour, mois et annee
                // sont cote a cote, donc gauche et droite s'y deplacent ; la
                // valeur, elle, monte et descend. C'est la meme regle partout
                // dans le programme -- on parcourt dans l'axe ou les choses
                // sont rangees, et on regle dans l'autre.
                case Keys.Left:
                    Segment(_segment - 1);
                    return true;
                case Keys.Right:
                case Keys.Tab:
                    Segment(_segment + 1);
                    return true;
                case Keys.Up:
                    Modifier(1);
                    return true;
                case Keys.Down:
                    Modifier(-1);
                    return true;
            }
            return false;
        }

        protected override void Tape(char c)
        {
            if (c >= '0' && c <= '9')
            {
                if (_chiffres.Length >= 8)
                    return;
                _chiffres += c;
                _date = Dates.Aucune;
                _sansJour = false;      // une saisie chiffree donne un jour
                _segment = _chiffres.Length < 2 ? Jour
                    : _chiffres.Length < 4 ? Mois : Annee;
                Invalidate();
                return;
            }
            if (c == '+') Modifier(1);
            else if (c == '-') Modifier(-1);
        }

        private void Reculer()
        {
            if (_chiffres.Length > 0)
            {
                _chiffres = _chiffres.Substring(0, _chiffres.Length - 1);
                Invalidate();
                return;
            }
            // Sur le jour, le premier retour arriere enleve le jour et garde
            // le mois : beaucoup d'emballages ne portent que celui-la.
            if (_segment == Jour && _date != Dates.Aucune && !_sansJour)
            {
                _sansJour = true;
                _date = FinDeMois(_date);
                Invalidate();
                return;
            }
            if (_date != Dates.Aucune)
            {
                _date = Dates.Aucune;
                _sansJour = false;
                Invalidate();
            }
        }

        private void Segment(int cible)
        {
            _segment = (cible + Positions) % Positions;
            Sons.Jouer(Sons.Clic);
            Invalidate();
        }

        /// <summary>
        /// Haut et bas. Sur une valeur, ils la corrigent d'un cran ; sur un
        /// raccourci d'echeance, qui n'a rien a incrementer, ils continuent de
        /// parcourir -- pour qu'aucune touche ne reste sans effet.
        /// </summary>
        private void Modifier(int delta)
        {
            if (_segment >= PremierRaccourci)
            {
                Segment(_segment + (delta > 0 ? -1 : 1));
                return;
            }
            Ajuster(delta);
        }

        /// <summary>Corrige d'un cran ce qui est selectionne.</summary>
        private void Ajuster(int delta)
        {
            if (_segment == Quantite_)
            {
                Quantite(delta);
                return;
            }

            // Partir d'aujourd'hui quand rien n'est pose : une premiere fleche
            // vers la droite donne demain, ce a quoi on s'attend.
            string erreur;
            DateTime date = Resolue(out erreur);
            bool vierge = date == Dates.Aucune;
            if (vierge)
                date = Aujourdhui;
            _chiffres = "";

            if (_segment == Jour)
            {
                // Toucher au jour, c'est en donner un.
                _sansJour = false;
                date = date.AddDays(delta);
            }
            else
            {
                // Regler le mois ou l'annee sans avoir touche au jour ne doit
                // pas en inventer un : beaucoup d'emballages n'indiquent que
                // « avant fin 11/2026 ».
                if (vierge)
                    _sansJour = true;
                date = _segment == Mois ? AjouterMois(date, delta)
                                        : AjouterAnnees(date, delta);
                if (_sansJour)
                    date = FinDeMois(date);
            }
            _date = date;
            Sons.Jouer(Sons.Clic);
            Invalidate();
        }

        /// <summary>Ajoute des mois en ramenant le jour dans le mois d'arrivee.</summary>
        private static DateTime AjouterMois(DateTime date, int delta)
        {
            int total = date.Year * 12 + (date.Month - 1) + delta;
            int annee = total / 12;
            int mois = total % 12 + 1;
            if (annee < 1970) { annee = 1970; mois = 1; }
            if (annee > 2099) { annee = 2099; mois = 12; }
            int fin = DateTime.DaysInMonth(annee, mois);
            return new DateTime(annee, mois, date.Day < fin ? date.Day : fin);
        }

        /// <summary>Dernier jour du mois : le sens de « a consommer avant fin... ».</summary>
        private static DateTime FinDeMois(DateTime date)
        {
            return new DateTime(date.Year, date.Month,
                                DateTime.DaysInMonth(date.Year, date.Month));
        }

        private static DateTime AjouterAnnees(DateTime date, int delta)
        {
            int annee = date.Year + delta;
            if (annee < 1970) annee = 1970;
            if (annee > 2099) annee = 2099;
            int fin = DateTime.DaysInMonth(annee, date.Month);
            return new DateTime(annee, date.Month, date.Day < fin ? date.Day : fin);
        }

        private void Quantite(int delta)
        {
            int valeur = _qte + delta;
            if (valeur < 1) valeur = 1;
            if (valeur > 99) valeur = 99;
            if (valeur != _qte)
            {
                _qte = valeur;
                Sons.Jouer(Sons.Clic);
                Invalidate();
            }
        }

        private void AppliquerRaccourci(int position)
        {
            _chiffres = "";
            _sansJour = false;
            _date = position == SansDate ? Dates.Aucune
                : Aujourdhui.AddDays(RaccourcisJours[position - PremierRaccourci]);
            Sons.Jouer(Sons.Navigation);
            Invalidate();
        }

        protected override void Activer(string id)
        {
            if (id == "valider") { Valider_(); return; }
            if (id == "plus") { Quantite(1); return; }
            if (id == "moins") { Quantite(-1); return; }
            if (id.Length > 4 && id.Substring(0, 4) == "seg.")
            {
                int cible = int.Parse(id.Substring(4));
                if (cible >= PremierRaccourci)
                    AppliquerRaccourci(cible);
                _segment = cible >= PremierRaccourci ? Jour : cible;
                Invalidate();
            }
        }

        private void Valider_()
        {
            string erreur;
            DateTime date = Resolue(out erreur);
            if (erreur != null)
            {
                Fenetre.Dire(erreur, false);
                return;
            }
            string code = _article.Code;
            int qte = _qte;
            // AAAA-MM quand seul le mois est connu : le serveur le range au
            // dernier jour du mois en retenant que le jour etait absent.
            string iso = _sansJour ? Dates.VersIsoMois(date) : Dates.VersIso(date);
            Fenetre.Appeler("ajout", new FonctionReseau(delegate()
            {
                return Api.Ajouter(code, qte, iso);
            }), new SuiteReseau(delegate(Reponse reponse)
            {
                if (!reponse.Joint || !reponse.Ok)
                {
                    Fenetre.Dire(reponse.Erreur.Length > 0 ? reponse.Erreur
                                 : "ajout refuse", false);
                    return;
                }
                Article mis = Article.Depuis(reponse.Donnees);
                Fenetre.Aller(Fenetre.Article, mis);
                Fenetre.Dire(mis.Message, true);
            }));
        }

        // ------------------------------------------------------------ rendu

        protected override void Peindre(Graphics g, Rectangle zone)
        {
            PeindreBandeau(g, _article.Libelle, true);

            int marge = 6;
            int largeur = Width - 2 * marge;
            int y = HauteurBandeau + 5;

            Theme.Texte_(g, "PEREMPTION", Theme.PetiteGras, Theme.Doux, marge, y);
            if (_chiffres.Length == 0 && _date != Dates.Aucune
                && _article.Suggestion >= 0)
            {
                string note = "habituel : " + _article.Suggestion + " j";
                int l = Theme.Largeur(g, note, Theme.Minuscule);
                Theme.Texte_(g, note, Theme.Minuscule, Theme.Barre,
                             Width - marge - l, y + 1);
            }
            y += 14;

            string erreur;
            DateTime date = Resolue(out erreur);
            y = PeindreSegments(g, date, erreur, marge, largeur, y);
            y = PeindreRelecture(g, zone, date, erreur, y);
            y = PeindreRaccourcis(g, date, marge, largeur, y);
            PeindreQuantite(g, marge, largeur, y);

            int hauteurBouton = 46;
            int yBouton = BasUtile - 3 - hauteurBouton;
            Theme.Texte_(g, _sansJour ? "sans jour · retour arriere efface le jour"
                         : "gauche/droite : champ · haut/bas : -1 / +1",
                         Theme.Minuscule, _sansJour ? Theme.Barre : Theme.Doux,
                         marge, yBouton - 14);
            BoutonZone(g, new Rectangle(marge, yBouton, largeur, hauteurBouton),
                       "valider", "AJOUTER AU FRIGO", Theme.Vert, Color.White,
                       Theme.Grande, false);
        }

        /// <summary>Jour, mois et annee en trois cases separement modifiables.</summary>
        private int PeindreSegments(Graphics g, DateTime date, string erreur, int marge,
                                    int largeur, int y)
        {
            int hauteur = 42;
            int[] parts = { 30, 30, 40 };
            string[] legendes = { "jour", "mois", "annee" };
            string[] valeurs = ValeursSegments(date);

            int x = marge;
            for (int i = 0; i < 3; i++)
            {
                int w = i == 2 ? marge + largeur - x : largeur * parts[i] / 100;
                Rectangle r = new Rectangle(x, y, w - (i == 2 ? 0 : 2), hauteur);
                Zones.Add(new Zone(r, "seg." + i));
                bool actif = _segment == i;
                Theme.Remplir(g, r, actif ? Theme.Barre : Theme.FondDoux);
                Theme.Cadre(g, r, erreur != null ? Theme.Rouge
                            : actif ? Theme.Assombrir(Theme.Barre) : Theme.Trait);
                Color encre = actif ? Color.White
                    : valeurs[i][0] == '-' ? Theme.Trait : Theme.Texte;
                Theme.TexteCentre(g, valeurs[i], Theme.Enorme, encre, r, y + 6);
                Theme.TexteCentre(g, legendes[i], Theme.Minuscule,
                                  actif ? Color.White : Theme.Doux, r, y + hauteur - 12);
                x += w;
            }
            return y + hauteur + 4;
        }

        /// <summary>Ce qu'affichent les trois cases : la saisie en cours, ou la date.</summary>
        private string[] ValeursSegments(DateTime date)
        {
            if (_chiffres.Length > 0)
            {
                string plein = _chiffres;
                int cible = _chiffres.Length > 6 ? 8 : 6;
                while (plein.Length < cible)
                    plein += "-";
                return new string[] {
                    plein.Substring(0, 2), plein.Substring(2, 2),
                    cible == 8 ? plein.Substring(4, 4) : "20" + plein.Substring(4, 2),
                };
            }
            if (date == Dates.Aucune)
                return new string[] { "--", "--", "----" };
            return new string[] {
                _sansJour ? "--" : date.ToString("dd"),
                date.ToString("MM"), date.Year.ToString(),
            };
        }

        private int PeindreRelecture(Graphics g, Rectangle zone, DateTime date,
                                     string erreur, int y)
        {
            if (erreur != null)
            {
                Theme.TexteCentre(g, erreur, Theme.Petite, Theme.Rouge, zone, y);
            }
            else if (date == Dates.Aucune)
            {
                Theme.TexteCentre(g, "aucune date ne sera enregistree", Theme.Petite,
                                  Theme.Doux, zone, y);
            }
            else
            {
                int jours = Dates.JoursRestants(date, Aujourdhui);
                Theme.TexteCentre(g, Dates.Libelle(date, _sansJour) + " · "
                                  + Theme.TexteJours(jours, false), Theme.PetiteGras,
                                  Theme.CouleurJours(jours, false), zone, y);
            }
            return y + 17;
        }

        private int PeindreRaccourcis(Graphics g, DateTime date, int marge, int largeur,
                                      int y)
        {
            int hauteur = 26;
            int tiers = (largeur - 8) / 3;
            for (int i = 0; i < 6; i++)
            {
                int colonne = i % 3, rangee = i / 3;
                int w = colonne == 2 ? largeur - 2 * (tiers + 4) : tiers;
                Rectangle r = new Rectangle(marge + colonne * (tiers + 4),
                                            y + rangee * (hauteur + 3), w, hauteur);
                int position = PremierRaccourci + i;
                bool sansDate = position == SansDate;
                bool pose = _chiffres.Length == 0 && (sansDate
                    ? date == Dates.Aucune
                    : date != Dates.Aucune
                      && Dates.JoursRestants(date, Aujourdhui) == RaccourcisJours[i]);
                Zones.Add(new Zone(r, "seg." + position));
                Theme.Bouton(g, r, sansDate ? "sans date" : RaccourcisNoms[i],
                             pose ? Theme.Barre : Theme.FondDoux,
                             pose ? Color.White : Theme.Texte, Theme.Petite,
                             Enfoncee("seg." + position));
                if (_segment == position)
                    Theme.AnneauFocus(g, r);
            }
            return y + 2 * hauteur + 3 + 6;
        }

        private void PeindreQuantite(Graphics g, int marge, int largeur, int y)
        {
            bool actif = _segment == Quantite_;
            Rectangle ligne = new Rectangle(marge, y, largeur, 30);
            Zones.Add(new Zone(ligne, "seg." + Quantite_));
            if (actif)
            {
                Theme.Remplir(g, ligne, Theme.FondDoux);
                Theme.AnneauFocus(g, ligne);
            }
            Theme.Texte_(g, "QUANTITE", Theme.PetiteGras, Theme.Doux, marge + 5, y + 9);

            int taille = 28;
            Rectangle moins = new Rectangle(ligne.Right - 3 * taille - 10, y + 1,
                                            taille, taille);
            BoutonZone(g, moins, "moins", "-", Theme.FondDoux, Theme.Texte, Theme.Grande,
                       false);
            Rectangle compteur = new Rectangle(moins.Right + 4, y + 1, taille, taille);
            Theme.TexteCentre(g, _qte.ToString(), Theme.Grande, Theme.Texte, compteur,
                              y + 7);
            Rectangle plus = new Rectangle(compteur.Right + 4, y + 1, taille, taille);
            BoutonZone(g, plus, "plus", "+", Theme.FondDoux, Theme.Texte, Theme.Grande,
                       false);
        }
    }
}
