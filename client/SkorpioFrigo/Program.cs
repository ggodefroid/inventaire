using System;
using System.IO;
using System.Windows.Forms;

namespace SkorpioFrigo
{
    internal static class Program
    {
        public const string Version = "0.1.0";

        [MTAThread]
        public static void Main()
        {
            Settings settings;
            try
            {
                settings = Settings.Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Reglages illisibles, valeurs par defaut utilisees.\r\n"
                                + ex.Message, "Frigo", MessageBoxButtons.OK,
                                MessageBoxIcon.None, MessageBoxDefaultButton.Button1);
                settings = new Settings();
            }

            ScanBuffer buffer;
            try
            {
                buffer = new ScanBuffer();
            }
            catch (Exception ex)
            {
                // Sans tampon utilisable, tout scan serait perdu : on refuse de demarrer
                // plutot que de faire croire a l'utilisateur que ses scans sont gardes.
                MessageBox.Show("Impossible d'ouvrir le tampon de scans dans\r\n"
                                + Settings.BaseDir + "\r\n\r\n" + ex.Message
                                + "\r\n\r\nLe programme ne peut pas demarrer.",
                                "Erreur fatale", MessageBoxButtons.OK,
                                MessageBoxIcon.None, MessageBoxDefaultButton.Button1);
                return;
            }

            Application.Run(new MainForm(settings, buffer));
        }
    }
}
