using System;
using System.Drawing;

namespace Inventaire
{
    // ====================================================================
    //  Vingt effets de demoscene, des annees 1985 a 2000. Tous en entiers,
    //  tous en blocs : voir l'en-tete d'Effet.cs.
    // ====================================================================

    /// <summary>Plasma. Somme de sinus, la figure imposee de toute demo.</summary>
    internal sealed class Plasma : Effet
    {
        public override string Nom { get { return "PLASMA"; } }
        private const int Cote = 6;
        private Color[] _palette;

        protected override void Demarrer()
        {
            _palette = new Color[256];
            for (int i = 0; i < 256; i++)
                _palette[i] = Roue(i);
        }

        public override void Peindre(Graphics g)
        {
            for (int y = 0; y < H; y += Cote)
                for (int x = 0; x < L; x += Cote)
                {
                    int v = S(x / 3 + T) + S(y / 4 - T) + S((x + y) / 5 + T * 2);
                    Bloc(g, x, y, Cote, _palette[((v / 24) + 512) & 255]);
                }
        }
    }

    /// <summary>Le feu de Doom : une braise au bas, moyennee vers le haut.</summary>
    internal sealed class Feu : Effet
    {
        public override string Nom { get { return "FEU"; } }
        private const int Cote = 5;
        private int[] _braise;
        private int _l, _h;
        private Color[] _palette;

        protected override void Demarrer()
        {
            _l = L / Cote + 1; _h = H / Cote + 1;
            _braise = new int[_l * _h];
            _palette = new Color[37];
            for (int i = 0; i < 37; i++)
            {
                // Noir -> rouge -> orange -> jaune -> blanc, comme la palette
                // d'origine de 1993.
                int n = i * 255 / 36;
                _palette[i] = i < 12 ? Color.FromArgb(n * 2, 0, 0)
                    : i < 24 ? Color.FromArgb(255, (i - 12) * 21, 0)
                    : Color.FromArgb(255, 255, (i - 24) * 20);
            }
        }

        public override void Avancer()
        {
            base.Avancer();
            for (int x = 0; x < _l; x++)
                _braise[(_h - 1) * _l + x] = 24 + Hasard.Next(12);
            for (int y = _h - 2; y >= 0; y--)
                for (int x = 0; x < _l; x++)
                {
                    int gauche = x > 0 ? _braise[(y + 1) * _l + x - 1] : 0;
                    int droite = x < _l - 1 ? _braise[(y + 1) * _l + x + 1] : 0;
                    int dessous = _braise[(y + 1) * _l + x];
                    int v = (gauche + droite + dessous * 2) / 4 - Hasard.Next(2);
                    _braise[y * _l + x] = v < 0 ? 0 : v;
                }
        }

        public override void Peindre(Graphics g)
        {
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _l; x++)
                {
                    int v = _braise[y * _l + x];
                    if (v > 0) Bloc(g, x * Cote, y * Cote, Cote, _palette[v]);
                }
        }
    }

    /// <summary>Tunnel. Table de rayon et d'angle calculee une fois.</summary>
    internal sealed class Tunnel : Effet
    {
        public override string Nom { get { return "TUNNEL"; } }
        private const int Cote = 6;
        private int[] _rayon, _angle;
        private int _l, _h;

        protected override void Demarrer()
        {
            _l = L / Cote + 1; _h = H / Cote + 1;
            _rayon = new int[_l * _h]; _angle = new int[_l * _h];
            int cx = _l / 2, cy = _h / 2;
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _l; x++)
                {
                    int dx = x - cx, dy = y - cy;
                    int d = Racine(dx * dx + dy * dy);
                    // 1/d donne la profondeur ; le +1 evite la division par zero
                    // au centre exact.
                    _rayon[y * _l + x] = 900 / (d + 1);
                    _angle[y * _l + x] = Atan(dy, dx);
                }
        }

        /// <summary>Arc-tangente approchee, sur 256 pas. Sans flottant.</summary>
        private static int Atan(int y, int x)
        {
            if (x == 0 && y == 0) return 0;
            int ax = x < 0 ? -x : x, ay = y < 0 ? -y : y;
            int r = ax + ay == 0 ? 0 : (ax - ay) * 32 / (ax + ay);
            int a = x >= 0 ? 32 - r : 96 + r;
            return (y < 0 ? -a : a) & 255;
        }

        public override void Peindre(Graphics g)
        {
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _l; x++)
                {
                    int i = y * _l + x;
                    // Damier en profondeur : un XOR des deux coordonnees polaires.
                    int damier = ((_rayon[i] + T * 2) / 8 + (_angle[i] / 16)) & 1;
                    int lueur = _rayon[i] * 3;
                    if (lueur > 255) lueur = 255;
                    Color teinte = damier == 0 ? Gris(255 - lueur)
                        : Color.FromArgb(0, (255 - lueur) / 2, 255 - lueur);
                    Bloc(g, x * Cote, y * Cote, Cote, teinte);
                }
        }
    }

    /// <summary>Rotozoom : un damier qu'on tourne et qu'on zoome.</summary>
    internal sealed class Rotozoom : Effet
    {
        public override string Nom { get { return "ROTOZOOM"; } }
        private const int Cote = 6;

        public override void Peindre(Graphics g)
        {
            int zoom = 600 + S(T * 2) / 3;          // respiration du zoom
            int cos = C(T), sin = S(T);
            int cx = L / 2, cy = H / 2;
            for (int y = 0; y < H; y += Cote)
                for (int x = 0; x < L; x += Cote)
                {
                    int dx = x - cx, dy = y - cy;
                    int u = (dx * cos - dy * sin) / 1024 * zoom / 512;
                    int v = (dx * sin + dy * cos) / 1024 * zoom / 512;
                    int damier = ((u >> 4) + (v >> 4)) & 1;
                    Bloc(g, x, y, Cote, damier == 0
                        ? Color.FromArgb(20, 140, 200) : Color.FromArgb(240, 210, 60));
                }
        }
    }

    /// <summary>Metaballs : trois sources, une isosurface.</summary>
    internal sealed class Metaballs : Effet
    {
        public override string Nom { get { return "METABALLS"; } }
        private const int Cote = 6, N = 3;
        private int[] _bx, _by;

        protected override void Demarrer()
        {
            _bx = new int[N]; _by = new int[N];
        }

        public override void Peindre(Graphics g)
        {
            for (int i = 0; i < N; i++)
            {
                _bx[i] = L / 2 + S(T * (2 + i) + i * 80) * (L / 3) / 1024;
                _by[i] = H / 2 + C(T * (3 - i) + i * 50) * (H / 3) / 1024;
            }
            for (int y = 0; y < H; y += Cote)
                for (int x = 0; x < L; x += Cote)
                {
                    int champ = 0;
                    for (int i = 0; i < N; i++)
                    {
                        int dx = x - _bx[i], dy = y - _by[i];
                        int d2 = dx * dx + dy * dy;
                        champ += 90000 / (d2 + 90);
                    }
                    if (champ < 12) continue;
                    Color teinte = champ > 60 ? Color.White
                        : champ > 34 ? Color.FromArgb(120, 255, 230)
                        : Color.FromArgb(0, 90 + champ * 3, 120 + champ * 2);
                    Bloc(g, x, y, Cote, teinte);
                }
        }
    }

    /// <summary>Figures de Lissajous, comme sur un oscilloscope a deux voies.</summary>
    internal sealed class Lissajous : Effet
    {
        public override string Nom { get { return "LISSAJOUS"; } }
        private const int Traine = 200;
        private int _a = 3, _b = 2;

        protected override void Demarrer()
        {
            _a = 2 + Hasard.Next(4); _b = 2 + Hasard.Next(5);
        }

        public override void Peindre(Graphics g)
        {
            int cx = L / 2, cy = H / 2, rx = L / 2 - 12, ry = H / 3;
            for (int i = 0; i < Traine; i++)
            {
                int p = T * 2 + i;
                int x = cx + S(p * _a) * rx / 1024;
                int y = cy + S(p * _b + 40) * ry / 1024;
                int clarte = 255 - i * 255 / Traine;
                Bloc(g, x, y, 2, Color.FromArgb(clarte / 4, clarte, clarte / 2));
            }
        }
    }

    /// <summary>Spirographe : un cercle qui roule dans un autre.</summary>
    internal sealed class Spirographe : Effet
    {
        public override string Nom { get { return "SPIROGRAPHE"; } }
        private int _r, _d;
        private int[] _x, _y;
        private int _n;

        protected override void Demarrer()
        {
            _r = 2 + Hasard.Next(5); _d = 30 + Hasard.Next(50);
            _x = new int[600]; _y = new int[600]; _n = 0;
        }

        public override void Avancer()
        {
            base.Avancer();
            for (int k = 0; k < 4 && _n < _x.Length; k++)
            {
                int p = _n * 2;
                int grand = L / 2 - 16;
                _x[_n] = L / 2 + (S(p) * grand + S(p * _r) * _d) / 1024;
                _y[_n] = H / 2 + (C(p) * grand + C(p * _r) * _d) / 1024;
                _n++;
            }
            if (_n >= _x.Length) Demarrer();
        }

        public override void Peindre(Graphics g)
        {
            for (int i = 0; i < _n; i++)
                Bloc(g, _x[i], _y[i], 2, Roue(i / 2));
        }
    }

    /// <summary>L'attracteur de Lorenz, en projection sur deux axes.</summary>
    internal sealed class Lorenz : Effet
    {
        public override string Nom { get { return "LORENZ"; } }
        private int _x, _y, _z;              // x 1000
        private int[] _tx, _ty;
        private int _n;

        protected override void Demarrer()
        {
            _x = 1000; _y = 1000; _z = 20000;
            _tx = new int[500]; _ty = new int[500]; _n = 0;
        }

        public override void Avancer()
        {
            base.Avancer();
            for (int k = 0; k < 6; k++)
            {
                // sigma=10, rho=28, beta=8/3, pas=0,004 -- en millièmes.
                int dx = 10 * (_y - _x) * 4 / 1000;
                int dy = (_x * (28000 - _z) / 1000 - _y) * 4 / 1000;
                int dz = (_x * _y / 1000 - 8 * _z / 3) * 4 / 1000;
                _x += dx; _y += dy; _z += dz;
                // File glissante : une fois pleine, la trace defile au lieu de
                // s'effacer d'un coup. L'attracteur reste dessine en permanence.
                if (_n < _tx.Length) _n++;
                else
                {
                    Array.Copy(_tx, 1, _tx, 0, _tx.Length - 1);
                    Array.Copy(_ty, 1, _ty, 0, _ty.Length - 1);
                }
                _tx[_n - 1] = L / 2 + _x * (L / 2 - 10) / 22000;
                _ty[_n - 1] = H - 20 - _z * (H - 40) / 50000;
            }
        }

        public override void Peindre(Graphics g)
        {
            for (int i = 0; i < _n; i++)
                Bloc(g, _tx[i], _ty[i], 2, Roue(i / 3 + 120));
        }
    }

    /// <summary>Le jeu du chaos : trois sommets, un point, une moitie a la fois.</summary>
    internal sealed class Sierpinski : Effet
    {
        public override string Nom { get { return "SIERPINSKI"; } }
        private int[] _px, _py;
        private int _n, _x, _y;

        protected override void Demarrer()
        {
            _px = new int[3000]; _py = new int[3000]; _n = 0;
            _x = L / 2; _y = H / 2;
        }

        public override void Avancer()
        {
            base.Avancer();
            int[] sx = { L / 2, 8, L - 8 };
            int[] sy = { 24, H - 24, H - 24 };
            for (int k = 0; k < 60 && _n < _px.Length; k++)
            {
                int s = Hasard.Next(3);
                _x = (_x + sx[s]) / 2; _y = (_y + sy[s]) / 2;
                _px[_n] = _x; _py[_n] = _y; _n++;
            }
        }

        public override void Peindre(Graphics g)
        {
            for (int i = 0; i < _n; i++)
                Bloc(g, _px[i], _py[i], 1, Roue(i / 12 + 80));
        }
    }

    /// <summary>Mandelbrot, en entiers a virgule fixe, devoile ligne par ligne.</summary>
    internal sealed class Mandelbrot : Effet
    {
        public override string Nom { get { return "MANDELBROT"; } }
        private const int Cote = 4, Max = 40, Un = 4096;
        private int _ligne;

        protected override void Demarrer() { _ligne = 0; }

        public override void Avancer()
        {
            base.Avancer();
            _ligne += Cote * 3;                 // trois rangees par image
            if (_ligne > H) _ligne = 0;
        }

        public override void Peindre(Graphics g)
        {
            for (int py = 0; py < H && py <= _ligne; py += Cote)
                for (int px = 0; px < L; px += Cote)
                {
                    int cr = (px - L * 7 / 10) * 3 * Un / L;
                    int ci = (py - H / 2) * 2 * Un / H;
                    int zr = 0, zi = 0, i = 0;
                    while (i < Max)
                    {
                        int zr2 = zr * zr / Un, zi2 = zi * zi / Un;
                        if (zr2 + zi2 > 4 * Un) break;
                        zi = 2 * zr * zi / Un + ci;
                        zr = zr2 - zi2 + cr;
                        i++;
                    }
                    Bloc(g, px, py, Cote,
                         i >= Max ? Color.Black : Roue(i * 6 + 160));
                }
        }
    }

    /// <summary>Julia : meme calcul, constante fixe qui derive lentement.</summary>
    internal sealed class Julia : Effet
    {
        public override string Nom { get { return "JULIA"; } }
        private const int Cote = 6, Max = 28, Un = 4096;

        public override void Peindre(Graphics g)
        {
            int cr = S(T) * 700 / 1024 - 300;
            int ci = C(T * 2) * 700 / 1024;
            for (int py = 0; py < H; py += Cote)
                for (int px = 0; px < L; px += Cote)
                {
                    int zr = (px - L / 2) * 3 * Un / L;
                    int zi = (py - H / 2) * 3 * Un / H;
                    int i = 0;
                    while (i < Max)
                    {
                        int zr2 = zr * zr / Un, zi2 = zi * zi / Un;
                        if (zr2 + zi2 > 4 * Un) break;
                        zi = 2 * zr * zi / Un + ci;
                        zr = zr2 - zi2 + cr;
                        i++;
                    }
                    Bloc(g, px, py, Cote, i >= Max ? Color.Black : Roue(i * 8));
                }
        }
    }

    /// <summary>Barres copper : la marque de fabrique de l'Amiga 500.</summary>
    internal sealed class Copper : Effet
    {
        public override string Nom { get { return "COPPER"; } }
        private const int Barres = 6, Epaisseur = 26;

        public override void Peindre(Graphics g)
        {
            for (int b = 0; b < Barres; b++)
            {
                int centre = H / 2 + S(T * 2 + b * 42) * (H / 2 - Epaisseur) / 1024;
                Color pleine = Roue(b * 40 + T);
                for (int d = -Epaisseur; d <= Epaisseur; d++)
                {
                    // Le degre de clarte suit une parabole : c'est ce qui donne
                    // le reflet metallique de la barre.
                    int f = 255 - d * d * 255 / (Epaisseur * Epaisseur);
                    g.FillRectangle(Theme.P(Color.FromArgb(pleine.R * f / 255,
                                                           pleine.G * f / 255,
                                                           pleine.B * f / 255)),
                                    0, centre + d, L, 1);
                }
            }
        }
    }

    /// <summary>Sinus scroller : le texte qui ondule, en bas de toute intro.</summary>
    internal sealed class Scroller : Effet
    {
        public override string Nom { get { return "SCROLLER"; } }
        private const string Texte =
            "   INVENTAIRE DU FRIGO   ***   DATALOGIC SKORPIO   ***   WINDOWS CE 5.0   "
            + "***   MARVELL PXA270 A 520 MHZ   ***   240 x 320   ***   "
            + "COMPACT FRAMEWORK 2.0   ***   GREETINGS AUX LECTEURS DE CODES BARRES   ";
        private int _x;

        protected override void Demarrer() { _x = L; }

        public override void Avancer()
        {
            base.Avancer();
            _x -= 3;
            if (_x < -Texte.Length * 11) _x = L;
        }

        public override void Peindre(Graphics g)
        {
            int y0 = H / 2;
            for (int i = 0; i < Texte.Length; i++)
            {
                int x = _x + i * 11;
                if (x < -12 || x > L) continue;
                int y = y0 + S(T * 4 + i * 16) * 42 / 1024;
                g.DrawString(Texte.Substring(i, 1), Theme.Grande,
                             Theme.P(Roue(i * 6 + T * 2)), x, y);
            }
        }
    }

    /// <summary>Moire : deux trames de cercles qui glissent l'une sur l'autre.</summary>
    internal sealed class Moire : Effet
    {
        public override string Nom { get { return "MOIRE"; } }
        private const int Cote = 4;

        public override void Peindre(Graphics g)
        {
            int ax = L / 2 + S(T) * (L / 3) / 1024, ay = H / 2 + C(T) * (H / 4) / 1024;
            int bx = L / 2 - S(T) * (L / 3) / 1024, by = H / 2 - C(T) * (H / 4) / 1024;
            for (int y = 0; y < H; y += Cote)
                for (int x = 0; x < L; x += Cote)
                {
                    int da = Racine((x - ax) * (x - ax) + (y - ay) * (y - ay));
                    int db = Racine((x - bx) * (x - bx) + (y - by) * (y - by));
                    if ((((da / 7) ^ (db / 7)) & 1) == 0)
                        Bloc(g, x, y, Cote, Color.FromArgb(220, 240, 255));
                }
        }
    }

    /// <summary>Boids : separation, alignement, cohesion. Reynolds, 1986.</summary>
    internal sealed class Boids : Effet
    {
        public override string Nom { get { return "BOIDS"; } }
        private const int N = 24;
        private int[] _x, _y, _vx, _vy;

        protected override void Demarrer()
        {
            _x = new int[N]; _y = new int[N]; _vx = new int[N]; _vy = new int[N];
            for (int i = 0; i < N; i++)
            {
                _x[i] = Hasard.Next(L); _y[i] = Hasard.Next(H);
                _vx[i] = Hasard.Next(-3, 4); _vy[i] = Hasard.Next(-3, 4);
            }
        }

        public override void Avancer()
        {
            base.Avancer();
            for (int i = 0; i < N; i++)
            {
                int cx = 0, cy = 0, ax = 0, ay = 0, sx = 0, sy = 0, voisins = 0;
                for (int j = 0; j < N; j++)
                {
                    if (j == i) continue;
                    int dx = _x[j] - _x[i], dy = _y[j] - _y[i];
                    int d2 = dx * dx + dy * dy;
                    if (d2 > 3600) continue;
                    voisins++;
                    cx += _x[j]; cy += _y[j];
                    ax += _vx[j]; ay += _vy[j];
                    if (d2 < 260) { sx -= dx; sy -= dy; }
                }
                if (voisins > 0)
                {
                    _vx[i] += (cx / voisins - _x[i]) / 40 + ax / voisins / 8 + sx / 10;
                    _vy[i] += (cy / voisins - _y[i]) / 40 + ay / voisins / 8 + sy / 10;
                }
                if (_vx[i] > 5) _vx[i] = 5; if (_vx[i] < -5) _vx[i] = -5;
                if (_vy[i] > 5) _vy[i] = 5; if (_vy[i] < -5) _vy[i] = -5;
                if (_vx[i] == 0 && _vy[i] == 0) _vx[i] = 2;
                _x[i] = (_x[i] + _vx[i] + L) % L;
                _y[i] = (_y[i] + _vy[i] + H) % H;
            }
        }

        public override void Peindre(Graphics g)
        {
            for (int i = 0; i < N; i++)
            {
                Color teinte = Roue(i * 10 + 100);
                Bloc(g, _x[i], _y[i], 4, teinte);
                // Une queue courte dans le sens inverse du deplacement suffit
                // a lire la direction sans dessiner de triangle.
                Bloc(g, _x[i] - _vx[i] * 2, _y[i] - _vy[i] * 2, 2,
                     Color.FromArgb(teinte.R / 2, teinte.G / 2, teinte.B / 2));
            }
        }
    }

    /// <summary>Le jeu de la vie. Conway, 1970.</summary>
    internal sealed class Vie : Effet
    {
        public override string Nom { get { return "JEU DE LA VIE"; } }
        private const int Cote = 6;
        private bool[] _grille, _suivante;
        private int[] _age;
        private int _l, _h;

        protected override void Demarrer()
        {
            _l = L / Cote; _h = H / Cote;
            _grille = new bool[_l * _h]; _suivante = new bool[_l * _h];
            _age = new int[_l * _h];
            for (int i = 0; i < _grille.Length; i++)
                _grille[i] = Hasard.Next(100) < 32;
        }

        public override void Avancer()
        {
            base.Avancer();
            if ((T & 1) != 0) return;               // une generation sur deux
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _l; x++)
                {
                    int n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            if (_grille[((y + dy + _h) % _h) * _l + (x + dx + _l) % _l])
                                n++;
                        }
                    int i = y * _l + x;
                    _suivante[i] = _grille[i] ? (n == 2 || n == 3) : n == 3;
                    _age[i] = _suivante[i] ? (_age[i] < 40 ? _age[i] + 1 : 40) : 0;
                }
            bool[] echange = _grille; _grille = _suivante; _suivante = echange;
        }

        public override void Peindre(Graphics g)
        {
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _l; x++)
                {
                    int i = y * _l + x;
                    if (!_grille[i]) continue;
                    // Les cellules stables virent au bleu, les neuves au blanc :
                    // on lit d'un coup d'oeil ou la colonie travaille encore.
                    Bloc(g, x * Cote, y * Cote, Cote - 1,
                         Fondu(Color.White, Color.FromArgb(30, 120, 220), _age[i], 40));
                }
        }
    }

    /// <summary>Regle 30 : un automate a une dimension, empile ligne a ligne.</summary>
    internal sealed class Regle30 : Effet
    {
        public override string Nom { get { return "REGLE 30"; } }
        private const int Cote = 3;
        private bool[] _ligne;
        private bool[] _histoire;
        private int _l, _h, _y;

        protected override void Demarrer()
        {
            _l = L / Cote; _h = H / Cote;
            _ligne = new bool[_l]; _histoire = new bool[_l * _h]; _y = 0;
            _ligne[_l / 2] = true;
        }

        public override void Avancer()
        {
            base.Avancer();
            for (int k = 0; k < 2; k++)
            {
                for (int x = 0; x < _l; x++) _histoire[_y * _l + x] = _ligne[x];
                bool[] suivante = new bool[_l];
                for (int x = 0; x < _l; x++)
                {
                    bool a = _ligne[(x - 1 + _l) % _l], b = _ligne[x], c = _ligne[(x + 1) % _l];
                    suivante[x] = a ^ (b || c);      // c'est exactement la regle 30
                }
                _ligne = suivante;
                _y++;
                if (_y >= _h) { Demarrer(); return; }
            }
        }

        public override void Peindre(Graphics g)
        {
            for (int y = 0; y < _y; y++)
                for (int x = 0; x < _l; x++)
                    if (_histoire[y * _l + x])
                        Bloc(g, x * Cote, y * Cote, Cote, Gris(120 + y * 120 / _h));
        }
    }

    /// <summary>Sable qui tombe : un automate de grains, tres 1990.</summary>
    internal sealed class Sable : Effet
    {
        public override string Nom { get { return "SABLE"; } }
        private const int Cote = 4;
        private byte[] _cases;
        private int _l, _h;

        protected override void Demarrer()
        {
            _l = L / Cote; _h = H / Cote;
            _cases = new byte[_l * _h];
        }

        public override void Avancer()
        {
            base.Avancer();
            for (int k = 0; k < 3; k++)
                _cases[Hasard.Next(_l / 3) + _l / 3] = (byte)(1 + Hasard.Next(5));
            // De bas en haut, sinon un grain tomberait de toute la hauteur
            // en une seule image.
            for (int y = _h - 2; y >= 0; y--)
                for (int x = 0; x < _l; x++)
                {
                    int i = y * _l + x;
                    if (_cases[i] == 0) continue;
                    int bas = (y + 1) * _l + x;
                    if (_cases[bas] == 0) { _cases[bas] = _cases[i]; _cases[i] = 0; continue; }
                    int cote = Hasard.Next(2) == 0 ? -1 : 1;
                    int nx = x + cote;
                    if (nx >= 0 && nx < _l && _cases[(y + 1) * _l + nx] == 0)
                    {
                        _cases[(y + 1) * _l + nx] = _cases[i]; _cases[i] = 0;
                    }
                }
        }

        public override void Peindre(Graphics g)
        {
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _l; x++)
                {
                    byte v = _cases[y * _l + x];
                    if (v != 0) Bloc(g, x * Cote, y * Cote, Cote, Roue(v * 28 + 20));
                }
        }
    }

    /// <summary>Ondes sur l'eau : la propagation par difference, en entiers.</summary>
    internal sealed class Ondes : Effet
    {
        public override string Nom { get { return "ONDES"; } }
        private const int Cote = 5;
        private int[] _a, _b;
        private int _l, _h;

        protected override void Demarrer()
        {
            _l = L / Cote; _h = H / Cote;
            _a = new int[_l * _h]; _b = new int[_l * _h];
        }

        public override void Avancer()
        {
            base.Avancer();
            if (T % 24 == 0)
                _a[(1 + Hasard.Next(_h - 2)) * _l + 1 + Hasard.Next(_l - 2)] = 900;
            for (int y = 1; y < _h - 1; y++)
                for (int x = 1; x < _l - 1; x++)
                {
                    int i = y * _l + x;
                    int somme = _a[i - 1] + _a[i + 1] + _a[i - _l] + _a[i + _l];
                    int v = somme / 2 - _b[i];
                    _b[i] = v - v / 16;             // amortissement
                }
            int[] echange = _a; _a = _b; _b = echange;
        }

        public override void Peindre(Graphics g)
        {
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _l; x++)
                {
                    int v = _a[y * _l + x];
                    if (v < 0) v = -v;
                    if (v < 6) continue;
                    if (v > 255) v = 255;
                    Bloc(g, x * Cote, y * Cote, Cote,
                         Color.FromArgb(v / 4, v / 2 + 60, 180 + v / 4));
                }
        }
    }

    /// <summary>Lampe a lave : des gouttes qui montent, descendent, fusionnent.</summary>
    internal sealed class Lave : Effet
    {
        public override string Nom { get { return "LAMPE A LAVE"; } }
        private const int Cote = 6, N = 5;
        private int[] _x, _y, _r, _v;

        protected override void Demarrer()
        {
            _x = new int[N]; _y = new int[N]; _r = new int[N]; _v = new int[N];
            for (int i = 0; i < N; i++)
            {
                _x[i] = 20 + Hasard.Next(L - 40);
                _y[i] = Hasard.Next(H);
                _r[i] = 16 + Hasard.Next(18);
                _v[i] = Hasard.Next(2) == 0 ? 1 : -1;
            }
        }

        public override void Avancer()
        {
            base.Avancer();
            for (int i = 0; i < N; i++)
            {
                _y[i] += _v[i];
                if (_y[i] < _r[i] || _y[i] > H - _r[i]) _v[i] = -_v[i];
                _x[i] += S(T + i * 60) / 700;
                if (_x[i] < _r[i]) _x[i] = _r[i];
                if (_x[i] > L - _r[i]) _x[i] = L - _r[i];
            }
        }

        public override void Peindre(Graphics g)
        {
            Fond(g, Color.FromArgb(12, 0, 24));
            for (int y = 0; y < H; y += Cote)
                for (int x = 0; x < L; x += Cote)
                {
                    int champ = 0;
                    for (int i = 0; i < N; i++)
                    {
                        int dx = x - _x[i], dy = y - _y[i];
                        champ += _r[i] * _r[i] * 100 / (dx * dx + dy * dy + 60);
                    }
                    if (champ < 90) continue;
                    int n = champ > 320 ? 255 : 90 + champ / 2;
                    if (n > 255) n = 255;
                    Bloc(g, x, y, Cote, Color.FromArgb(n, n / 3, 40 + n / 4));
                }
        }
    }
}
