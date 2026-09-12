using System;
using System.Runtime.InteropServices;

namespace Inventaire
{
    /// <summary>
    /// Effets sonores, synthetises en memoire et joues par coredll.
    ///
    /// MessageBeep, la voie evidente, ne donne que cinq sons figes decides par
    /// l'image du systeme -- et sur un terminal industriel, souvent un seul.
    /// PlaySound, lui, accepte un fichier WAV **en memoire** (SND_MEMORY) :
    /// il suffit donc de fabriquer l'onde soi-meme pour disposer d'une palette
    /// entiere sans rien deposer sur le terminal.
    ///
    /// Les ondes sont en 8 bits a 11 025 Hz, mono. C'est peu, et c'est voulu :
    /// quelques kilo-octets par son, une synthese instantanee sur un PXA270,
    /// et un grain franchement retro qui va bien a la machine.
    ///
    /// Chaque son est fabrique une fois puis garde. Si coredll n'expose pas
    /// PlaySound -- image amputee, poste de developpement -- on retombe sur
    /// MessageBeep, et a defaut sur le silence : jamais sur une exception.
    /// </summary>
    internal static class Sons
    {
        // ------------------------------------------------------- la palette

        public const int Aucun = -1;
        public const int Demarrage = 0;
        public const int Fermeture = 1;
        public const int Bip = 2;          // code-barres lu
        public const int Ajout = 3;
        public const int Retrait = 4;
        public const int Erreur = 5;
        public const int Succes = 6;
        public const int Clic = 7;
        public const int Navigation = 8;
        public const int Obturateur = 9;   // photo agrandie
        public const int Balayage = 10;    // suppression d'un lot
        public const int Alerte = 11;      // produit perime
        private const int Nombre = 12;

        /// <summary>Sons juges indispensables : joues des le niveau 1.</summary>
        private static readonly bool[] Essentiel = {
            true,  true,  true,  true,  true,  true,
            true,  false, false, false, false, true,
        };

        // ------------------------------------------------------ formes d'onde

        private const int Sinus = 0, Carre = 1, Bruit = 2;
        private const int Echantillonnage = 11025;

        private static readonly byte[][] _ondes = new byte[Nombre][];
        private static int _niveau = 2;
        private static int _moteur;        // 0 inconnu, 1 PlaySoundW, 2 PlaySound, 3 bip, 4 muet
        private static int _finPrevue;     // instant ou le son en cours se termine
        private static readonly int[] _file = new int[4];
        private static int _enFile;

        /// <summary>0 muet · 1 l'essentiel · 2 tout.</summary>
        public static void Niveau(int niveau)
        {
            _niveau = niveau < 0 ? 0 : niveau > 2 ? 2 : niveau;
        }

        public static void Jouer(int son)
        {
            if (son < 0 || son >= Nombre || _niveau == 0)
                return;
            if (_niveau < 2 && !Essentiel[son])
                return;

            // Un scan enchaine trois sons en un quart de seconde. Les jouer
            // tels quels les ferait se couper l'un l'autre et n'en laisserait
            // entendre que des bribes : les sons qui portent une information
            // attendent leur tour, les autres passent ou se taisent.
            if (Environment.TickCount - _finPrevue < 0)
            {
                if (!Essentiel[son])
                    return;
                if (_enFile < _file.Length)
                    _file[_enFile++] = son;
                return;
            }
            Lancer(son);
        }

        /// <summary>Appele a chaque battement de la fenetre : depile la suite.</summary>
        public static void Battement()
        {
            if (_enFile == 0 || Environment.TickCount - _finPrevue < 0)
                return;
            int son = _file[0];
            for (int i = 1; i < _enFile; i++)
                _file[i - 1] = _file[i];
            _enFile--;
            Lancer(son);
        }

        private static void Lancer(int son)
        {
            if (_ondes[son] == null)
                _ondes[son] = Fabriquer(son);
            byte[] wav = _ondes[son];
            int duree = (wav.Length - TailleEntete) * 1000 / Echantillonnage;
            _finPrevue = Environment.TickCount + duree + 25;
            Emettre(wav, son == Fermeture);
        }

        /// <summary>
        /// Pousse le volume de sortie au maximum. Effet de bord assume : sur un
        /// terminal qu'on tient a bout de bras dans une cuisine, un son qu'on
        /// n'entend pas ne sert a rien.
        /// </summary>
        public static void VolumeMaximum()
        {
            try
            {
                VolumeCE(IntPtr.Zero, 0xFFFFFFFF);
            }
            catch (Exception)
            {
            }
        }

        // ------------------------------------------------------- fabrication

        private static byte[] Fabriquer(int son)
        {
            switch (son)
            {
                case Demarrage:
                    // Arpege majeur montant : do, mi, sol, do.
                    return Onde(new double[] { 523, 659, 784, 1047 },
                                new int[] { 90, 90, 90, 190 }, Sinus, 0.85);
                case Fermeture:
                    return Onde(new double[] { 784, 659, 523, 392 },
                                new int[] { 80, 80, 80, 220 }, Sinus, 0.8);
                case Bip:
                    // Le bip du lecteur : court, haut, franc.
                    return Onde(new double[] { 2400 }, new int[] { 55 }, Carre, 0.9);
                case Ajout:
                    return Onde(new double[] { 880, 1175, 1568 },
                                new int[] { 55, 55, 110 }, Carre, 0.75);
                case Retrait:
                    return Onde(new double[] { 1568, 1175, 880 },
                                new int[] { 55, 55, 110 }, Carre, 0.75);
                case Erreur:
                    // Deux frequences voisines : le battement sonne faux, et
                    // c'est exactement ce qu'on veut entendre.
                    return Melange(Onde(new double[] { 220 }, new int[] { 300 }, Carre, 0.6),
                                   Onde(new double[] { 233 }, new int[] { 300 }, Carre, 0.6));
                case Succes:
                    return Onde(new double[] { 1047, 1319, 1568, 2093 },
                                new int[] { 45, 45, 45, 130 }, Sinus, 0.8);
                case Clic:
                    return Onde(new double[] { 1800 }, new int[] { 14 }, Carre, 0.35);
                case Navigation:
                    return Balayer(620, 1240, 70, 0.5);
                case Obturateur:
                    // Un claquement : bruit bref, puis un cliquetis metallique.
                    return Coller(Onde(new double[] { 0 }, new int[] { 35 }, Bruit, 0.7),
                                  Onde(new double[] { 3000 }, new int[] { 18 }, Carre, 0.5));
                case Balayage:
                    return Balayer(1800, 180, 200, 0.7);
                case Alerte:
                    return Onde(new double[] { 880, 1175, 880, 1175, 880, 1175 },
                                new int[] { 90, 90, 90, 90, 90, 160 }, Carre, 0.85);
            }
            return Onde(new double[] { 1000 }, new int[] { 50 }, Carre, 0.5);
        }

        /// <summary>Suite de notes, chacune avec sa duree en millisecondes.</summary>
        private static byte[] Onde(double[] frequences, int[] durees, int forme,
                                   double volume)
        {
            int total = 0;
            for (int i = 0; i < durees.Length; i++)
                total += durees[i] * Echantillonnage / 1000;

            byte[] donnees = new byte[total];
            int position = 0;
            for (int note = 0; note < frequences.Length; note++)
            {
                int n = durees[note] * Echantillonnage / 1000;
                double pas = frequences[note] * 2.0 * Math.PI / Echantillonnage;
                int graine = 12345 + note * 7919;
                for (int i = 0; i < n; i++)
                {
                    double valeur;
                    if (forme == Bruit)
                    {
                        // Generateur congruentiel : Random existe sous CF 2.0,
                        // mais une suite reproductible rend le son identique
                        // d'une execution a l'autre.
                        graine = graine * 1103515245 + 12345;
                        valeur = ((graine >> 16) & 0x7FFF) / 16384.0 - 1.0;
                    }
                    else
                    {
                        double sinus = Math.Sin(pas * i);
                        valeur = forme == Carre ? (sinus >= 0 ? 1.0 : -1.0) : sinus;
                    }
                    donnees[position + i] = Echantillon(valeur * volume
                                                        * Enveloppe(i, n));
                }
                position += n;
            }
            return Envelopper(donnees);
        }

        /// <summary>Glissando d'une frequence a une autre.</summary>
        private static byte[] Balayer(double depart, double arrivee, int duree,
                                      double volume)
        {
            int n = duree * Echantillonnage / 1000;
            byte[] donnees = new byte[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                double frequence = depart + (arrivee - depart) * i / n;
                phase += frequence * 2.0 * Math.PI / Echantillonnage;
                double sinus = Math.Sin(phase);
                donnees[i] = Echantillon((sinus >= 0 ? 1.0 : -1.0) * volume
                                         * Enveloppe(i, n));
            }
            return Envelopper(donnees);
        }

        /// <summary>Attaque et chute courtes : sans elles, chaque note claque.</summary>
        private static double Enveloppe(int i, int n)
        {
            int frange = Echantillonnage / 400;         // 2,5 ms
            if (frange < 1)
                return 1.0;
            if (i < frange)
                return (double)i / frange;
            if (i > n - frange)
                return (double)(n - i) / frange;
            return 1.0;
        }

        private static byte Echantillon(double valeur)
        {
            int v = 128 + (int)(valeur * 127.0);
            if (v < 0) v = 0;
            if (v > 255) v = 255;
            return (byte)v;
        }

        private static byte[] Coller(byte[] premier, byte[] second)
        {
            byte[] a = Donnees(premier), b = Donnees(second);
            byte[] somme = new byte[a.Length + b.Length];
            Array.Copy(a, 0, somme, 0, a.Length);
            Array.Copy(b, 0, somme, a.Length, b.Length);
            return Envelopper(somme);
        }

        private static byte[] Melange(byte[] premier, byte[] second)
        {
            byte[] a = Donnees(premier), b = Donnees(second);
            int n = a.Length < b.Length ? a.Length : b.Length;
            byte[] somme = new byte[n];
            for (int i = 0; i < n; i++)
                somme[i] = Echantillon(((a[i] - 128) + (b[i] - 128)) / 254.0);
            return Envelopper(somme);
        }

        private const int TailleEntete = 44;

        private static byte[] Donnees(byte[] wav)
        {
            byte[] brut = new byte[wav.Length - TailleEntete];
            Array.Copy(wav, TailleEntete, brut, 0, brut.Length);
            return brut;
        }

        /// <summary>En-tete RIFF/WAVE de 44 octets, PCM 8 bits mono.</summary>
        private static byte[] Envelopper(byte[] donnees)
        {
            byte[] wav = new byte[TailleEntete + donnees.Length];
            Ascii(wav, 0, "RIFF");
            Entier(wav, 4, 36 + donnees.Length);
            Ascii(wav, 8, "WAVE");
            Ascii(wav, 12, "fmt ");
            Entier(wav, 16, 16);                    // taille du bloc fmt
            Court(wav, 20, 1);                      // PCM
            Court(wav, 22, 1);                      // mono
            Entier(wav, 24, Echantillonnage);
            Entier(wav, 28, Echantillonnage);       // octets par seconde
            Court(wav, 32, 1);                      // alignement de bloc
            Court(wav, 34, 8);                      // bits par echantillon
            Ascii(wav, 36, "data");
            Entier(wav, 40, donnees.Length);
            Array.Copy(donnees, 0, wav, TailleEntete, donnees.Length);
            return wav;
        }

        private static void Ascii(byte[] tampon, int position, string texte)
        {
            for (int i = 0; i < texte.Length; i++)
                tampon[position + i] = (byte)texte[i];
        }

        private static void Entier(byte[] tampon, int position, int valeur)
        {
            tampon[position] = (byte)(valeur & 0xFF);
            tampon[position + 1] = (byte)((valeur >> 8) & 0xFF);
            tampon[position + 2] = (byte)((valeur >> 16) & 0xFF);
            tampon[position + 3] = (byte)((valeur >> 24) & 0xFF);
        }

        private static void Court(byte[] tampon, int position, int valeur)
        {
            tampon[position] = (byte)(valeur & 0xFF);
            tampon[position + 1] = (byte)((valeur >> 8) & 0xFF);
        }

        // ------------------------------------------------------------ sortie

        private const uint SndAsync = 0x0001;
        private const uint SndNoDefault = 0x0002;
        private const uint SndMemory = 0x0004;

        [DllImport("coredll.dll", EntryPoint = "PlaySoundW")]
        private static extern int JouerLargeCE(byte[] son, IntPtr module, uint options);

        [DllImport("coredll.dll", EntryPoint = "PlaySound")]
        private static extern int JouerCE(byte[] son, IntPtr module, uint options);

        [DllImport("coredll.dll", EntryPoint = "MessageBeep")]
        private static extern int BipCE(uint type);

        [DllImport("coredll.dll", EntryPoint = "waveOutSetVolume")]
        private static extern int VolumeCE(IntPtr appareil, uint volume);

        private static void Emettre(byte[] wav, bool attendre)
        {
            uint options = SndMemory | SndNoDefault | (attendre ? 0u : SndAsync);

            // Le premier son decide du moteur : selon l'image CE, coredll
            // exporte PlaySoundW, PlaySound, ou ni l'un ni l'autre.
            if (_moteur == 0 || _moteur == 1)
            {
                try
                {
                    JouerLargeCE(wav, IntPtr.Zero, options);
                    _moteur = 1;
                    return;
                }
                catch (Exception)
                {
                    _moteur = 2;
                }
            }
            if (_moteur == 2)
            {
                try
                {
                    JouerCE(wav, IntPtr.Zero, options);
                    return;
                }
                catch (Exception)
                {
                    _moteur = 3;
                }
            }
            if (_moteur == 3)
            {
                try
                {
                    BipCE(0);
                    return;
                }
                catch (Exception)
                {
                    _moteur = 4;        // ni son ni bip : on se tait pour de bon
                }
            }
        }
    }
}
