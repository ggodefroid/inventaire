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
        public const int Voix = 12;        // le programme dit son nom
        public const int Branchement = 13; // le terminal vient d'etre mis en charge
        private const int Nombre = 14;

        /// <summary>Sons juges indispensables : joues des le niveau 1.</summary>
        private static readonly bool[] Essentiel = {
            true,  true,  true,  true,  true,  true,
            true,  false, false, false, false, true,
            true,  false,
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
                case Voix:
                    return Voix_();
                case Branchement:
                    // Un glissando qui monte, puis deux notes tenues : le son
                    // suit le geste, et se reconnait sans regarder l'ecran.
                    return Coller(Balayer(320, 1150, 70, 0.5),
                                  Onde(new double[] { 1568, 2093 },
                                       new int[] { 70, 160 }, Sinus, 0.7));
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

        // ------------------------------------------------------------- la voix

        /// <summary>Un phoneme : trois formants, une nasalite, une source.</summary>
        private sealed class Phoneme
        {
            public readonly double F1, F2, F3;
            /// <summary>0 bouche ouverte · 1 voyelle nasale (« in », « an »).</summary>
            public readonly double Nasal;
            /// <summary>Part de source glottale, et part de souffle.</summary>
            public readonly double Voise, Souffle;
            public readonly int Duree;          // millisecondes

            public Phoneme(double f1, double f2, double f3, double nasal,
                           double voise, double souffle, int duree)
            {
                F1 = f1; F2 = f2; F3 = f3;
                Nasal = nasal; Voise = voise; Souffle = souffle; Duree = duree;
            }

            /// <summary>L'occlusion d'une consonne : bouche fermee, rien ne sort.</summary>
            public bool Muet { get { return Voise == 0 && Souffle == 0; } }
        }

        /// <summary>
        /// « Inventaire », prononce au demarrage.
        ///
        /// Aucune image de Windows CE n'embarque de synthese vocale, et poser
        /// un enregistrement a cote du binaire irait contre le principe du
        /// reste du fichier : tout est fabrique en memoire. La voix l'est donc
        /// aussi -- douze kilo-octets calcules au premier usage, et rien sur
        /// la carte.
        ///
        /// Le procede est celui de Klatt, reduit a l'os. Une source -- des
        /// impulsions glottales melangees a du souffle -- traverse trois
        /// resonateurs a deux poles cales sur les formants du phoneme en
        /// cours. Ce sont les deux premiers formants qui font la voyelle, le
        /// troisieme donne le timbre, et un quatrieme resonateur grave, en
        /// parallele, porte le murmure nasal : sans lui, « Inventaire »
        /// s'entend « Evatere ».
        ///
        /// Le grain suave, lui, tient a quatre nombres et pas un de plus : une
        /// fondamentale basse qui descend d'un bout a l'autre du mot, un
        /// vibrato lent qui s'elargit vers la fin, une impulsion glottale tres
        /// arrondie -- donc pauvre en aigus -- et une bonne dose de souffle.
        /// Le debit fait le reste : une bonne demi-seconde pour « Inven- »,
        /// autant pour un « -taire » qui s'eteint au lieu de s'arreter.
        /// </summary>
        private static byte[] Voix_()
        {
            //            F1    F2    F3  nasal voise souf.   ms
            Phoneme[] mot = {
                new Phoneme( 540, 1380, 2450, 0.85, 1.00, 0.30, 225),  // « in »
                new Phoneme( 330, 1150, 2200, 0.00, 0.75, 0.55, 100),  // « v »
                new Phoneme( 660, 1040, 2400, 0.85, 1.00, 0.28, 225),  // « an »
                new Phoneme(   0,    0,    0, 0.00, 0.00, 0.00,  60),  // « t » : l'occlusion
                new Phoneme(1750, 2600, 3300, 0.00, 0.00, 0.45,  22),  //         puis la detente
                new Phoneme( 600, 1800, 2560, 0.00, 1.00, 0.30, 200),  // « ai »
                new Phoneme( 420, 1280, 2320, 0.00, 0.85, 0.45, 300),  // « re », qui s'eteint
            };

            int total = 0;
            for (int i = 0; i < mot.Length; i++)
                total += mot[i].Duree * Echantillonnage / 1000;

            // Trois resonateurs en cascade, un quatrieme en parallele pour le
            // nez : coefficients et memoire de chacun.
            double[] a = new double[4], b = new double[4], c = new double[4];
            double[] z1 = new double[4], z2 = new double[4];
            double f1 = mot[0].F1, f2 = mot[0].F2, f3 = mot[0].F3, nasal = mot[0].Nasal;
            double f0 = 0, phase = 0, fluxPrecedent = 0, doux = 0, souffleDoux = 0;
            int graine = 20050317;

            double[] brut = new double[total];
            double crete = 0;
            int position = 0;

            const int Bloc = 64;                // ~6 ms

            for (int p = 0; p < mot.Length; p++)
            {
                Phoneme ph = mot[p];
                int n = ph.Duree * Echantillonnage / 1000;
                if (ph.Muet)
                {
                    // Bouche fermee : rien a ecrire, brut[] vaut deja zero. Les
                    // resonateurs repartent de zero a la detente, et c'est ce
                    // silence net -- plus que la detente elle-meme -- qui fait
                    // entendre une occlusive.
                    for (int k = 0; k < 4; k++) { z1[k] = 0; z2[k] = 0; }
                    position += n;
                    continue;
                }
                for (int i = 0; i < n; i++, position++)
                {
                    if (i % Bloc == 0)
                    {
                        // Recalcule par blocs, et non par echantillon : le
                        // PXA270 n'a pas d'unite flottante, et trois
                        // exponentielles par echantillon couteraient plus cher
                        // que tout le reste du son. Six millisecondes de grain,
                        // c'est sous le seuil de l'oreille.
                        double avance = (double)position / total;
                        f0 = 186.0 - 56.0 * avance
                             + (1.2 + 3.6 * avance)
                               * Math.Sin(2.0 * Math.PI * 4.6 * position
                                          / Echantillonnage);
                        // Les formants rejoignent leur cible en une vingtaine
                        // de millisecondes. C'est ce glissement, et non les
                        // voyelles elles-memes, qui fait entendre un mot
                        // plutot qu'une suite de sons tenus.
                        f1 += (ph.F1 - f1) * 0.28;
                        f2 += (ph.F2 - f2) * 0.28;
                        f3 += (ph.F3 - f3) * 0.28;
                        nasal += (ph.Nasal - nasal) * 0.28;
                        // Un premier formant elargi par la nasalite : le nez
                        // amortit la bouche autant qu'il ajoute son murmure.
                        Resonateur(a, b, c, 0, f1, 90.0 + 140.0 * nasal);
                        Resonateur(a, b, c, 1, f2, 72.0);
                        Resonateur(a, b, c, 2, f3, 130.0);
                        Resonateur(a, b, c, 3, 270.0, 150.0);
                    }

                    // Impulsion de Rosenberg : l'onde que produit reellement
                    // une glotte, montee lente et fermeture franche. Sa
                    // derivee tient lieu de source -- c'est aussi ce que fait
                    // le rayonnement des levres.
                    phase += f0 / Echantillonnage;
                    if (phase >= 1.0)
                        phase -= 1.0;
                    double flux;
                    if (phase < 0.42)
                    {
                        double u = phase / 0.42;
                        flux = u * u * (3.0 - 2.0 * u);
                    }
                    else if (phase < 0.58)
                    {
                        double u = (phase - 0.42) / 0.16;
                        flux = 1.0 - u * u;
                    }
                    else
                    {
                        flux = 0.0;
                    }
                    double glotte = flux - fluxPrecedent;
                    fluxPrecedent = flux;

                    // Le souffle : le meme generateur congruentiel que les
                    // autres sons, adouci pour qu'il murmure au lieu de
                    // siffler.
                    graine = graine * 1103515245 + 12345;
                    double bruit = ((graine >> 16) & 0x7FFF) / 16384.0 - 1.0;
                    souffleDoux += (bruit - souffleDoux) * 0.4;

                    // Inclinaison spectrale : avec le souffle, c'est elle qui
                    // separe une voix qui appelle d'une voix qui murmure.
                    double source = glotte * ph.Voise + souffleDoux * ph.Souffle * 0.12;
                    doux += (source - doux) * 0.75;

                    double v = doux;
                    for (int k = 0; k < 3; k++)
                    {
                        double y = a[k] * v + b[k] * z1[k] + c[k] * z2[k];
                        z2[k] = z1[k];
                        z1[k] = y;
                        v = y;
                    }
                    if (nasal > 0.01)
                    {
                        double y = a[3] * doux + b[3] * z1[3] + c[3] * z2[3];
                        z2[3] = z1[3];
                        z1[3] = y;
                        v += y * nasal * 0.6;
                    }

                    brut[position] = v;
                    double amplitude = v < 0 ? -v : v;
                    if (amplitude > crete)
                        crete = amplitude;
                }
            }

            // Le gain d'une cascade de resonateurs depend des formants : plutot
            // que de le calculer, on mesure la crete et on ramene le tout a
            // l'echelle du huit bits.
            double echelle = crete > 0 ? 0.92 / crete : 0.0;
            int attaque = 30 * Echantillonnage / 1000;
            int chute = 260 * Echantillonnage / 1000;
            byte[] donnees = new byte[total];
            for (int i = 0; i < total; i++)
            {
                double enveloppe = i < attaque ? (double)i / attaque : 1.0;
                if (i > total - chute)
                {
                    // Le mot ne se coupe pas, il s'eteint : la courbe est au
                    // carre, donc longue d'abord et franche a la fin.
                    double u = (double)(total - i) / chute;
                    enveloppe *= u * u;
                }
                donnees[i] = Echantillon(brut[i] * echelle * enveloppe);
            }
            return Envelopper(donnees);
        }

        /// <summary>
        /// Coefficients d'un resonateur a deux poles, c'est-a-dire d'un
        /// formant. Le gain est normalise a l'unite en continu (a = 1 - b - c),
        /// ce qui permet d'en cascader plusieurs sans que le niveau s'envole.
        /// </summary>
        private static void Resonateur(double[] a, double[] b, double[] c, int rang,
                                       double frequence, double largeur)
        {
            double r = Math.Exp(-Math.PI * largeur / Echantillonnage);
            b[rang] = 2.0 * r * Math.Cos(2.0 * Math.PI * frequence / Echantillonnage);
            c[rang] = -r * r;
            a[rang] = 1.0 - b[rang] - c[rang];
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
