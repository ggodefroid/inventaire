using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Inventaire
{
    /// <summary>
    /// Base commune aux ecrans : un seul controle, peint entierement a la main.
    ///
    /// Les zones tactiles sont declarees pendant le rendu, donc jamais
    /// desynchronisees de ce qui est affiche : une mise en page calculee en
    /// fonction de la longueur d'un nom de produit reste cliquable au bon
    /// endroit sans table de correspondance a tenir a jour.
    /// </summary>
    internal abstract class Ecran : Control
    {
        protected sealed class Zone
        {
            public Rectangle R;
            public string Id;

            public Zone(Rectangle r, string id)
            {
                R = r;
                Id = id;
            }
        }

        public const int HauteurBandeau = 22;
        /// <summary>Hauteur du bandeau de message, quand il y en a un.</summary>
        public const int HauteurPied = 18;

        protected readonly Fenetre Fenetre;
        protected readonly List<Zone> Zones = new List<Zone>();
        private readonly Lecture _lecture = new Lecture();
        private readonly List<string> _focusables = new List<string>();
        private string _focus;
        private Bitmap _tampon;
        private Graphics _dessin;
        private string _enfoncee;

        protected Ecran(Fenetre fenetre)
        {
            Fenetre = fenetre;
            BackColor = Theme.Fond;
        }

        protected Reglages Reglages { get { return Fenetre.Reglages; } }
        protected Api Api { get { return Fenetre.Api; } }

        /// <summary>
        /// Date du jour selon le serveur. Passer par une propriete plutot que
        /// de lire Fenetre.Aujourdhui directement n'est pas cosmetique :
        /// Control derive de MarshalByRefObject, et appeler une methode sur un
        /// champ de type valeur d'une telle classe travaille sur une copie
        /// implicite (CS1690). La propriete rend cette copie explicite.
        /// </summary>
        protected DateTime Aujourdhui { get { return Fenetre.Aujourdhui; } }

        /// <summary>
        /// Derniere ligne utilisable, bord de l'ecran compris.
        ///
        /// Le bandeau de message ne reserve plus sa bande : il ne parait que
        /// quelques secondes apres une action, et se pose alors par-dessus. Il
        /// aurait ete dommage de laisser dix-huit pixels vides en permanence
        /// sur trois cent vingt -- le haut de l'ecran, lui, est occupe bord a
        /// bord.
        /// </summary>
        protected int BasUtile { get { return Height; } }

        // ------------------------------------------------------ cycle de vie

        /// <summary>Appele a chaque fois que l'ecran devient visible.</summary>
        public virtual void Entrer(object argument) { }

        public virtual void Sortir() { }

        /// <summary>
        /// true pour les quatre ecrans ou l'on se pose : l'accueil, le frigo,
        /// les courses et les reglages. Ce sont les destinations des touches
        /// de fonction, et les seules dont on ne « revient » pas -- on y va.
        /// </summary>
        public virtual bool EstRacine { get { return false; } }

        /// <summary>
        /// La fleche du bandeau, et Echap.
        ///
        /// Un sous-ecran rend la racine d'ou l'on vient : la fiche d'un
        /// article ouverte depuis « Tout le frigo » rend la liste, la meme
        /// fiche ouverte apres un bip rend l'accueil. Sans cela, consulter un
        /// article en parcourant le frigo ferait perdre sa place dans la
        /// liste a chaque coup d'oeil.
        ///
        /// Une racine, elle, remonte a l'accueil : c'est le seul cran
        /// au-dessus d'elle.
        /// </summary>
        public virtual void Retour()
        {
            Fenetre.Aller(EstRacine ? Fenetre.Scan : Fenetre.Racine, null);
        }

        // ----------------------------------------------------------- clavier

        /// <summary>Touche de commande. Retourne true si elle est consommee.</summary>
        public virtual bool Touche(Keys touche)
        {
            return false;
        }

        /// <summary>
        /// false pendant une saisie qui serait perdue : la fenetre garde alors
        /// ses touches de fonction pour elle.
        /// </summary>
        public virtual bool ToucheGlobaleAutorisee { get { return true; } }

        /// <summary>
        /// true : la zone de saisie accumule elle-meme, comme un bloc-notes,
        /// et <see cref="Lecture"/> decide quand la lire.
        /// false : les caracteres arrivent bruts dans <see cref="Tape"/>, pour
        /// les ecrans ou chaque chiffre compte (saisie de date, adresse du
        /// serveur) et ou aucune rafale n'est attendue.
        /// </summary>
        public virtual bool LectureActive { get { return true; } }

        /// <summary>true si un caractere isole doit devenir un raccourci.</summary>
        protected virtual bool RaccourcisActifs { get { return false; } }

        /// <summary>Caractere brut, pour les ecrans de saisie.</summary>
        protected virtual void Tape(char c) { }

        /// <summary>Touche isolee, une fois la temporisation ecoulee.</summary>
        protected virtual void Raccourci(char c) { }

        /// <summary>Code-barres complet.</summary>
        protected virtual void CodeLu(string code) { }

        /// <summary>Entree pressee sans rien en tampon : l'action principale.</summary>
        protected virtual bool Valider() { return false; }

        protected string Frappe { get { return _lecture.Tampon; } }

        /// <summary>Caractere brut, pour les ecrans de saisie uniquement.</summary>
        public void Caractere(char c)
        {
            if (c >= ' ')
                Tape(c);
        }

        /// <summary>
        /// Contenu de la zone de saisie, releve par la fenetre a chaque
        /// battement. C'est la seule source du code-barres : on lit ce que le
        /// controle d'edition a reellement accumule, jamais les evenements
        /// clavier un par un.
        /// </summary>
        public void ObserverPuits(string texte)
        {
            string avant = _lecture.Tampon;
            _lecture.Observer(texte);
            if (_lecture.Tampon != avant)
                Invalidate();
        }

        /// <summary>
        /// Traitement clavier commun. A appeler en tete du <see cref="Touche"/>
        /// des ecrans qui utilisent la lecture.
        /// </summary>
        protected bool ToucheBase(Keys touche)
        {
            if (!LectureActive)
                return false;
            if (touche == Keys.Enter)
            {
                // On ne lit surtout pas le tampon ici : les derniers WM_CHAR de
                // la rafale peuvent encore etre dans la file. On note seulement
                // que la rafale se termine ; la lecture aura lieu au battement
                // suivant, une fois la file vidée.
                _lecture.Entree();
                return true;
            }
            if (touche == Keys.Escape && !_lecture.Vide)
            {
                EffacerFrappe();
                return true;
            }
            // Retour arriere volontairement non consomme : la zone de saisie
            // efface son dernier caractere elle-meme, et le releve suivant en
            // rendra compte.
            return false;
        }

        private void Conclure(string tampon)
        {
            Fenetre.ViderPuits();
            Invalidate();
            if (tampon.Length == 0)
            {
                Valider();
                return;
            }
            if (RaccourcisActifs && tampon.Length == 1)
            {
                Sons.Jouer(Sons.Clic);
                Raccourci(tampon[0]);
                return;
            }
            Sons.Jouer(Sons.Bip);       // le code-barres est entier
            CodeLu(tampon);
        }

        /// <summary>Appele a chaque battement de la fenetre (environ 90 ms).</summary>
        public virtual void Battre()
        {
            if (!LectureActive)
                return;
            string clos;
            if (_lecture.Battement(RaccourcisActifs, out clos))
                Conclure(clos);
        }

        protected void EffacerFrappe()
        {
            _lecture.Effacer();
            Fenetre.ViderPuits();
            Invalidate();
        }


        // ------------------------------------------------------------ rendu

        protected abstract void Peindre(Graphics g, Rectangle zone);

        /// <summary>
        /// Bandeau superieur : retour, titre, heure et batterie.
        ///
        /// L'heure et la charge sont a la meme place sur tous les ecrans. Sur
        /// un terminal qu'on tient en main une heure durant dans une cuisine,
        /// ce sont les deux informations qu'on cherche sans y penser.
        /// </summary>
        protected void PeindreBandeau(Graphics g, string titre, bool fleche)
        {
            Rectangle bande = new Rectangle(0, 0, Width, HauteurBandeau);
            Theme.Remplir(g, bande, Theme.Barre);

            int x = 4;
            if (fleche)
            {
                Theme.Texte_(g, "<", Theme.Grande, Theme.SurBarre, x + 2, 1);
                Zones.Add(new Zone(new Rectangle(0, 0, 26, HauteurBandeau), "retour"));
                x = 22;
            }

            int droite = PeindreEtat(g);
            Theme.Texte_(g, Theme.Tronquer(g, titre, Theme.NormaleGras, droite - x - 6),
                         Theme.NormaleGras, Theme.SurBarre, x, 4);
        }

        /// <summary>Heure et batterie, calees a droite. Retourne leur abscisse.</summary>
        private int PeindreEtat(Graphics g)
        {
            string heure = Fenetre.TexteHeure();
            bool secteur;
            int charge = Fenetre.Batterie(out secteur);

            int largeurHeure = Theme.Largeur(g, heure, Theme.PetiteGras);
            string pourcent = charge >= 0 ? charge + "%" : "";
            int largeurPourcent = Theme.Largeur(g, pourcent, Theme.Minuscule);
            int largeurJauge = charge >= 0 ? 17 : 0;
            int total = largeurHeure + (largeurJauge > 0 ? 5 + largeurJauge + 1 : 0)
                        + largeurPourcent;

            int x = Width - 4 - total;
            Theme.Texte_(g, heure, Theme.PetiteGras, Theme.SurBarre, x, 5);
            if (charge >= 0)
            {
                PeindreJauge(g, new Rectangle(x + largeurHeure + 5, 6, 15, 9),
                             charge, secteur);
                Theme.Texte_(g, pourcent, Theme.Minuscule, Theme.SurBarre,
                             x + largeurHeure + 5 + largeurJauge + 1, 7);
            }
            return x;
        }

        private static void PeindreJauge(Graphics g, Rectangle r, int charge, bool secteur)
        {
            Theme.Cadre(g, r, Theme.SurBarre);
            g.DrawLine(Theme.C(Theme.SurBarre), r.Right, r.Y + 2, r.Right, r.Bottom - 3);
            int plein = (r.Width - 4) * Math.Max(0, Math.Min(100, charge)) / 100;
            if (plein > 0)
            {
                Color teinte = secteur ? Color.FromArgb(140, 220, 255)
                    : charge <= 15 ? Color.FromArgb(255, 150, 140)
                    : charge <= 35 ? Color.FromArgb(255, 215, 130)
                    : Color.White;
                Theme.Remplir(g, new Rectangle(r.X + 2, r.Y + 2, plein, r.Height - 4),
                              teinte);
            }
        }

        /// <summary>
        /// Bandeau du bas : attente reseau, ou dernier message. C'est le seul
        /// endroit ou l'application dit ce qui se passe, il est donc toujours
        /// au meme endroit -- pose par-dessus le contenu, et seulement quand
        /// il a quelque chose a dire.
        /// </summary>
        protected void PeindrePied(Graphics g)
        {
            Rectangle bande = new Rectangle(0, Height - HauteurPied, Width, HauteurPied);
            if (Fenetre.Occupe)
            {
                Theme.Remplir(g, bande, Theme.Bleu);
                Theme.TexteCentre(g, Fenetre.LibelleOccupe + " " + Fenetre.Sablier(),
                                  Theme.Petite, Color.White, bande, bande.Y + 3);
                return;
            }
            if (Fenetre.Message.Length == 0)
                return;
            Theme.Remplir(g, bande, Fenetre.MessageOk ? Theme.Vert : Theme.Rouge);
            Theme.TexteCentre(g, Theme.Tronquer(g, Fenetre.Message, Theme.Petite,
                                                Width - 8),
                              Theme.Petite, Color.White, bande, bande.Y + 3);
        }

        /// <summary>Bandeau discret montrant le code-barres en cours de lecture.</summary>
        protected void PeindreFrappe(Graphics g)
        {
            if (Frappe.Length < 2)
                return;
            int h = 20;
            Rectangle bande = new Rectangle(0, Height - HauteurPied - h, Width, h);
            Theme.Remplir(g, bande, Theme.Texte);
            Theme.TexteCentre(g, "lecture  " + Frappe, Theme.PetiteGras, Color.White,
                              bande, bande.Y + 4);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 0 || Height <= 0)
                return;
            // Double tampon manuel : le CF 2.0 n'a ni DoubleBuffered ni SetStyle,
            // et peindre directement fait clignoter tout l'ecran a chaque touche.
            if (_tampon == null || _tampon.Width != Width || _tampon.Height != Height)
            {
                if (_dessin != null) _dessin.Dispose();
                if (_tampon != null) _tampon.Dispose();
                _tampon = new Bitmap(Width, Height);
                _dessin = Graphics.FromImage(_tampon);
            }
            Zones.Clear();
            _focusables.Clear();
            _dessin.Clear(Theme.Fond);
            Peindre(_dessin, new Rectangle(0, 0, Width, Height));
            PeindrePied(_dessin);
            e.Graphics.DrawImage(_tampon, 0, 0);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Le tampon couvre toute la surface : effacer d'abord ne ferait
            // que doubler le travail et ajouter un clignotement.
        }

        // ------------------------------------------------------------ tactile

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Fenetre.Activite();
            string id = Trouver(e.X, e.Y);
            if (id != null)
            {
                _enfoncee = id;
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            string id = _enfoncee;
            _enfoncee = null;
            if (id != null)
            {
                Invalidate();
                if (id == Trouver(e.X, e.Y))
                {
                    if (id == "retour")
                        Retour();
                    else
                        Activer(id);
                }
            }
            base.OnMouseUp(e);
        }

        /// <summary>Zone tactile relachee.</summary>
        protected virtual void Activer(string id) { }

        private string Trouver(int x, int y)
        {
            // Parcours a l'envers : les zones declarees en dernier sont dessinees
            // par-dessus, elles doivent donc gagner.
            for (int i = Zones.Count - 1; i >= 0; i--)
            {
                if (Zones[i].R.Contains(x, y))
                    return Zones[i].Id;
            }
            return null;
        }

        protected bool Enfoncee(string id)
        {
            return _enfoncee == id;
        }

        /// <summary>Declare une zone et dessine son bouton dans la foulee.</summary>
        protected void BoutonZone(Graphics g, Rectangle r, string id, string libelle,
                                  Color fond, Color encre, Font police)
        {
            BoutonZone(g, r, id, libelle, fond, encre, police, true);
        }

        /// <param name="focalisable">
        /// false pour les boutons qui ne font pas partie du cycle des fleches :
        /// les fleches de defilement, ou les actions d'une ligne de liste, dont
        /// l'ecran gere lui-meme le parcours.
        /// </param>
        protected void BoutonZone(Graphics g, Rectangle r, string id, string libelle,
                                  Color fond, Color encre, Font police,
                                  bool focalisable)
        {
            Zones.Add(new Zone(r, id));
            if (focalisable)
                _focusables.Add(id);
            Theme.Bouton(g, r, libelle, fond, encre, police, Enfoncee(id));
            if (_focus == id)
                Theme.AnneauFocus(g, r);
        }

        // ------------------------------------------------------ focus clavier

        /// <summary>
        /// Bouton actuellement sous le curseur clavier.
        ///
        /// Le terminal a un ecran tactile, mais il se tient d'une main, la
        /// gachette sous l'index : atteindre un bouton au stylet demande de
        /// poser l'appareil. Tout ce qui est cliquable doit donc etre
        /// atteignable aux fleches, et l'anneau de focus dit ou l'on est.
        ///
        /// La liste des boutons est reconstituee a chaque rendu, dans l'ordre
        /// ou ils sont peints : le parcours suit donc toujours l'ordre visuel,
        /// sans table a tenir a jour.
        /// </summary>
        protected string Curseur
        {
            get { return _focus; }
            set { _focus = value; }
        }

        /// <summary>Deplace le curseur d'un cran. true s'il a bouge.</summary>
        protected bool CurseurDeplacer(int delta)
        {
            if (_focusables.Count == 0)
                return false;
            Sons.Jouer(Sons.Clic);
            int position = _focusables.IndexOf(_focus);
            if (position < 0)
                position = delta > 0 ? -1 : 0;
            position = (position + delta + _focusables.Count) % _focusables.Count;
            _focus = _focusables[position];
            Invalidate();
            return true;
        }

        /// <summary>Active le bouton sous le curseur. false s'il n'y en a pas.</summary>
        protected bool CurseurActiver()
        {
            if (_focus == null || _focusables.IndexOf(_focus) < 0)
                return false;
            Activer(_focus);
            return true;
        }

        /// <summary>Pose le curseur sur un bouton donne, s'il existe encore.</summary>
        protected void PoserCurseur(string id)
        {
            _focus = id;
        }

        protected override void Dispose(bool liberer)
        {
            if (liberer)
            {
                if (_dessin != null) { _dessin.Dispose(); _dessin = null; }
                if (_tampon != null) { _tampon.Dispose(); _tampon = null; }
            }
            base.Dispose(liberer);
        }
    }
}
