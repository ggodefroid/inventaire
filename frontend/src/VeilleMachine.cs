using System;
using System.Drawing;

namespace Inventaire
{
    // ====================================================================
    //  Vingt ecrans de machine : ce que montraient les ordinateurs quand ils
    //  travaillaient, tombaient en panne, ou attendaient qu'on les regarde.
    // ====================================================================

    /// <summary>L'ecran bleu. Celui de Windows NT, pas le sourire de Windows 8.</summary>
    internal sealed class EcranBleu : Effet
    {
        public override string Nom { get { return "ECRAN BLEU"; } }
        private static readonly Color Bleu = Color.FromArgb(0, 0, 170);
        private int _avancement;

        protected override void Demarrer() { _avancement = 0; }

        public override void Avancer()
        {
            base.Avancer();
            if (T % 8 == 0 && _avancement < 100) _avancement += Hasard.Next(4);
        }

        public override void Peindre(Graphics g)
        {
            Fond(g, Bleu);
            int y = 24;
            g.FillRectangle(Theme.P(Color.LightGray), (L - 96) / 2, y, 96, 14);
            g.DrawString(" INVENTAIRE ", Theme.PetiteGras, Theme.P(Bleu), (L - 96) / 2 + 4, y);
            y += 26;
            string[] lignes = {
                "Une erreur fatale est survenue",
                "a l'adresse 0028:C001A4B2.",
                "",
                "*  Appuyez sur une touche pour",
                "   revenir au frigo.",
                "*  Appuyez sur CTRL+ALT+SUPPR",
                "   pour redemarrer. Vous perdrez",
                "   les yaourts non enregistres.",
                "",
                "Vidage de la memoire physique...",
            };
            foreach (string ligne in lignes)
            {
                g.DrawString(ligne, Theme.Petite, Theme.P(Color.LightGray), 10, y);
                y += 15;
            }
            g.DrawString(_avancement + " %", Theme.Petite, Theme.P(Color.LightGray), 10, y);
        }
    }

    /// <summary>L'invite MS-DOS, avec son curseur qui bat.</summary>
    internal sealed class InviteDos : Effet
    {
        public override string Nom { get { return "MS-DOS"; } }
        private static readonly string[] Session = {
            "Microsoft(R) MS-DOS(R) Version 6.22",
            "  (C)Copyright Microsoft Corp 1981-1994.",
            "",
            "C:\\>cd FRIGO",
            "",
            "C:\\FRIGO>dir *.dat",
            "",
            " Volume in drive C is SKORPIO",
            " Directory of C:\\FRIGO",
            "",
            "LOTS     DAT        4 096  13-09-26  12:04",
            "PRODUIT  DAT       61 440  13-09-26  12:04",
            "JOURNAL  DAT        8 192  13-09-26  12:04",
            "COURSES  DAT          512  13-09-26  12:04",
            "        4 file(s)     74 240 bytes",
            "                   1 998 848 bytes free",
            "",
            "C:\\FRIGO>inventaire /scan",
            "Lecture du code-barres...",
        };
        private int _lignes, _colonne;

        protected override void Demarrer() { _lignes = 0; _colonne = 0; }

        public override void Avancer()
        {
            base.Avancer();
            if (_lignes >= Session.Length) return;
            _colonne += 2;
            if (_colonne >= Session[_lignes].Length) { _lignes++; _colonne = 0; }
        }

        public override void Peindre(Graphics g)
        {
            Color encre = Color.FromArgb(190, 190, 190);
            int y = 4;
            for (int i = 0; i < _lignes && i < Session.Length; i++)
            {
                g.DrawString(Session[i], Theme.Minuscule, Theme.P(encre), 3, y);
                y += 12;
            }
            if (_lignes < Session.Length)
            {
                string partielle = Session[_lignes].Substring(
                    0, Math.Min(_colonne, Session[_lignes].Length));
                g.DrawString(partielle, Theme.Minuscule, Theme.P(encre), 3, y);
                int x = 3 + (int)g.MeasureString(partielle, Theme.Minuscule).Width;
                if ((T / 8) % 2 == 0)
                    g.FillRectangle(Theme.P(encre), x, y + 2, 6, 10);
            }
        }
    }

    /// <summary>Le defragmenteur de Windows 95. On pouvait le regarder des heures.</summary>
    internal sealed class Defragmenteur : Effet
    {
        public override string Nom { get { return "DEFRAGMENTATION"; } }
        private const int Cote = 8;
        private byte[] _blocs;           // 0 libre, 1 utilise, 2 en cours, 3 range
        private int _l, _h, _curseur;

        protected override void Demarrer()
        {
            _l = L / Cote; _h = (H - 46) / Cote;
            _blocs = new byte[_l * _h];
            for (int i = 0; i < _blocs.Length; i++)
                _blocs[i] = (byte)(Hasard.Next(100) < 55 ? 1 : 0);
            _curseur = 0;
        }

        public override void Avancer()
        {
            base.Avancer();
            if (T % 2 != 0) return;
            if (_curseur >= _blocs.Length) { Demarrer(); return; }
            if (_blocs[_curseur] == 1) _blocs[_curseur] = 3;
            else
            {
                // Case libre : on va chercher un bloc plus loin et on le ramene.
                for (int j = _blocs.Length - 1; j > _curseur; j--)
                    if (_blocs[j] == 1) { _blocs[j] = 0; _blocs[_curseur] = 2; break; }
                if (_blocs[_curseur] == 2) _blocs[_curseur] = 3;
            }
            _curseur++;
        }

        public override void Peindre(Graphics g)
        {
            Color[] teintes = { Color.FromArgb(40, 40, 40), Color.FromArgb(200, 200, 200),
                                Color.FromArgb(255, 230, 80), Color.FromArgb(60, 160, 255) };
            for (int i = 0; i < _blocs.Length; i++)
                g.FillRectangle(Theme.P(teintes[_blocs[i]]),
                                (i % _l) * Cote, 22 + (i / _l) * Cote, Cote - 1, Cote - 1);
            g.DrawString("Defragmentation du disque C:", Theme.Petite,
                         Theme.P(Color.White), 4, 4);
            int pourcent = _curseur * 100 / _blocs.Length;
            g.DrawRectangle(Theme.C(Color.Gray), 8, H - 22, L - 16, 12);
            g.FillRectangle(Theme.P(Color.FromArgb(60, 160, 255)),
                            9, H - 21, (L - 18) * pourcent / 100, 10);
            Centre(g, pourcent + " % termine", Theme.Minuscule, Color.White, H - 38);
        }
    }

    /// <summary>ScanDisk. La barre d'analyse, surface par surface.</summary>
    internal sealed class Scandisk : Effet
    {
        public override string Nom { get { return "SCANDISK"; } }
        private const int Cote = 10;
        private byte[] _cases;
        private int _l, _h, _curseur;

        protected override void Demarrer()
        {
            _l = L / Cote; _h = (H - 60) / Cote;
            _cases = new byte[_l * _h]; _curseur = 0;
        }

        public override void Avancer()
        {
            base.Avancer();
            if (_curseur < _cases.Length)
                _cases[_curseur++] = (byte)(Hasard.Next(60) == 0 ? 2 : 1);
            else if (T % 50 == 0) Demarrer();
        }

        public override void Peindre(Graphics g)
        {
            Fond(g, Color.FromArgb(0, 0, 140));
            g.DrawString("ScanDisk - verification de surface", Theme.Petite,
                         Theme.P(Color.White), 4, 6);
            for (int i = 0; i < _cases.Length; i++)
            {
                int x = (i % _l) * Cote + 2, y = 28 + (i / _l) * Cote;
                if (_cases[i] == 0)
                    g.DrawString(".", Theme.Petite, Theme.P(Color.Gray), x, y);
                else if (_cases[i] == 1)
                    g.DrawString("U", Theme.Petite, Theme.P(Color.FromArgb(160, 220, 160)), x, y);
                else
                    g.DrawString("B", Theme.PetiteGras, Theme.P(Color.FromArgb(255, 90, 90)), x, y);
            }
            int pourcent = _curseur * 100 / _cases.Length;
            g.DrawString("Termine a " + pourcent + " %   [U] utilise  [B] defectueux",
                         Theme.Minuscule, Theme.P(Color.LightGray), 4, H - 26);
        }
    }

    /// <summary>Une installation qui n'en finit pas, et sa barre de progression.</summary>
    internal sealed class Installation : Effet
    {
        public override string Nom { get { return "INSTALLATION"; } }
        private static readonly string[] Fichiers = {
            "INVENT32.DLL", "CODEBARR.SYS", "OPENFOOD.DAT", "SKORPIO.INF",
            "TAHOMA.TTF", "WEDGE.DRV", "FRIGO.HLP", "NUTRI.DAT", "BMP24.CDC",
            "COURSES.DLL", "VEILLE.SCR", "PXA270.BIN", "CE5CORE.DLL",
        };
        private int _avancement, _fichier;

        protected override void Demarrer() { _avancement = 0; _fichier = 0; }

        public override void Avancer()
        {
            base.Avancer();
            if (T % 3 != 0) return;
            // La progression ralentit vers la fin, comme il se doit.
            _avancement += _avancement < 80 ? Hasard.Next(3) : (Hasard.Next(6) == 0 ? 1 : 0);
            if (_avancement > 100) { _avancement = 0; _fichier = 0; }
            _fichier = _avancement * Fichiers.Length / 101;
        }

        public override void Peindre(Graphics g)
        {
            Fond(g, Color.FromArgb(0, 90, 140));
            int y = H / 2 - 54;
            g.FillRectangle(Theme.P(Color.FromArgb(210, 210, 205)), 14, y, L - 28, 108);
            g.FillRectangle(Theme.P(Color.FromArgb(0, 0, 130)), 14, y, L - 28, 16);
            g.DrawString("Installation de l'inventaire", Theme.PetiteGras,
                         Theme.P(Color.White), 18, y + 1);
            g.DrawString("Copie des fichiers...", Theme.Petite,
                         Theme.P(Color.Black), 22, y + 26);
            g.DrawString(Fichiers[_fichier % Fichiers.Length], Theme.Minuscule,
                         Theme.P(Color.FromArgb(60, 60, 60)), 22, y + 42);
            g.DrawRectangle(Theme.C(Color.Gray), 22, y + 62, L - 44, 16);
            // Barre en pas de huit pixels : c'est ainsi qu'elle se remplissait.
            int plein = (L - 46) * _avancement / 100;
            for (int x = 0; x < plein; x += 8)
                g.FillRectangle(Theme.P(Color.FromArgb(0, 0, 140)), 23 + x, y + 63, 7, 14);
            g.DrawString(_avancement + " %", Theme.Petite, Theme.P(Color.Black),
                         L / 2 - 12, y + 84);
        }
    }

    /// <summary>Le journal de demarrage qui defile trop vite pour etre lu.</summary>
    internal sealed class JournalDemarrage : Effet
    {
        public override string Nom { get { return "DEMARRAGE"; } }
        private static readonly string[] Modeles = {
            "CE5: kernel init ok", "CE5: mapping 0x{0:X6}", "pxa270: core @520MHz",
            "flash: block {1} verified", "wifi: scanning...", "wifi: assoc SSID=MAISON",
            "wifi: dhcp lease 192.168.1.{1}", "scanner: laser warm-up",
            "scanner: decoder EAN13 ok", "audio: coredll waveOut ok",
            "batt: {1}% charging", "touch: calibration loaded",
            "fs: \\FlashDisk mounted", "net: gateway reachable",
            "inventaire: contacting 192.168.1.24:8080", "inventaire: ping 31 ms",
            "inventaire: {1} unites en base", "photo: bmp8 decoder ready",
        };
        private string[] _lignes;
        private int _n;

        protected override void Demarrer()
        {
            _lignes = new string[H / 12 + 2]; _n = 0;
        }

        public override void Avancer()
        {
            base.Avancer();
            if (T % 3 != 0) return;
            string ligne = string.Format(Modeles[Hasard.Next(Modeles.Length)],
                                         Hasard.Next(0xFFFFFF), Hasard.Next(100));
            if (_n < _lignes.Length) _lignes[_n++] = ligne;
            else
            {
                for (int i = 1; i < _lignes.Length; i++) _lignes[i - 1] = _lignes[i];
                _lignes[_lignes.Length - 1] = ligne;
            }
        }

        public override void Peindre(Graphics g)
        {
            for (int i = 0; i < _n; i++)
            {
                if (_lignes[i] == null) continue;
                int clarte = 120 + i * 130 / _lignes.Length;
                g.DrawString("[ " + (i * 3 + 1) + " ] " + _lignes[i], Theme.Minuscule,
                             Theme.P(Color.FromArgb(clarte / 3, clarte, clarte / 2)),
                             3, i * 12 + 2);
            }
        }
    }

    /// <summary>Vidage hexadecimal : la memoire telle qu'on la lisait.</summary>
    internal sealed class Hexa : Effet
    {
        public override string Nom { get { return "VIDAGE MEMOIRE"; } }
        private byte[] _octets;
        private int _base;

        protected override void Demarrer()
        {
            _octets = new byte[1024];
            Hasard.NextBytes(_octets);
            _base = 0x1C0000;
        }

        public override void Avancer()
        {
            base.Avancer();
            if (T % 6 != 0) return;
            _base += 8;
            for (int i = 0; i < 8; i++) _octets[Hasard.Next(_octets.Length)] = (byte)Hasard.Next(256);
        }

        public override void Peindre(Graphics g)
        {
            int rangees = H / 12;
            for (int r = 0; r < rangees; r++)
            {
                int adresse = _base + r * 8;
                string hexa = "", texte = "";
                for (int i = 0; i < 8; i++)
                {
                    byte v = _octets[(adresse + i) % _octets.Length];
                    hexa += v.ToString("X2") + " ";
                    texte += v >= 32 && v < 127 ? ((char)v).ToString() : ".";
                }
                g.DrawString(adresse.ToString("X6"), Theme.Minuscule,
                             Theme.P(Color.FromArgb(90, 150, 200)), 2, r * 12 + 2);
                g.DrawString(hexa, Theme.Minuscule,
                             Theme.P(Color.FromArgb(200, 210, 220)), 48, r * 12 + 2);
                g.DrawString(texte, Theme.Minuscule,
                             Theme.P(Color.FromArgb(120, 220, 140)), L - 56, r * 12 + 2);
            }
        }
    }

    /// <summary>Le test memoire du POST, barrette par barrette.</summary>
    internal sealed class TestMemoire : Effet
    {
        public override string Nom { get { return "TEST MEMOIRE"; } }
        private int _teste;
        private const int Total = 65536;

        protected override void Demarrer() { _teste = 0; }

        public override void Avancer()
        {
            base.Avancer();
            _teste += 512 + Hasard.Next(512);
            if (_teste > Total) _teste = 0;
        }

        public override void Peindre(Graphics g)
        {
            int y = H / 2 - 40;
            g.DrawString("Award Modular BIOS v4.51PG", Theme.Petite,
                         Theme.P(Color.FromArgb(200, 200, 200)), 6, 6);
            g.DrawString("Marvell PXA270  520 MHz", Theme.Petite,
                         Theme.P(Color.FromArgb(160, 160, 160)), 6, 20);
            g.DrawString("Memory Test :", Theme.Petite,
                         Theme.P(Color.FromArgb(200, 200, 200)), 6, y);
            g.DrawString(_teste + " K OK", Theme.Grande,
                         Theme.P(Color.FromArgb(120, 255, 140)), 6, y + 16);
            int barre = (L - 20) * _teste / Total;
            g.DrawRectangle(Theme.C(Color.Gray), 6, y + 44, L - 20, 10);
            g.FillRectangle(Theme.P(Color.FromArgb(120, 255, 140)), 7, y + 45, barre, 8);
            g.DrawString("Detecting IDE drives ...", Theme.Minuscule,
                         Theme.P(Color.FromArgb(150, 150, 150)), 6, y + 64);
            if ((T / 6) % 4 == 0)
                g.DrawString("|", Theme.Petite, Theme.P(Color.White), L - 16, y + 62);
        }
    }

    /// <summary>Les fenetres volantes de Windows 3.1, en fuite vers le fond.</summary>
    internal sealed class FenetresVolantes : Effet
    {
        public override string Nom { get { return "FENETRES VOLANTES"; } }
        private const int N = 44;
        private int[] _x, _y, _z;

        protected override void Demarrer()
        {
            _x = new int[N]; _y = new int[N]; _z = new int[N];
            for (int i = 0; i < N; i++) Semer(i, true);
        }

        private void Semer(int i, bool partout)
        {
            // Une dispersion de +/-260 garde la plupart des fenetres dans le
            // cadre jusqu'a ce qu'elles le frolent : a +/-500, elles sortaient
            // de l'ecran avant d'avoir grossi assez pour etre vues.
            _x[i] = Hasard.Next(-260, 260);
            _y[i] = Hasard.Next(-260, 260);
            _z[i] = partout ? 90 + Hasard.Next(650) : 740;
        }

        public override void Avancer()
        {
            base.Avancer();
            for (int i = 0; i < N; i++)
            {
                _z[i] -= 12;
                if (_z[i] < 40) Semer(i, false);
            }
        }

        public override void Peindre(Graphics g)
        {
            int cx = L / 2, cy = H / 2;
            for (int i = 0; i < N; i++)
            {
                int x = cx + _x[i] * 200 / _z[i], y = cy + _y[i] * 200 / _z[i];
                int taille = 3600 / _z[i];
                if (taille < 6 || x < -40 || y < -40 || x > L || y > H) continue;
                // Les quatre carreaux du drapeau, sans la courbure. Un demi
                // carreau plein, et non `d - 1` : a petite taille, retirer un
                // pixel donnait un rectangle de cote nul, donc invisible.
                int d = taille / 2;
                g.FillRectangle(Theme.P(Color.FromArgb(230, 70, 60)), x, y, d, d);
                g.FillRectangle(Theme.P(Color.FromArgb(110, 200, 90)), x + d + 1, y, d, d);
                g.FillRectangle(Theme.P(Color.FromArgb(70, 140, 230)), x, y + d + 1, d, d);
                g.FillRectangle(Theme.P(Color.FromArgb(250, 210, 70)), x + d + 1, y + d + 1, d, d);
            }
        }
    }

    /// <summary>Oscilloscope : deux traces, une grille, un balayage.</summary>
    internal sealed class Oscilloscope : Effet
    {
        public override string Nom { get { return "OSCILLOSCOPE"; } }

        public override void Peindre(Graphics g)
        {
            Fond(g, Color.FromArgb(4, 16, 8));
            Color grille = Color.FromArgb(20, 60, 30);
            for (int x = 0; x < L; x += L / 10) g.DrawLine(Theme.C(grille), x, 0, x, H);
            for (int y = 0; y < H; y += H / 8) g.DrawLine(Theme.C(grille), 0, y, L, y);
            int c1 = H / 3, c2 = H * 2 / 3;
            for (int x = 0; x < L; x++)
            {
                int a = x * 4 + T * 6;
                int y1 = c1 + S(a) * 34 / 1024;
                // Voie 2 : un signal carre, comme une sortie logique.
                int y2 = c2 + ((S(a * 2 / 3) > 0) ? -26 : 26);
                g.FillRectangle(Theme.P(Color.FromArgb(120, 255, 150)), x, y1, 2, 2);
                g.FillRectangle(Theme.P(Color.FromArgb(255, 220, 90)), x, y2, 2, 2);
            }
            g.DrawString("CH1 50mV  CH2 2V  2ms/div", Theme.Minuscule,
                         Theme.P(Color.FromArgb(120, 200, 140)), 4, H - 14);
        }
    }

    /// <summary>Vumetre a aiguille, comme sur un magnetophone a cassette.</summary>
    internal sealed class Vumetre : Effet
    {
        public override string Nom { get { return "VUMETRE"; } }
        private int _aiguille, _cible;

        public override void Avancer()
        {
            base.Avancer();
            if (T % 6 == 0) _cible = Hasard.Next(100);
            // L'aiguille monte vite et redescend lentement : c'est la balistique
            // d'un vrai vumetre, et c'est ce qui le rend agreable a regarder.
            _aiguille += _cible > _aiguille ? (_cible - _aiguille) / 2 + 1
                                            : (_cible - _aiguille) / 8 - 1;
            if (_aiguille < 0) _aiguille = 0;
            if (_aiguille > 100) _aiguille = 100;
        }

        public override void Peindre(Graphics g)
        {
            Fond(g, Color.FromArgb(235, 225, 195));
            int cx = L / 2, cy = H * 3 / 4, r = Math.Min(L, H) / 2 - 20;
            for (int i = 0; i <= 20; i++)
            {
                int a = 160 + i * 48 / 20;         // de 160 a 208 sur 256 pas
                bool rouge = i > 15;
                int r1 = r - (i % 5 == 0 ? 14 : 7);
                g.DrawLine(Theme.C(rouge ? Color.FromArgb(180, 30, 30) : Color.FromArgb(40, 40, 40)),
                           cx + C(a) * r1 / 1024, cy + S(a) * r1 / 1024,
                           cx + C(a) * r / 1024, cy + S(a) * r / 1024);
            }
            int angle = 160 + _aiguille * 48 / 100;
            g.DrawLine(Theme.C(Color.FromArgb(20, 20, 20)), cx, cy,
                       cx + C(angle) * (r - 6) / 1024, cy + S(angle) * (r - 6) / 1024);
            g.FillRectangle(Theme.P(Color.FromArgb(60, 60, 60)), cx - 3, cy - 3, 7, 7);
            g.DrawString("VU", Theme.PetiteGras, Theme.P(Color.FromArgb(60, 60, 60)),
                         cx - 8, cy - 34);
            // Chaque graduation posee sous son propre trait, et non en une
            // seule chaine : alignee a plat, elle traversait l'arc.
            string[] crans = { "-20", "-10", "-5", "0", "+3" };
            for (int i = 0; i < crans.Length; i++)
            {
                int a = 160 + i * 48 / (crans.Length - 1);
                int rx = cx + C(a) * (r + 10) / 1024 - 7;
                int ry = cy + S(a) * (r + 10) / 1024 - 6;
                g.DrawString(crans[i], Theme.Minuscule,
                             Theme.P(i == crans.Length - 1 ? Color.FromArgb(170, 30, 30)
                                                           : Color.FromArgb(80, 80, 80)),
                             rx, ry);
            }
        }
    }

    /// <summary>Egaliseur a barres, avec les temoins de crete qui retombent.</summary>
    internal sealed class Egaliseur : Effet
    {
        public override string Nom { get { return "EGALISEUR"; } }
        private const int Bandes = 12;
        private int[] _niveau, _crete;

        protected override void Demarrer()
        {
            _niveau = new int[Bandes]; _crete = new int[Bandes];
        }

        public override void Avancer()
        {
            base.Avancer();
            for (int i = 0; i < Bandes; i++)
            {
                int cible = 20 + (S(T * (3 + i % 4) + i * 21) + 1024) * 70 / 2048
                            + Hasard.Next(14);
                _niveau[i] += cible > _niveau[i] ? 9 : -5;
                if (_niveau[i] < 0) _niveau[i] = 0;
                if (_niveau[i] > 100) _niveau[i] = 100;
                if (_niveau[i] > _crete[i]) _crete[i] = _niveau[i];
                else if (T % 3 == 0 && _crete[i] > 0) _crete[i]--;
            }
        }

        public override void Peindre(Graphics g)
        {
            int largeur = L / Bandes, hauteur = H - 40;
            for (int i = 0; i < Bandes; i++)
            {
                int x = i * largeur + 2;
                int crans = hauteur / 7;
                for (int k = 0; k < crans; k++)
                {
                    int part = k * 100 / crans;
                    if (part > _niveau[i]) break;
                    Color teinte = part > 82 ? Color.FromArgb(255, 70, 60)
                        : part > 62 ? Color.FromArgb(255, 200, 60)
                        : Color.FromArgb(80, 230, 110);
                    g.FillRectangle(Theme.P(teinte), x, H - 20 - k * 7, largeur - 4, 5);
                }
                int y = H - 20 - (_crete[i] * crans / 100) * 7;
                g.FillRectangle(Theme.P(Color.White), x, y, largeur - 4, 2);
            }
        }
    }

    /// <summary>Mire de television, barres SMPTE comprises.</summary>
    internal sealed class Mire : Effet
    {
        public override string Nom { get { return "MIRE"; } }
        private static readonly Color[] Barres = {
            Color.FromArgb(192,192,192), Color.FromArgb(192,192,0),
            Color.FromArgb(0,192,192), Color.FromArgb(0,192,0),
            Color.FromArgb(192,0,192), Color.FromArgb(192,0,0),
            Color.FromArgb(0,0,192),
        };

        public override void Peindre(Graphics g)
        {
            int haut = H * 2 / 3, largeur = L / Barres.Length;
            for (int i = 0; i < Barres.Length; i++)
                g.FillRectangle(Theme.P(Barres[i]), i * largeur, 0, largeur + 1, haut);
            for (int i = 0; i < Barres.Length; i++)
                g.FillRectangle(Theme.P(Barres[Barres.Length - 1 - i]),
                                i * largeur, haut, largeur + 1, H / 12);
            int y = haut + H / 12;
            int cases = 16;
            for (int i = 0; i < cases; i++)
                g.FillRectangle(Theme.P(Gris(i * 255 / (cases - 1))),
                                i * L / cases, y, L / cases + 1, H - y);
            g.DrawString("INVENTAIRE  " + (T / 25) + "s", Theme.PetiteGras,
                         Theme.P(Color.Black), 6, haut + 2);
        }
    }

    /// <summary>Neige : l'ecran d'un televiseur sans signal.</summary>
    internal sealed class Neige : Effet
    {
        public override string Nom { get { return "PAS DE SIGNAL"; } }
        private const int Cote = 3;

        public override void Peindre(Graphics g)
        {
            for (int y = 0; y < H; y += Cote)
                for (int x = 0; x < L; x += Cote)
                    Bloc(g, x, y, Cote, Gris(Hasard.Next(256)));
            // La barre de trame qui remonte lentement, defaut de synchro bien
            // connu des televiseurs cathodiques.
            int barre = H - (T * 3) % (H + 40);
            for (int d = 0; d < 18; d++)
                g.FillRectangle(Theme.P(Gris(150 + d * 6)), 0, barre + d, L, 1);
        }
    }

    /// <summary>Horloge a aiguilles, trotteuse comprise.</summary>
    internal sealed class Horloge : Effet
    {
        public override string Nom { get { return "HORLOGE"; } }

        public override void Peindre(Graphics g)
        {
            DateTime maintenant = DateTime.Now;
            int cx = L / 2, cy = H / 2, r = Math.Min(L, H) / 2 - 14;
            for (int i = 0; i < 60; i++)
            {
                int a = i * 256 / 60 - 64;
                int r1 = r - (i % 5 == 0 ? 10 : 4);
                g.DrawLine(Theme.C(i % 5 == 0 ? Color.White : Color.FromArgb(90, 90, 90)),
                           cx + C(a) * r1 / 1024, cy + S(a) * r1 / 1024,
                           cx + C(a) * r / 1024, cy + S(a) * r / 1024);
            }
            Aiguille(g, cx, cy, (maintenant.Hour % 12) * 256 / 12
                     + maintenant.Minute * 256 / 720 - 64, r / 2, Color.White, 3);
            Aiguille(g, cx, cy, maintenant.Minute * 256 / 60 - 64, r * 3 / 4,
                     Color.White, 2);
            Aiguille(g, cx, cy, maintenant.Second * 256 / 60 - 64, r * 4 / 5,
                     Color.FromArgb(255, 90, 90), 1);
            Centre(g, maintenant.ToString("HH:mm:ss"), Theme.Petite,
                   Color.FromArgb(140, 140, 140), cy + r / 3);
        }

        private static void Aiguille(Graphics g, int cx, int cy, int angle, int longueur,
                                     Color teinte, int epaisseur)
        {
            for (int e = 0; e < epaisseur; e++)
                g.DrawLine(Theme.C(teinte), cx + e, cy,
                           cx + e + C(angle) * longueur / 1024,
                           cy + S(angle) * longueur / 1024);
        }
    }

    /// <summary>Horloge a volets, celle des gares et des radios-reveils.</summary>
    internal sealed class HorlogeVolets : Effet
    {
        public override string Nom { get { return "HORLOGE A VOLETS"; } }
        private string _affiche = "";
        private int _bascule;

        public override void Avancer()
        {
            base.Avancer();
            string maintenant = DateTime.Now.ToString("HH:mm:ss");
            if (maintenant != _affiche) { _affiche = maintenant; _bascule = 6; }
            else if (_bascule > 0) _bascule--;
        }

        public override void Peindre(Graphics g)
        {
            Fond(g, Color.FromArgb(18, 18, 20));
            if (_affiche.Length == 0) _affiche = DateTime.Now.ToString("HH:mm:ss");
            string[] blocs = _affiche.Split(':');
            int largeur = 62, hauteur = 52, y = H / 2 - hauteur - 30;
            for (int i = 0; i < blocs.Length; i++)
            {
                int x = L / 2 - largeur / 2;
                int yy = y + i * (hauteur + 10);
                g.FillRectangle(Theme.P(Color.FromArgb(35, 35, 40)), x, yy, largeur, hauteur);
                // La charniere : le trait noir qui coupe le volet en deux.
                g.FillRectangle(Theme.P(Color.FromArgb(12, 12, 14)),
                                x, yy + hauteur / 2 - 1, largeur, 2);
                int decalage = (i == blocs.Length - 1 && _bascule > 0) ? _bascule : 0;
                g.DrawString(blocs[i], Theme.Enorme,
                             Theme.P(Color.FromArgb(235, 235, 230)),
                             x + 8, yy + 8 + decalage);
            }
        }
    }

    /// <summary>Horloge binaire : six colonnes de bits, une par chiffre.</summary>
    internal sealed class HorlogeBinaire : Effet
    {
        public override string Nom { get { return "HORLOGE BINAIRE"; } }

        public override void Peindre(Graphics g)
        {
            DateTime n = DateTime.Now;
            int[] chiffres = { n.Hour / 10, n.Hour % 10, n.Minute / 10, n.Minute % 10,
                               n.Second / 10, n.Second % 10 };
            int cote = 22, x0 = (L - chiffres.Length * cote) / 2, y0 = H / 2 - 50;
            for (int c = 0; c < chiffres.Length; c++)
                for (int b = 3; b >= 0; b--)
                {
                    bool allume = (chiffres[c] & (1 << b)) != 0;
                    int x = x0 + c * cote, y = y0 + (3 - b) * cote;
                    g.FillRectangle(Theme.P(allume ? Color.FromArgb(80, 240, 160)
                                                   : Color.FromArgb(22, 42, 34)),
                                    x + 2, y + 2, cote - 5, cote - 5);
                }
            Centre(g, n.ToString("HH:mm:ss"), Theme.Petite,
                   Color.FromArgb(90, 140, 120), y0 + 4 * cote + 12);
        }
    }

    /// <summary>Tubes Nixie : les chiffres orange d'avant l'affichage a cristaux.</summary>
    internal sealed class Nixie : Effet
    {
        public override string Nom { get { return "TUBES NIXIE"; } }

        public override void Peindre(Graphics g)
        {
            Fond(g, Color.FromArgb(10, 6, 4));
            string heure = DateTime.Now.ToString("HHmmss");
            int largeur = 34, hauteur = 62, x0 = (L - heure.Length * largeur) / 2;
            int y0 = H / 2 - hauteur / 2;
            for (int i = 0; i < heure.Length; i++)
            {
                int x = x0 + i * largeur;
                g.FillRectangle(Theme.P(Color.FromArgb(24, 18, 14)), x + 2, y0, largeur - 5, hauteur);
                // Le halo : deux passes decalees, la lueur d'un tube qui chauffe.
                g.DrawString(heure.Substring(i, 1), Theme.Enorme,
                             Theme.P(Color.FromArgb(120, 40, 0)), x + 7, y0 + 15);
                g.DrawString(heure.Substring(i, 1), Theme.Enorme,
                             Theme.P(Color.FromArgb(255, 150, 40)), x + 6, y0 + 14);
                g.DrawRectangle(Theme.C(Color.FromArgb(60, 50, 40)), x + 2, y0, largeur - 5, hauteur);
            }
        }
    }

    /// <summary>Code-barres defilant. L'objet meme de cet appareil.</summary>
    internal sealed class CodeBarres : Effet
    {
        public override string Nom { get { return "EAN 13"; } }
        private string _code = "3017620422003";
        private int _x;

        protected override void Demarrer() { _x = L; Tirer(); }

        private void Tirer()
        {
            char[] chiffres = new char[13];
            for (int i = 0; i < 13; i++) chiffres[i] = (char)('0' + Hasard.Next(10));
            _code = new string(chiffres);
        }

        public override void Avancer()
        {
            base.Avancer();
            _x -= 4;
            if (_x < -180) { _x = L; Tirer(); }
        }

        public override void Peindre(Graphics g)
        {
            Fond(g, Color.FromArgb(245, 245, 240));
            int y = H / 2 - 60, hauteur = 110, x = _x;
            // Les largeurs derivent des chiffres : ce n'est pas un vrai EAN-13,
            // mais le rythme est le bon.
            for (int i = 0; i < _code.Length; i++)
            {
                int n = _code[i] - '0';
                for (int b = 0; b < 4; b++)
                {
                    int epaisseur = 1 + ((n >> b) & 1) * 2;
                    if (x > -6 && x < L)
                        g.FillRectangle(Theme.P(Color.Black), x, y, epaisseur, hauteur);
                    x += epaisseur + 1 + (b & 1);
                }
            }
            g.DrawString(_code, Theme.Grande, Theme.P(Color.Black), _x, y + hauteur + 4);
            // Le trait du lecteur, qui balaie.
            int laser = (T * 7) % H;
            g.FillRectangle(Theme.P(Color.FromArgb(255, 40, 40)), 0, laser, L, 2);
        }
    }

    /// <summary>Teleimprimeur : le ruban du telex, caractere par caractere.</summary>
    internal sealed class Telex : Effet
    {
        public override string Nom { get { return "TELEX"; } }
        private static readonly string[] Depeches = {
            "STOP NUTELLA PERIME DANS 5 JOURS STOP",
            "STOP LISTE DE COURSES 3 ARTICLES STOP",
            "STOP TERMINAL SKORPIO EN LIGNE STOP",
            "STOP OPEN FOOD FACTS REPOND EN 31 MS STOP",
            "STOP FRAICHEUR DU FRIGO 67 SUR 100 STOP",
            "STOP RIEN A SIGNALER SUR LE RAYON YAOURTS STOP",
        };
        private string _texte = "";
        private int _n;

        protected override void Demarrer()
        {
            _texte = Depeches[Hasard.Next(Depeches.Length)]; _n = 0;
        }

        public override void Avancer()
        {
            base.Avancer();
            if (T % 2 != 0) return;
            _n++;
            if (_n > _texte.Length + 20) Demarrer();
        }

        public override void Peindre(Graphics g)
        {
            Fond(g, Color.FromArgb(232, 226, 208));
            g.FillRectangle(Theme.P(Color.FromArgb(212, 204, 184)), 0, H / 2 - 46, L, 92);
            // Les perforations d'entrainement du ruban.
            for (int x = (T * 2) % 12; x < L; x += 12)
                g.FillRectangle(Theme.P(Color.FromArgb(150, 144, 130)), x, H / 2 - 2, 4, 4);
            string visible = _texte.Substring(0, Math.Min(_n, _texte.Length));
            int debut = Math.Max(0, visible.Length - 22);
            g.DrawString(visible.Substring(debut), Theme.Grande,
                         Theme.P(Color.FromArgb(30, 30, 30)), 6, H / 2 - 38);
            if ((T / 4) % 2 == 0 && _n <= _texte.Length)
                g.FillRectangle(Theme.P(Color.FromArgb(30, 30, 30)),
                                6 + (visible.Length - debut) * 11, H / 2 - 36, 8, 16);
        }
    }
}
