using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace Inventaire
{
    /// <summary>
    /// Reglages persistes dans inventaire.ini, a cote de l'executable.
    ///
    /// Windows CE n'a pas de repertoire courant : un chemin relatif finit a la
    /// racine du terminal. Tous les chemins sont donc derives de l'emplacement
    /// de l'assembly.
    /// </summary>
    internal sealed class Reglages
    {
        public string Hote = "192.168.1.10";
        public int Port = 8080;
        public int DelaiMs = 5000;          // au-dela, le serveur est declare muet
        public bool Photos = true;
        public int TailleImage = 80;
        /// <summary>0 muet · 1 l'essentiel · 2 tous les effets.</summary>
        public int Bip = 2;
        public bool VolumeMax = true;
        /// <summary>Secondes d'inactivite avant l'economiseur. 0 = jamais.</summary>
        public int VeilleS = 60;
        /// <summary>Secondes d'inactivite avant l'ecran noir. 0 = jamais.</summary>
        public int NoirS = 240;
        /// <summary>Verifier au demarrage si le serveur a une version plus recente.</summary>
        public bool MajAuto = true;
        public int QuantiteParDefaut = 1;

        /// <summary>
        /// "auto" : on tente d'abord Bitmap(flux), et on retombe sur le
        /// decodeur maison s'il echoue. "maison" : on court-circuite Bitmap.
        ///
        /// Ce reglage existe pour une raison precise. Le repli n'est cense
        /// servir que sur une image OS depourvue de codecs, c'est-a-dire
        /// justement la ou personne ne peut l'essayer avant d'y etre. Pouvoir
        /// le forcer permet de l'eprouver sur n'importe quelle machine.
        /// </summary>
        public string DecodeurBmp = "auto";

        private static string _dossier;
        private static string _cheminExe;

        /// <summary>Chemin complet de l'executable en cours d'execution.</summary>
        public static string CheminExe
        {
            get
            {
                if (_cheminExe == null)
                {
                    try
                    {
                        _cheminExe = Assembly.GetExecutingAssembly()
                            .GetModules()[0].FullyQualifiedName;
                    }
                    catch (Exception)
                    {
                        _cheminExe = Path.Combine(Dossier, "Inventaire.exe");
                    }
                }
                return _cheminExe;
            }
        }

        /// <summary>Repertoire de l'executable : seul chemin fiable sous CE.</summary>
        public static string Dossier
        {
            get
            {
                if (_dossier == null)
                {
                    try
                    {
                        string module = Assembly.GetExecutingAssembly()
                            .GetModules()[0].FullyQualifiedName;
                        _dossier = Path.GetDirectoryName(module);
                    }
                    catch (Exception)
                    {
                        _dossier = "\\Program Files\\Frigo";
                    }
                    if (_dossier == null || _dossier.Length == 0)
                        _dossier = "\\";
                }
                return _dossier;
            }
        }

        public const string NomFichier = "inventaire.ini";
        private const string AncienNom = "frigo.ini";

        private static string Fichier
        {
            get { return Path.Combine(Dossier, NomFichier); }
        }

        /// <summary>
        /// Fichier a lire : le nouveau nom, ou l'ancien s'il traine encore.
        /// Le programme s'est appele « frigo » un temps ; personne ne devrait
        /// avoir a ressaisir l'adresse du serveur pour autant.
        /// </summary>
        private static string FichierALire
        {
            get
            {
                if (File.Exists(Fichier))
                    return Fichier;
                string ancien = Path.Combine(Dossier, AncienNom);
                return File.Exists(ancien) ? ancien : Fichier;
            }
        }

        public string BaseUrl
        {
            get { return "http://" + Hote + ":" + Port; }
        }

        public static Reglages Charger()
        {
            Reglages r = new Reglages();
            string source = FichierALire;
            if (!File.Exists(source))
                return r;
            Dictionary<string, string> kv = new Dictionary<string, string>();
            using (StreamReader lecteur = new StreamReader(source, Encoding.UTF8))
            {
                string ligne;
                while ((ligne = lecteur.ReadLine()) != null)
                {
                    ligne = ligne.Trim();
                    if (ligne.Length == 0 || ligne[0] == '#' || ligne[0] == ';'
                        || ligne[0] == '[')
                        continue;
                    int egal = ligne.IndexOf('=');
                    if (egal <= 0)
                        continue;
                    kv[ligne.Substring(0, egal).Trim().ToLower()] =
                        ligne.Substring(egal + 1).Trim();
                }
            }
            r.Hote = Texte(kv, "serveur", r.Hote);
            r.Port = Entier(kv, "port", r.Port);
            r.DelaiMs = Entier(kv, "delai_ms", r.DelaiMs);
            r.Photos = Texte(kv, "photos", "1") != "0";
            // Le cadre de la photo est peint a cette taille exacte et l'image
            // est demandee au serveur dans la meme : hors de ces bornes, la
            // mise en page de l'ecran article deborde.
            r.TailleImage = Entier(kv, "taille_image", r.TailleImage);
            if (r.TailleImage < 48) r.TailleImage = 48;
            if (r.TailleImage > 96) r.TailleImage = 96;
            // L'ancien reglage etait un booleen : « 1 » vaut donc
            // « l'essentiel », et il faut demander « 2 » pour tout.
            r.Bip = Entier(kv, "bip", r.Bip);
            if (r.Bip < 0) r.Bip = 0;
            if (r.Bip > 2) r.Bip = 2;
            r.VolumeMax = Texte(kv, "volume_max", "1") != "0";
            r.VeilleS = Entier(kv, "veille_s", r.VeilleS);
            r.NoirS = Entier(kv, "noir_s", r.NoirS);
            r.MajAuto = Texte(kv, "maj_auto", "1") != "0";
            r.QuantiteParDefaut = Entier(kv, "quantite_defaut", r.QuantiteParDefaut);
            r.DecodeurBmp = Texte(kv, "decodeur_bmp", r.DecodeurBmp).ToLower();
            return r;
        }

        public void Enregistrer()
        {
            using (StreamWriter ecrivain = new StreamWriter(Fichier, false, Encoding.UTF8))
            {
                ecrivain.WriteLine("# Inventaire - reglages du terminal");
                ecrivain.WriteLine("# serveur : IP ou nom du PC qui fait tourner serveur.py");
                ecrivain.WriteLine("serveur = " + Hote);
                ecrivain.WriteLine("port = " + Port);
                ecrivain.WriteLine("delai_ms = " + DelaiMs);
                ecrivain.WriteLine("photos = " + (Photos ? "1" : "0"));
                ecrivain.WriteLine("taille_image = " + TailleImage);
                ecrivain.WriteLine("# bip : 0 muet, 1 l'essentiel, 2 tous les effets");
                ecrivain.WriteLine("bip = " + Bip);
                ecrivain.WriteLine("volume_max = " + (VolumeMax ? "1" : "0"));
                ecrivain.WriteLine("# veille_s : economiseur apres N s "
                                   + "d'inactivite · noir_s : ecran noir. 0 = jamais");
                ecrivain.WriteLine("veille_s = " + VeilleS);
                ecrivain.WriteLine("noir_s = " + NoirS);
                ecrivain.WriteLine("# maj_auto : proposer la mise a jour "
                                   + "au demarrage si le serveur en a une");
                ecrivain.WriteLine("maj_auto = " + (MajAuto ? "1" : "0"));
                ecrivain.WriteLine("quantite_defaut = " + QuantiteParDefaut);
                ecrivain.WriteLine("# decodeur_bmp : auto, ou maison pour "
                                   + "court-circuiter Bitmap(flux)");
                ecrivain.WriteLine("decodeur_bmp = " + DecodeurBmp);
            }
        }

        private static string Texte(Dictionary<string, string> kv, string cle, string defaut)
        {
            string valeur;
            if (kv.TryGetValue(cle, out valeur) && valeur.Length > 0)
                return valeur;
            return defaut;
        }

        private static int Entier(Dictionary<string, string> kv, string cle, int defaut)
        {
            string valeur;
            if (kv.TryGetValue(cle, out valeur))
            {
                try { return int.Parse(valeur.Trim()); }
                catch (Exception) { }
            }
            return defaut;
        }

    }
}
