using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

namespace Inventaire
{
    /// <summary>
    /// Banc de rendu des economiseurs, hors terminal.
    ///
    /// Parcourt toutes les classes derivees d'Effet, en anime chacune
    /// quelques dizaines d'images et en sauve une planche. Cela ne remplace
    /// pas un regard sur le materiel, mais cela prouve qu'aucun economiseur
    /// ne leve d'exception ni ne rend un ecran vide -- deux pannes qu'on ne
    /// verrait autrement qu'en attendant trente secondes devant l'appareil,
    /// cinquante-cinq fois de suite.
    ///
    /// Compile avec les memes sources, contre le Mono de bureau :
    ///     mcs -main:Inventaire.PlancheVeille ... -out:planche.exe
    /// </summary>
    internal static class PlancheVeille
    {
        private const int L = 240, H = 320, Images = 90;

        public static int Main(string[] args)
        {
            string sortie = args.Length > 0 ? args[0] : "planche";
            Directory.CreateDirectory(sortie);

            Type[] tous = Assembly.GetExecutingAssembly().GetTypes();
            Array.Sort(tous, new Comparison<Type>(ParNom));

            int rendus = 0, vides = 0, casses = 0;
            foreach (Type type in tous)
            {
                if (type.IsAbstract || !type.IsSubclassOf(typeof(Effet)))
                    continue;
                Effet effet = (Effet)Activator.CreateInstance(type);
                string nom;
                try { nom = effet.Nom; } catch (Exception) { nom = type.Name; }

                Bitmap image = new Bitmap(L, H);
                bool ok = true;
                string souci = "";
                using (Graphics g = Graphics.FromImage(image))
                {
                    try
                    {
                        effet.Poser(L, H, new Random(12345));
                        for (int i = 0; i < Images; i++)
                        {
                            effet.Avancer();
                            g.Clear(Color.Black);
                            effet.Peindre(g);
                        }
                    }
                    catch (Exception exc)
                    {
                        ok = false;
                        souci = exc.GetType().Name + " : " + exc.Message;
                    }
                }

                int allumes = Allumes(image);
                string fichier = Path.Combine(sortie, type.Name + ".png");
                image.Save(fichier, ImageFormat.Png);
                image.Dispose();

                if (!ok)
                {
                    casses++;
                    Console.WriteLine("  CASSE   {0,-20} {1}", type.Name, souci);
                }
                // Un champ d'etoiles est legitimement clairseme : le seuil ne
                // traque que l'ecran reellement noir, signe d'un effet casse.
                else if (allumes < 60)
                {
                    vides++;
                    Console.WriteLine("  VIDE    {0,-20} {1} pixels allumes",
                                      type.Name, allumes);
                }
                else
                {
                    rendus++;
                    Console.WriteLine("  ok      {0,-20} {1,6} px   « {2} »",
                                      type.Name, allumes, nom);
                }
            }
            Console.WriteLine();
            Console.WriteLine("{0} economiseurs : {1} rendus, {2} vides, {3} casses",
                              rendus + vides + casses, rendus, vides, casses);
            return (vides + casses) == 0 ? 0 : 1;
        }

        private static int ParNom(Type a, Type b)
        {
            return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        }

        /// <summary>Pixels non noirs : un ecran vide est une panne silencieuse.</summary>
        private static int Allumes(Bitmap image)
        {
            int n = 0;
            for (int y = 0; y < image.Height; y += 2)
                for (int x = 0; x < image.Width; x += 2)
                {
                    Color c = image.GetPixel(x, y);
                    if (c.R > 12 || c.G > 12 || c.B > 12) n++;
                }
            return n * 4;
        }
    }
}
