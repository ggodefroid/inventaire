using System;
using System.Drawing;
using System.Windows.Forms;

namespace Inventaire
{
    /// <summary>
    /// Ce que le terminal affiche quand on le pose et qu'on l'oublie.
    ///
    /// Cinquante-cinq economiseurs, tires sans remise et relayes toutes les
    /// trente secondes, puis l'ecran noir. Ils ne servent a rien d'utile, et
    /// c'est assume : un terminal de 2007 pose sur un plan de travail merite
    /// mieux qu'une image figee, et un ecran fixe use les afficheurs.
    ///
    /// Le catalogue est une traversee de ce que montraient les machines avant
    /// celle-ci : les effets de demoscene qu'on ecrivait pour l'Amiga et le
    /// PC, les bornes d'arcade en mode attraction, et les ecrans que les
    /// systemes affichaient en travaillant -- defragmenteur, ScanDisk, ecran
    /// bleu, vidage hexadecimal. Le nom de chacun s'affiche deux secondes en
    /// bas de l'ecran, faute de quoi la moitie des references passeraient
    /// inapercues.
    ///
    /// Le moteur ne sait rien d'aucun effet : il tient une table, tire dedans,
    /// appelle `Avancer` puis `Peindre`. Ajouter un economiseur, c'est ajouter
    /// une classe et une ligne dans `Catalogue`. Les contraintes de calcul
    /// -- aucun flottant, aucun pixel individuel -- sont expliquees en tete
    /// d'Effet.cs, et elles sont la vraie raison de la forme du code.
    /// </summary>
    internal sealed class Veille : Control
    {
        /// <summary>Duree d'un economiseur avant de passer au suivant.</summary>
        private const int DureeEffetMs = 30000;
        /// <summary>Duree d'affichage du nom de l'effet.</summary>
        private const int DureeNomMs = 2200;

        private readonly Random _hasard = new Random();
        private Bitmap _tampon;
        private Graphics _dessin;

        private Effet[] _catalogue;
        private int[] _ordre;
        private int _rang;
        private Effet _effet;
        private int _debutEffet;
        private bool _noir;

        public Veille()
        {
            BackColor = Color.Black;
            Visible = false;
            _catalogue = Catalogue();
            _ordre = new int[_catalogue.Length];
            for (int i = 0; i < _ordre.Length; i++) _ordre[i] = i;
            Battre();
        }

        /// <summary>
        /// Les cinquante-cinq economiseurs. L'ordre n'a pas d'importance : ils
        /// sont melanges a chaque tour.
        /// </summary>
        private static Effet[] Catalogue()
        {
            return new Effet[] {
                // -- les cinq premiers, d'avant la table
                new Pluie(), new Etoiles(), new Logo(), new Lignes(), new Tuyaux(),
                // -- demoscene
                new Plasma(), new Feu(), new Tunnel(), new Rotozoom(), new Metaballs(),
                new Lissajous(), new Spirographe(), new Lorenz(), new Sierpinski(),
                new Mandelbrot(), new Julia(), new Copper(), new Scroller(), new Moire(),
                new Boids(), new Vie(), new Regle30(), new Sable(), new Ondes(), new Lave(),
                // -- bornes d'arcade, en mode attraction
                new Pong(), new CasseBriques(), new Serpent(), new Tetris(),
                new Envahisseurs(), new Glouton(), new Asteroides(), new Simon(),
                new Demineur(), new Labyrinthe(),
                // -- ecrans de machine
                new EcranBleu(), new InviteDos(), new Defragmenteur(), new Scandisk(),
                new Installation(), new JournalDemarrage(), new Hexa(), new TestMemoire(),
                new FenetresVolantes(), new Oscilloscope(), new Vumetre(), new Egaliseur(),
                new Mire(), new Neige(), new Horloge(), new HorlogeVolets(),
                new HorlogeBinaire(), new Nixie(), new CodeBarres(), new Telex(),
            };
        }

        /// <summary>Nombre d'economiseurs au catalogue. Lu par l'ecran Reglages.</summary>
        public int Nombre { get { return _catalogue.Length; } }

        /// <summary>Entre en veille sur un economiseur tire au hasard.</summary>
        public void Demarrer()
        {
            _noir = false;
            Battre();
            Choisir();
        }

        public void Noircir()
        {
            if (_noir) return;
            _noir = true;
            Invalidate();
        }

        public bool Noire { get { return _noir; } }

        /// <summary>
        /// Melange l'ordre de passage. Tirer au hasard a chaque fois ferait
        /// revenir le meme effet deux fois de suite une fois sur cinquante-cinq ;
        /// une permutation garantit qu'on voit tout le catalogue avant d'en
        /// revoir un seul.
        /// </summary>
        private void Battre()
        {
            for (int i = _ordre.Length - 1; i > 0; i--)
            {
                int j = _hasard.Next(i + 1);
                int t = _ordre[i]; _ordre[i] = _ordre[j]; _ordre[j] = t;
            }
            _rang = 0;
        }

        private void Choisir()
        {
            if (_rang >= _ordre.Length) Battre();
            _effet = _catalogue[_ordre[_rang]];
            _rang++;
            _effet.Poser(Width > 0 ? Width : 240, Height > 0 ? Height : 320, _hasard);
            _debutEffet = Environment.TickCount;
            Invalidate();
        }

        // ------------------------------------------------------------ animation

        /// <summary>Avance d'une image. Appelee par le battement de la fenetre.</summary>
        public void Animer()
        {
            if (_noir) return;
            if (_effet == null) { Choisir(); return; }
            if (Environment.TickCount - _debutEffet > DureeEffetMs)
            {
                Choisir();
                return;
            }
            try
            {
                _effet.Avancer();
            }
            catch (Exception)
            {
                // Un economiseur qui plante ne doit pas emporter l'application :
                // on passe au suivant et personne ne s'en apercoit.
                Choisir();
            }
            Invalidate();
        }

        // --------------------------------------------------------------- rendu

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Le tampon couvre tout : effacer d'abord ne ferait que clignoter.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 0 || Height <= 0) return;
            if (_tampon == null || _tampon.Width != Width || _tampon.Height != Height)
            {
                if (_dessin != null) _dessin.Dispose();
                if (_tampon != null) _tampon.Dispose();
                _tampon = new Bitmap(Width, Height);
                _dessin = Graphics.FromImage(_tampon);
                if (_effet != null) _effet.Poser(Width, Height, _hasard);
            }
            _dessin.Clear(Color.Black);
            if (!_noir && _effet != null)
            {
                try
                {
                    _effet.Peindre(_dessin);
                    PeindreNom(_dessin);
                }
                catch (Exception)
                {
                    _dessin.Clear(Color.Black);
                }
            }
            e.Graphics.DrawImage(_tampon, 0, 0);
        }

        private static Color Gris(int n)
        {
            if (n < 0) n = 0; else if (n > 255) n = 255;
            return Color.FromArgb(n, n, n);
        }

        /// <summary>Le nom de l'effet, deux secondes, puis il s'efface.</summary>
        private void PeindreNom(Graphics g)
        {
            int age = Environment.TickCount - _debutEffet;
            if (age > DureeNomMs) return;
            int reste = age > DureeNomMs - 600 ? (DureeNomMs - age) * 255 / 600 : 255;
            if (reste < 0) reste = 0;
            string texte = _effet.Nom;
            int largeur = (int)g.MeasureString(texte, Theme.Minuscule).Width;
            int x = (Width - largeur) / 2, y = Height - 16;
            // Pas de canal alpha sous Compact Framework : le cartouche
            // s'eteint vers le noir, qui est deja le fond. Le resultat est le
            // meme qu'une transparence, sans en avoir besoin.
            g.FillRectangle(Theme.P(Gris(reste / 8)), x - 6, y - 2, largeur + 12, 14);
            g.DrawString(texte, Theme.Minuscule,
                         Theme.P(Color.FromArgb(reste, reste, reste)), x, y);
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
