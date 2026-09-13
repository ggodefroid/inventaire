using System;
using System.Drawing;
using System.Windows.Forms;

namespace Inventaire
{
    /// <summary>
    /// Reglages du terminal : ou joindre le serveur, et comment.
    ///
    /// L'adresse se tape au pave numerique. Le point decimal manque sur
    /// certains claviers du Skorpio, ou demande une touche de fonction : une
    /// touche « . » a l'ecran evite d'avoir a le savoir. Les valeurs ne sont
    /// ecrites dans inventaire.ini qu'a l'enregistrement, mais « Tester » les
    /// applique d'abord, sinon on testerait l'ancienne adresse.
    /// </summary>
    internal sealed class EcranConfiguration : Ecran
    {
        private const int Serveur = 0, Port = 1, Delai = 2, Taille = 3,
                          Photos = 4, Bip = 5, Nombre = 6;
        // Les trois actions prolongent la liste des champs : les fleches haut
        // et bas parcourent l'ecran entier, formulaire et boutons compris,
        // et Entree execute ce qui est sous le curseur.
        private const int ActionTester = 6, ActionEnregistrer = 7,
                          ActionMaj = 8, ActionQuitter = 9, Positions = 10;
        private static readonly string[] Actions =
            { "tester", "enregistrer", "maj", "quitter" };

        private static readonly string[] Etiquettes =
            { "Serveur", "Port", "Delai (ms)", "Photo (px)", "Photos", "Sons" };
        /// <summary>Les trois niveaux sonores, dans l'ordre du cycle.</summary>
        private static readonly string[] NiveauxSons = { "non", "essentiel", "tout" };

        private readonly string[] _valeurs = new string[Nombre];
        private int _champ;
        private string _diagnostic = "";
        private bool _diagnosticOk;

        public EcranConfiguration(Fenetre fenetre) : base(fenetre) { }

        // Les caracteres vont dans le champ selectionne, tels quels.
        public override bool LectureActive { get { return false; } }
        public override bool EstRacine { get { return true; } }

        public override void Entrer(object argument)
        {
            _valeurs[Serveur] = Reglages.Hote;
            _valeurs[Port] = Reglages.Port.ToString();
            _valeurs[Delai] = Reglages.DelaiMs.ToString();
            _valeurs[Taille] = Reglages.TailleImage.ToString();
            _valeurs[Photos] = Reglages.Photos ? "1" : "0";
            _valeurs[Bip] = Reglages.Bip.ToString();
            _champ = Serveur;
            _diagnostic = "";
        }

        private static bool EstBooleen(int champ)
        {
            return champ == Photos;
        }

        /// <summary>Le niveau sonore n'est pas un oui/non : il tourne sur trois crans.</summary>
        private static bool EstCycle(int champ)
        {
            return champ == Bip;
        }

        private static bool EstChoix(int champ)
        {
            return EstBooleen(champ) || EstCycle(champ);
        }

        private bool SurAction { get { return _champ >= Nombre; } }

        // --------------------------------------------------------- clavier

        public override bool Touche(Keys touche)
        {
            switch (touche)
            {
                case Keys.Up:
                    _champ = (_champ + Positions - 1) % Positions;
                    Sons.Jouer(Sons.Clic);
                    Invalidate();
                    return true;
                case Keys.Down:
                case Keys.Tab:
                    _champ = (_champ + 1) % Positions;
                    Sons.Jouer(Sons.Clic);
                    Invalidate();
                    return true;
                case Keys.Left:
                case Keys.Right:
                    // Sur une case a cocher ou un cran, les fleches laterales
                    // changent la valeur ; ailleurs elles ne feraient rien.
                    if (EstChoix(_champ))
                    {
                        Basculer(touche == Keys.Left ? -1 : 1);
                        return true;
                    }
                    return false;
                case Keys.Enter:
                    if (SurAction)
                        Activer(Actions[_champ - Nombre]);
                    else
                        _champ++;
                    Invalidate();
                    return true;
                case Keys.Escape:
                    Retour();
                    return true;
                case Keys.Back:
                    Effacer();
                    return true;
                case Keys.Space:
                    if (EstChoix(_champ))
                    {
                        Basculer(1);
                        return true;
                    }
                    return false;
            }
            return false;
        }

        protected override void Tape(char c)
        {
            if (SurAction)
                return;
            if (EstChoix(_champ))
            {
                Basculer(1);
                return;
            }
            bool numerique = _champ != Serveur;
            if (numerique && (c < '0' || c > '9'))
                return;
            if (_valeurs[_champ].Length >= (numerique ? 6 : 40))
                return;
            _valeurs[_champ] += c;
            Invalidate();
        }

        private void Effacer()
        {
            if (SurAction)
                return;
            if (EstChoix(_champ))
            {
                Basculer(1);
                return;
            }
            string valeur = _valeurs[_champ];
            if (valeur.Length > 0)
            {
                _valeurs[_champ] = valeur.Substring(0, valeur.Length - 1);
                Invalidate();
            }
        }

        private void Basculer(int sens)
        {
            if (EstCycle(_champ))
            {
                int niveau = Borne(_valeurs[_champ], 0, 2, 0) + sens;
                _valeurs[_champ] = ((niveau + 3) % 3).ToString();
                // Applique tout de suite : on entend le reglage qu'on regle.
                Sons.Niveau(Borne(_valeurs[_champ], 0, 2, 0));
                Sons.Jouer(Sons.Succes);
            }
            else
            {
                _valeurs[_champ] = _valeurs[_champ] == "1" ? "0" : "1";
                Sons.Jouer(Sons.Clic);
            }
            Invalidate();
        }

        protected override void Activer(string id)
        {
            if (id == "point")
            {
                if (_champ == Serveur && _valeurs[Serveur].Length < 40)
                {
                    _valeurs[Serveur] += ".";
                    Invalidate();
                }
                return;
            }
            if (id == "effacer") { Effacer(); return; }
            if (id == "tester") { Tester(); return; }
            if (id == "enregistrer") { Enregistrer(); return; }
            if (id == "retour") { Retour(); return; }
            if (id == "maj") { Fenetre.Aller(Fenetre.Maj, null); return; }
            if (id == "quitter") { Fenetre.Quitter(); return; }
            if (id.Length > 6 && id.Substring(0, 6) == "champ.")
            {
                int cible = int.Parse(id.Substring(6));
                if (cible == _champ && EstChoix(cible))
                    Basculer(1);
                else
                {
                    _champ = cible;
                    Invalidate();
                }
            }
        }

        // --------------------------------------------------------- actions

        /// <summary>Reporte la saisie dans les reglages vivants. Retourne le souci.</summary>
        private string Appliquer()
        {
            string hote = _valeurs[Serveur].Trim();
            if (hote.Length == 0)
                return "adresse du serveur vide";
            Reglages.Hote = hote;
            Reglages.Port = Borne(_valeurs[Port], 1, 65535, Reglages.Port);
            Reglages.DelaiMs = Borne(_valeurs[Delai], 1000, 60000, Reglages.DelaiMs);
            Reglages.TailleImage = Borne(_valeurs[Taille], 48, 96, Reglages.TailleImage);
            Reglages.Photos = _valeurs[Photos] == "1";
            Reglages.Bip = Borne(_valeurs[Bip], 0, 2, Reglages.Bip);
            Sons.Niveau(Reglages.Bip);
            return null;
        }

        private static int Borne(string texte, int mini, int maxi, int defaut)
        {
            int valeur;
            try { valeur = int.Parse(texte.Trim()); }
            catch (Exception) { return defaut; }
            if (valeur < mini) return mini;
            if (valeur > maxi) return maxi;
            return valeur;
        }

        private void Tester()
        {
            string souci = Appliquer();
            if (souci != null)
            {
                Fenetre.Dire(souci, false);
                return;
            }
            _diagnostic = "test en cours...";
            _diagnosticOk = false;
            Invalidate();
            Fenetre.Appeler("test", new FonctionReseau(delegate()
            {
                return Api.Ping();
            }), new SuiteReseau(delegate(Reponse reponse)
            {
                if (reponse.Joint && reponse.Ok)
                {
                    _diagnosticOk = true;
                    _diagnostic = "serveur " + reponse.Donnees.S("version") + " · "
                        + reponse.Millisecondes + " ms · "
                        + reponse.Donnees.S("compteurs.unites") + " unites";
                    if (!reponse.Donnees.B("images"))
                        _diagnostic = "sans photos (Pillow absent)";
                }
                else
                {
                    _diagnosticOk = false;
                    _diagnostic = reponse.Erreur.Length > 0 ? reponse.Erreur
                        : "pas de reponse";
                }
                Fenetre.Dire(_diagnostic, _diagnosticOk);
                Invalidate();
            }));
        }

        private void Enregistrer()
        {
            string souci = Appliquer();
            if (souci != null)
            {
                Fenetre.Dire(souci, false);
                return;
            }
            try
            {
                Reglages.Enregistrer();
                Fenetre.Dire("enregistre dans inventaire.ini", true);
            }
            catch (Exception ex)
            {
                // Dossier en lecture seule : frequent quand le programme est
                // lance depuis la carte memoire montee en protection.
                Fenetre.Dire("ecriture refusee : " + ex.Message, false);
            }
        }

        // ------------------------------------------------------------ rendu

        protected override void Peindre(Graphics g, Rectangle zone)
        {
            PeindreBandeau(g, "Reglages " + Programme.Version, true);

            int marge = 5;
            int largeur = Width - 2 * marge;
            int y = HauteurBandeau + 4;
            int hauteur = 25;

            for (int i = 0; i < Nombre; i++)
            {
                Rectangle r = new Rectangle(marge, y, largeur, hauteur - 2);
                Zones.Add(new Zone(r, "champ." + i));
                bool actif = i == _champ && !SurAction;
                if (actif)
                {
                    Theme.Remplir(g, r, Theme.FondDoux);
                    Theme.Cadre(g, r, Theme.Barre);
                }
                Theme.Texte_(g, Etiquettes[i], Theme.Petite, Theme.Doux, r.X + 5, r.Y + 5);

                string valeur;
                Color encre = Theme.Texte;
                if (EstCycle(i))
                {
                    int niveau = Borne(_valeurs[i], 0, 2, 0);
                    valeur = NiveauxSons[niveau];
                    encre = niveau == 0 ? Theme.Doux
                        : niveau == 2 ? Theme.Vert : Theme.Texte;
                }
                else if (EstBooleen(i))
                {
                    bool oui = _valeurs[i] == "1";
                    valeur = oui ? "oui" : "non";
                    encre = oui ? Theme.Vert : Theme.Doux;
                }
                else
                {
                    valeur = _valeurs[i] + (actif ? "_" : "");
                }
                valeur = Theme.Tronquer(g, valeur, Theme.NormaleGras, largeur - 96);
                int l = Theme.Largeur(g, valeur, Theme.NormaleGras);
                Theme.Texte_(g, valeur, Theme.NormaleGras, encre, r.Right - l - 6, r.Y + 3);
                y += hauteur;
            }
            y += 3;

            // Clavier d'appoint pour l'adresse.
            int touche = (largeur - 6) / 2;
            BoutonZone(g, new Rectangle(marge, y, touche, 28), "point", ".",
                       _champ == Serveur ? Theme.FondDoux : Theme.Fond,
                       _champ == Serveur ? Theme.Texte : Theme.Trait, Theme.Grande,
                       false);
            BoutonZone(g, new Rectangle(marge + touche + 6, y, touche, 28), "effacer",
                       "<- effacer", Theme.FondDoux, Theme.Texte, Theme.Petite, false);
            y += 32;

            if (_diagnostic.Length > 0)
            {
                Theme.Texte_(g, Theme.Tronquer(g, _diagnostic, Theme.Minuscule, largeur),
                             Theme.Minuscule, _diagnosticOk ? Theme.Vert : Theme.Rouge,
                             marge, y);
            }
            y += 13;

            // Le curseur clavier suit la position courante : les trois boutons
            // ne sont donc pas dans le cycle automatique.
            PoserCurseur(SurAction ? Actions[_champ - Nombre] : null);
            int demi = (largeur - 6) / 2;
            BoutonZone(g, new Rectangle(marge, y, demi, 34), "tester", "Tester",
                       Theme.Bleu, Color.White, Theme.NormaleGras, false);
            BoutonZone(g, new Rectangle(marge + demi + 6, y, demi, 34), "enregistrer",
                       "Enregistrer", Theme.Vert, Color.White, Theme.NormaleGras, false);
            y += 38;

            // Pas de bouton « Retour » : la fleche du bandeau le fait deja.
            // Quitter reste ici, et nulle part ailleurs : c'est une action
            // qu'on ne doit pas declencher par megarde en rangeant des courses.
            int yBas = BasUtile - 3 - 30;
            if (y > yBas) yBas = y;
            BoutonZone(g, new Rectangle(marge, yBas, demi, 30), "maj",
                       "Mise a jour", Theme.FondDoux, Theme.Texte, Theme.Petite, false);
            BoutonZone(g, new Rectangle(marge + demi + 6, yBas, largeur - demi - 6, 30),
                       "quitter", "Quitter", Theme.FondDoux, Theme.Doux,
                       Theme.Petite, false);
        }
    }
}
