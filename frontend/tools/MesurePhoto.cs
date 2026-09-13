using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;

namespace Inventaire
{
    /// <summary>
    /// Chronometre le decodage d'une vignette, par les deux chemins.
    ///
    /// Le chemin normal passe par les codecs de l'image OS ; le chemin
    /// « maison » relit le BMP octet par octet, faute de codec. Le second est
    /// des centaines de fois plus lent, et c'est lui qui figeait l'ecran quand
    /// le decodage avait lieu sur le fil d'interface.
    ///
    ///     mono mesure.exe http://127.0.0.1:8080 3017620422003
    /// </summary>
    internal static class MesurePhoto
    {
        public static int Main(string[] args)
        {
            string serveur = args.Length > 0 ? args[0] : "http://127.0.0.1:8080";
            string code = args.Length > 1 ? args[1] : "3017620422003";

            Console.WriteLine("  taille   octets   aller-retour   codec OS   maison");
            double totalReseau = 0;
            foreach (int taille in new int[] { 26, 80, 200 })
            {
                string url = serveur + "/api/image?code=" + code + "&l=" + taille
                             + "&h=" + taille + "&img=bmp8";
                Telecharger(url);                    // chauffe le cache serveur
                Stopwatch montre = Stopwatch.StartNew();
                byte[] octets = Telecharger(url);
                montre.Stop();
                if (octets == null)
                {
                    Console.WriteLine("  {0,5}    photo indisponible", taille);
                    continue;
                }
                double reseau = Math.Round(montre.Elapsed.TotalMilliseconds, 1);
                totalReseau += reseau;
                Console.WriteLine("  {0,5}  {1,7}   {2,9} ms   {3,6} ms   {4,4} ms",
                                  taille, octets.Length, reseau,
                                  Chronometrer(octets, false),
                                  Chronometrer(octets, true));
            }
            Console.WriteLine();
            Console.WriteLine("  L'ecran de liste demande sept vignettes. Sur un seul");
            Console.WriteLine("  couloir, c'est autant de temps pendant lequel un bip");
            Console.WriteLine("  etait ignore : environ {0} ms ici, et cette liaison-ci",
                              Math.Round(totalReseau / 3 * 7));
            Console.WriteLine("  est un reseau local, pas du 802.11b de 2005.");
            return 0;
        }

        private static double Chronometrer(byte[] octets, bool maison)
        {
            Photo.Depuis(octets, maison);            // premiere passe : chauffe
            Stopwatch montre = Stopwatch.StartNew();
            const int Passes = 5;
            for (int i = 0; i < Passes; i++)
            {
                Bitmap image = Photo.Depuis(octets, maison);
                if (image != null) image.Dispose();
            }
            montre.Stop();
            return Math.Round(montre.Elapsed.TotalMilliseconds / Passes, 1);
        }

        private static byte[] Telecharger(string url)
        {
            try
            {
                HttpWebRequest requete = (HttpWebRequest)WebRequest.Create(url);
                requete.Timeout = 20000;
                using (WebResponse reponse = requete.GetResponse())
                using (Stream flux = reponse.GetResponseStream())
                using (MemoryStream tampon = new MemoryStream())
                {
                    byte[] morceau = new byte[8192];
                    int lus;
                    while ((lus = flux.Read(morceau, 0, morceau.Length)) > 0)
                        tampon.Write(morceau, 0, lus);
                    return tampon.ToArray();
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
