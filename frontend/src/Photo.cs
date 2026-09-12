using System;
using System.Drawing;
using System.IO;

namespace Inventaire
{
    /// <summary>
    /// Decodage de la vignette envoyee par le serveur.
    ///
    /// `new Bitmap(flux)` s'appuie sur les codecs d'image de l'image OS du
    /// terminal. Sur un Windows CE 5.0 assemble par un industriel, rien ne
    /// garantit qu'ils soient presents -- et leur absence se manifeste par une
    /// exception, pas par une image degradee.
    ///
    /// Le serveur envoyant deliberement un BMP 24 bits non compresse, on sait
    /// le relire nous-memes si besoin : l'entete fait 54 octets et les pixels
    /// suivent en clair. La photo s'affiche donc quelle que soit l'image OS.
    /// </summary>
    internal static class Photo
    {
        public static Bitmap Depuis(byte[] donnees, bool forcerMaison)
        {
            if (donnees == null || donnees.Length < 54)
                return null;
            if (!forcerMaison)
            {
                try
                {
                    using (MemoryStream flux = new MemoryStream(donnees))
                        return new Bitmap(flux);
                }
                catch (Exception)
                {
                    // Pas de codec : on relit le BMP a la main.
                }
            }
            try
            {
                return LireBmp24(donnees);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static int Lire32(byte[] d, int i)
        {
            return d[i] | (d[i + 1] << 8) | (d[i + 2] << 16) | (d[i + 3] << 24);
        }

        private static int Lire16(byte[] d, int i)
        {
            return d[i] | (d[i + 1] << 8);
        }

        private static Bitmap LireBmp24(byte[] d)
        {
            if (d[0] != (byte)'B' || d[1] != (byte)'M')
                return null;
            int debutPixels = Lire32(d, 10);
            int largeur = Lire32(d, 18);
            int hauteur = Lire32(d, 22);
            int bits = Lire16(d, 28);
            int compression = Lire32(d, 30);
            bool versLeHaut = hauteur > 0;          // hauteur negative = lignes en ordre direct
            if (hauteur < 0)
                hauteur = -hauteur;
            if (bits != 24 || compression != 0 || largeur <= 0 || hauteur <= 0
                || largeur > 480 || hauteur > 480)
                return null;

            int pas = ((largeur * 3 + 3) / 4) * 4;  // lignes alignees sur 4 octets
            if (debutPixels + pas * hauteur > d.Length)
                return null;

            Bitmap image = new Bitmap(largeur, hauteur);
            for (int y = 0; y < hauteur; y++)
            {
                int source = debutPixels + (versLeHaut ? (hauteur - 1 - y) : y) * pas;
                for (int x = 0; x < largeur; x++)
                {
                    int p = source + x * 3;
                    image.SetPixel(x, y, Color.FromArgb(d[p + 2], d[p + 1], d[p]));
                }
            }
            return image;
        }
    }
}
