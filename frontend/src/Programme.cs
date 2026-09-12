using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Inventaire
{
    internal static class Programme
    {
        public const string Version = "1.0.0";
        public const string FichierErreur = "inventaire-erreur.txt";

        [MTAThread]
        public static void Main()
        {
            Reglages reglages;
            try
            {
                reglages = Reglages.Charger();
            }
            catch (Exception ex)
            {
                // Un inventaire.ini illisible ne doit pas empecher de demarrer :
                // l'ecran de reglages permettra de le reecrire.
                Dire("Reglages illisibles, valeurs par defaut.\r\n" + ex.Message,
                     "Inventaire");
                reglages = new Reglages();
            }

            try
            {
                Application.Run(new Fenetre(reglages));
            }
            catch (Exception ex)
            {
                // Sur le terminal il n'y a ni console ni debogueur : une
                // exception qui remonte jusqu'ici ferait disparaitre
                // l'application sans un mot. On ecrit la trace complete a cote
                // de l'executable, et on dit ou la lire.
                string chemin = Consigner(ex);
                Dire(ex.GetType().Name + "\r\n" + ex.Message
                     + (chemin != null ? "\r\n\r\nDetail :\r\n" + chemin : ""),
                     "Inventaire - arret");
            }
        }

        private static void Dire(string texte, string titre)
        {
            MessageBox.Show(texte, titre, MessageBoxButtons.OK, MessageBoxIcon.None,
                            MessageBoxDefaultButton.Button1);
        }

        /// <summary>Ecrit la trace dans inventaire-erreur.txt. Retourne son chemin.</summary>
        private static string Consigner(Exception ex)
        {
            try
            {
                string chemin = Path.Combine(Reglages.Dossier, FichierErreur);
                StringBuilder texte = new StringBuilder();
                texte.Append("Inventaire ").Append(Version).Append("  ")
                     .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                     .Append("\r\n\r\n");
                for (Exception courante = ex; courante != null;
                     courante = courante.InnerException)
                {
                    texte.Append(courante.GetType().FullName).Append("\r\n")
                         .Append(courante.Message).Append("\r\n")
                         .Append(courante.StackTrace).Append("\r\n\r\n");
                }
                using (StreamWriter sortie = new StreamWriter(chemin, true,
                                                              Encoding.UTF8))
                    sortie.Write(texte.ToString());
                return chemin;
            }
            catch (Exception)
            {
                return null;        // support en lecture seule : tant pis
            }
        }
    }
}
