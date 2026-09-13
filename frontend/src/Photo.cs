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
    /// Le serveur envoyant deliberement un BMP non compresse, on sait le relire
    /// nous-memes si besoin : l'entete fait 54 octets, une palette suit
    /// eventuellement, puis les pixels en clair. La photo s'affiche donc quelle
    /// que soit l'image OS.
    ///
    /// Deux profondeurs sont acceptees. Le serveur envoie du **8 bits
    /// palettise** par defaut -- le tiers des octets du 24 bits, pour une perte
    /// invisible a cette taille, et c'est ce tiers qui fait la difference entre
    /// une demi-seconde d'attente et un affichage immediat sur une radio
    /// 802.11b. Le 24 bits reste lu tel quel.
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
                return LireBmp(donnees);
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

        private static Bitmap LireBmp(byte[] d)
        {
            if (d[0] != (byte)'B' || d[1] != (byte)'M')
                return null;
            int debutPixels = Lire32(d, 10);
            int tailleEntete = Lire32(d, 14);
            int largeur = Lire32(d, 18);
            int hauteur = Lire32(d, 22);
            int bits = Lire16(d, 28);
            int compression = Lire32(d, 30);
            bool versLeHaut = hauteur > 0;          // hauteur negative = lignes en ordre direct
            if (hauteur < 0)
                hauteur = -hauteur;
            if (compression != 0 || largeur <= 0 || hauteur <= 0
                || largeur > 480 || hauteur > 480)
                return null;
            if (bits != 24 && bits != 8)
                return null;

            // Lignes alignees sur quatre octets, quelle que soit la profondeur.
            int octetsParPixel = bits / 8;
            int pas = ((largeur * octetsParPixel + 3) / 4) * 4;
            if (debutPixels + pas * hauteur > d.Length)
                return null;

            // La palette suit l'entete d'information et precede les pixels.
            // On la convertit une fois pour toutes : refabriquer une Color par
            // pixel couterait plus cher que la lecture elle-meme.
            Color[] palette = null;
            if (bits == 8)
            {
                int debutPalette = 14 + tailleEntete;
                int couleurs = Lire32(d, 46);       // biClrUsed, 0 = les 256
                if (couleurs <= 0 || couleurs > 256)
                    couleurs = 256;
                if (debutPalette + couleurs * 4 > d.Length)
                    return null;
                palette = new Color[256];
                for (int i = 0; i < couleurs; i++)
                {
                    int q = debutPalette + i * 4;   // BGRA
                    palette[i] = Color.FromArgb(d[q + 2], d[q + 1], d[q]);
                }
                for (int i = couleurs; i < 256; i++)
                    palette[i] = Color.Black;
            }

            Bitmap image = new Bitmap(largeur, hauteur);
            for (int y = 0; y < hauteur; y++)
            {
                int source = debutPixels + (versLeHaut ? (hauteur - 1 - y) : y) * pas;
                if (bits == 8)
                {
                    for (int x = 0; x < largeur; x++)
                        image.SetPixel(x, y, palette[d[source + x]]);
                }
                else
                {
                    for (int x = 0; x < largeur; x++)
                    {
                        int p = source + x * 3;
                        image.SetPixel(x, y, Color.FromArgb(d[p + 2], d[p + 1], d[p]));
                    }
                }
            }
            return image;
        }
    }
}
