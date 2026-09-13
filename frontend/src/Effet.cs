using System;
using System.Drawing;

namespace Inventaire
{
    /// <summary>
    /// Un economiseur d'ecran. Le moteur en tient une table et les relaie.
    ///
    /// Deux contraintes gouvernent tout ce qui est ecrit ici, et elles ne sont
    /// pas negociables sur un PXA270 a 520 MHz :
    ///
    /// **Pas de virgule flottante.** Ce processeur n'a pas d'unite flottante :
    /// chaque `double` est emule par la bibliotheque, a des centaines de
    /// cycles l'operation. Un sinus par pixel rendrait une image par seconde.
    /// Tout se calcule donc en entiers, avec les tables `Sin` et `Cos`
    /// ci-dessous -- 256 pas, amplitude 1024, ce qu'on faisait deja sur Amiga
    /// pour les memes raisons.
    ///
    /// **Pas de pixel individuel.** `Bitmap.SetPixel` verrouille l'image a
    /// chaque appel ; 76 800 appels par image sont hors de portee. Les effets
    /// qui couvrent l'ecran travaillent donc en blocs de quatre a huit pixels,
    /// peints en rectangles pleins. C'est aussi ce qui leur donne leur grain
    /// d'epoque, et on ne s'en plaindra pas.
    /// </summary>
    internal abstract class Effet
    {
        protected Random Hasard;
        /// <summary>Largeur et hauteur de l'ecran, en pixels.</summary>
        protected int L, H;
        /// <summary>Numero de l'image courante depuis le debut de l'effet.</summary>
        protected int T;

        /// <summary>Ce que le moteur affiche brievement en bas de l'ecran.</summary>
        public abstract string Nom { get; }

        public void Poser(int largeur, int hauteur, Random hasard)
        {
            L = largeur > 0 ? largeur : 240;
            H = hauteur > 0 ? hauteur : 320;
            Hasard = hasard;
            T = 0;
            Demarrer();
        }

        protected virtual void Demarrer() { }

        public virtual void Avancer() { T++; }

        public abstract void Peindre(Graphics g);

        // ------------------------------------------------- trigonometrie entiere

        /// <summary>Sinus x 1024, sur 256 pas d'angle.</summary>
        protected static readonly int[] Sin = ConstruireSin();

        private static int[] ConstruireSin()
        {
            int[] table = new int[256];
            for (int i = 0; i < 256; i++)
                table[i] = (int)(Math.Sin(i * 2 * Math.PI / 256) * 1024);
            return table;
        }

        protected static int S(int angle) { return Sin[angle & 255]; }
        protected static int C(int angle) { return Sin[(angle + 64) & 255]; }

        /// <summary>Racine carree entiere, par Newton. Aucun flottant.</summary>
        protected static int Racine(int n)
        {
            if (n <= 0) return 0;
            int x = n, y = (x + 1) / 2;
            while (y < x) { x = y; y = (x + n / x) / 2; }
            return x;
        }

        // ------------------------------------------------------------ couleurs

        /// <summary>
        /// Roue de teintes, en entiers : 0..255 fait le tour du cercle
        /// chromatique a saturation et luminosite pleines.
        /// </summary>
        protected static Color Roue(int teinte)
        {
            teinte &= 255;
            int secteur = teinte / 43;              // six secteurs de 43 pas
            int reste = (teinte - secteur * 43) * 6;
            int montant = reste, descendant = 255 - reste;
            switch (secteur)
            {
                case 0: return Color.FromArgb(255, montant, 0);
                case 1: return Color.FromArgb(descendant, 255, 0);
                case 2: return Color.FromArgb(0, 255, montant);
                case 3: return Color.FromArgb(0, descendant, 255);
                case 4: return Color.FromArgb(montant, 0, 255);
                default: return Color.FromArgb(255, 0, descendant);
            }
        }

        /// <summary>Gris, borne aux valeurs affichables.</summary>
        protected static Color Gris(int n)
        {
            if (n < 0) n = 0; else if (n > 255) n = 255;
            return Color.FromArgb(n, n, n);
        }

        protected static Color Fondu(Color a, Color b, int part, int total)
        {
            if (total <= 0) return a;
            if (part < 0) part = 0; else if (part > total) part = total;
            return Color.FromArgb(a.R + (b.R - a.R) * part / total,
                                  a.G + (b.G - a.G) * part / total,
                                  a.B + (b.B - a.B) * part / total);
        }

        // -------------------------------------------------------------- dessin

        protected static void Bloc(Graphics g, int x, int y, int cote, Color teinte)
        {
            g.FillRectangle(Theme.P(teinte), x, y, cote, cote);
        }

        protected void Fond(Graphics g, Color teinte)
        {
            g.FillRectangle(Theme.P(teinte), 0, 0, L, H);
        }

        /// <summary>Texte centre horizontalement.</summary>
        protected void Centre(Graphics g, string texte, Font police, Color teinte, int y)
        {
            int largeur = (int)g.MeasureString(texte, police).Width;
            g.DrawString(texte, police, Theme.P(teinte), (L - largeur) / 2, y);
        }
    }
}
