using System;
using System.Runtime.InteropServices;

namespace Inventaire
{
    /// <summary>
    /// Le peu de Windows CE qu'il faut appeler directement : la batterie, la
    /// barre des taches, le buzzer.
    ///
    /// Tout est enveloppe dans un try/catch. Sur le poste de developpement,
    /// coredll.dll n'existe pas et chaque appel leve DllNotFoundException ; sur
    /// une image CE amputee, certains exports peuvent manquer. Aucun de ces cas
    /// ne justifie d'empecher l'inventaire du frigo : on degrade en silence.
    /// </summary>
    internal static class Systeme
    {
        // --------------------------------------------------------- batterie

        [DllImport("coredll.dll", EntryPoint = "GetSystemPowerStatusEx")]
        private static extern int EtatAlimentationCE(byte[] etat, int actualiser);

        public const int Inconnu = -1;

        /// <summary>Charge restante en pourcentage, ou <see cref="Inconnu"/>.</summary>
        public static int Batterie(out bool secteur)
        {
            secteur = false;
            try
            {
                // SYSTEM_POWER_STATUS_EX fait 32 octets. Plutot que de le
                // decrire en StructLayout -- dont le CF 2.0 ne gere pas le
                // champ Pack -- on recupere les octets bruts et on lit les
                // trois premiers, dont les decalages sont figes depuis 1998 :
                //   0 ACLineStatus · 1 BatteryFlag · 2 BatteryLifePercent
                byte[] tampon = new byte[32];
                if (EtatAlimentationCE(tampon, 1) == 0)
                    return Inconnu;
                secteur = tampon[0] == 1;
                int pourcent = tampon[2];
                return pourcent > 100 ? Inconnu : pourcent;   // 255 = indetermine
            }
            catch (Exception)
            {
                return Inconnu;
            }
        }

        // ------------------------------------------------------- plein ecran

        [DllImport("coredll.dll", EntryPoint = "GetSystemMetrics")]
        private static extern int MetriqueCE(int index);

        [DllImport("coredll.dll", EntryPoint = "SetWindowPos")]
        private static extern int PlacerCE(IntPtr fenetre, IntPtr apres, int x, int y,
                                           int largeur, int hauteur, uint options);

        private const int LargeurEcran = 0, HauteurEcran = 1;    // SM_CXSCREEN / SM_CYSCREEN
        private const uint SansZOrdre = 0x0004, Afficher = 0x0040;

        /// <summary>
        /// Pose la fenetre sur l'ecran entier, barre des taches comprise.
        ///
        /// Passer par les bornes du formulaire ne suffit pas : Windows CE les
        /// ramene a la zone de travail, qui exclut la barre. En demandant les
        /// dimensions physiques et en posant la fenetre soi-meme, on recupere
        /// la vingtaine de pixels du bas -- sur 320, cela fait une ligne de
        /// liste de plus.
        /// </summary>
        public static bool PleinEcran(IntPtr fenetre)
        {
            try
            {
                int largeur = MetriqueCE(LargeurEcran);
                int hauteur = MetriqueCE(HauteurEcran);
                if (largeur <= 0 || hauteur <= 0)
                    return false;
                PlacerCE(fenetre, IntPtr.Zero, 0, 0, largeur, hauteur,
                         SansZOrdre | Afficher);
                return true;
            }
            catch (Exception)
            {
                return false;       // poste de developpement : pas de coredll
            }
        }

        // ---------------------------------------------------- barre des taches

        [DllImport("coredll.dll", EntryPoint = "FindWindowW")]
        private static extern IntPtr TrouverFenetre(string classe, string titre);

        [DllImport("coredll.dll", EntryPoint = "ShowWindow")]
        private static extern int AfficherFenetre(IntPtr fenetre, int commande);

        [DllImport("coredll.dll", EntryPoint = "EnableWindow")]
        private static extern int ActiverFenetre(IntPtr fenetre, int actif);

        private const int Cacher = 0;
        private const int Montrer = 5;

        // Selon l'image CE, la barre porte l'une ou l'autre de ces classes.
        private static readonly string[] ClassesBarre = { "HHTaskBar", "Shell_TrayWnd" };

        /// <summary>
        /// Cache ou remontre la barre des taches.
        ///
        /// FormBorderStyle.None ne suffit pas : la barre reste au-dessus, et
        /// WindowState.Maximized se cale sur la zone de travail, donc sous
        /// elle. Sur 240x320, elle confisque un onzieme de l'ecran.
        ///
        /// La remontrer en quittant n'est pas une politesse : sans elle,
        /// l'utilisateur se retrouve devant un bureau sans menu Demarrer.
        /// </summary>
        public static void BarreDesTaches(bool visible)
        {
            for (int i = 0; i < ClassesBarre.Length; i++)
            {
                try
                {
                    IntPtr barre = TrouverFenetre(ClassesBarre[i], null);
                    if (barre == IntPtr.Zero)
                        continue;
                    AfficherFenetre(barre, visible ? Montrer : Cacher);
                    ActiverFenetre(barre, visible ? 1 : 0);
                }
                catch (Exception)
                {
                    return;         // pas de coredll : poste de developpement
                }
            }
        }

    }
}
