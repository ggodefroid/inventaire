using System;
using System.Drawing;

namespace Inventaire
{
    // ====================================================================
    //  Dix bornes en mode attraction. Aucune n'est jouable : elles se jouent
    //  toutes seules, comme une borne d'arcade qui attend un client.
    // ====================================================================

    /// <summary>Pong, 1972. Les deux raquettes suivent la balle, mal.</summary>
    internal sealed class Pong : Effet
    {
        public override string Nom { get { return "PONG   1972"; } }
        private int _bx, _by, _dx, _dy, _g, _d, _pg, _pd;

        protected override void Demarrer()
        {
            _bx = L / 2; _by = H / 2; _dx = 3; _dy = 2;
            _g = H / 2; _d = H / 2; _pg = 0; _pd = 0;
        }

        public override void Avancer()
        {
            base.Avancer();
            _bx += _dx; _by += _dy;
            if (_by < 4 || _by > H - 8) _dy = -_dy;
            // Les raquettes ratent volontairement de temps en temps : une
            // partie parfaite ne finit jamais, et n'a aucun interet a regarder.
            int vise = _by - 18 + S(T * 3) / 90;
            _g += _g < vise ? 4 : -4;
            _d += _d < vise ? 4 : -4;
            if (_bx < 14 && _dx < 0)
            {
                if (_by > _g && _by < _g + 36) _dx = -_dx; else { _pd++; Relancer(); }
            }
            if (_bx > L - 18 && _dx > 0)
            {
                if (_by > _d && _by < _d + 36) _dx = -_dx; else { _pg++; Relancer(); }
            }
        }

        private void Relancer()
        {
            _bx = L / 2; _by = H / 2;
            _dx = Hasard.Next(2) == 0 ? 3 : -3; _dy = Hasard.Next(2) == 0 ? 2 : -2;
        }

        public override void Peindre(Graphics g)
        {
            for (int y = 0; y < H; y += 14)
                g.FillRectangle(Theme.P(Color.FromArgb(60, 60, 60)), L / 2 - 1, y, 2, 8);
            g.FillRectangle(Theme.P(Color.White), 8, _g, 5, 36);
            g.FillRectangle(Theme.P(Color.White), L - 13, _d, 5, 36);
            g.FillRectangle(Theme.P(Color.White), _bx, _by, 5, 5);
            g.DrawString(_pg.ToString(), Theme.Enorme, Theme.P(Color.FromArgb(90, 90, 90)),
                         L / 2 - 44, 8);
            g.DrawString(_pd.ToString(), Theme.Enorme, Theme.P(Color.FromArgb(90, 90, 90)),
                         L / 2 + 26, 8);
        }
    }

    /// <summary>Casse-briques, 1976. La raquette ne rate jamais.</summary>
    internal sealed class CasseBriques : Effet
    {
        public override string Nom { get { return "CASSE-BRIQUES   1976"; } }
        private const int Colonnes = 8, Rangees = 6;
        private bool[] _briques;
        private int _bx, _by, _dx, _dy, _raquette, _lb, _hb;

        protected override void Demarrer()
        {
            _briques = new bool[Colonnes * Rangees];
            for (int i = 0; i < _briques.Length; i++) _briques[i] = true;
            _lb = L / Colonnes; _hb = 12;
            _bx = L / 2; _by = H / 2; _dx = 3; _dy = 3; _raquette = L / 2;
        }

        public override void Avancer()
        {
            base.Avancer();
            _bx += _dx; _by += _dy;
            if (_bx < 2 || _bx > L - 6) _dx = -_dx;
            if (_by < 24) _dy = -_dy;
            _raquette += _bx - 20 > _raquette ? 4 : -4;
            if (_by > H - 30 && _dy > 0
                && _bx > _raquette - 4 && _bx < _raquette + 44) _dy = -_dy;
            if (_by > H) { _by = H / 2; _dy = -3; }
            int cx = _bx / _lb, cy = (_by - 24) / _hb;
            if (cy >= 0 && cy < Rangees && cx >= 0 && cx < Colonnes
                && _briques[cy * Colonnes + cx])
            {
                _briques[cy * Colonnes + cx] = false;
                _dy = -_dy;
                bool reste = false;
                for (int i = 0; i < _briques.Length; i++) reste |= _briques[i];
                if (!reste) Demarrer();
            }
        }

        public override void Peindre(Graphics g)
        {
            for (int y = 0; y < Rangees; y++)
                for (int x = 0; x < Colonnes; x++)
                    if (_briques[y * Colonnes + x])
                        g.FillRectangle(Theme.P(Roue(y * 34 + 10)),
                                        x * _lb + 1, 24 + y * _hb + 1, _lb - 2, _hb - 2);
            g.FillRectangle(Theme.P(Color.White), _raquette, H - 24, 40, 5);
            g.FillRectangle(Theme.P(Color.White), _bx, _by, 5, 5);
        }
    }

    /// <summary>Snake. Le serpent vise la pomme sans jamais se mordre.</summary>
    internal sealed class Serpent : Effet
    {
        public override string Nom { get { return "SNAKE"; } }
        private const int Cote = 8;
        private int[] _x, _y;
        private int _n, _tete, _l, _h, _px, _py, _dx = 1, _dy;

        protected override void Demarrer()
        {
            _l = L / Cote; _h = H / Cote;
            _x = new int[_l * _h]; _y = new int[_l * _h];
            _n = 5; _tete = 0; _dx = 1; _dy = 0;
            for (int i = 0; i < _n; i++) { _x[i] = _l / 2 - i; _y[i] = _h / 2; }
            Pomme();
        }

        private void Pomme() { _px = Hasard.Next(_l); _py = Hasard.Next(_h); }

        private bool Occupe(int x, int y)
        {
            for (int i = 0; i < _n; i++)
            {
                int k = (_tete - i + _x.Length) % _x.Length;
                if (_x[k] == x && _y[k] == y) return true;
            }
            return false;
        }

        public override void Avancer()
        {
            base.Avancer();
            if ((T & 1) != 0) return;
            int tx = _x[_tete], ty = _y[_tete];
            // Choix glouton, mais on refuse un virage qui mene dans le corps.
            int[] cx = { 1, -1, 0, 0 }, cy = { 0, 0, 1, -1 };
            int meilleur = -1, meilleureDistance = int.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                if (cx[i] == -_dx && cy[i] == -_dy) continue;
                int nx = (tx + cx[i] + _l) % _l, ny = (ty + cy[i] + _h) % _h;
                if (Occupe(nx, ny)) continue;
                int d = Math.Abs(nx - _px) + Math.Abs(ny - _py);
                if (d < meilleureDistance) { meilleureDistance = d; meilleur = i; }
            }
            if (meilleur >= 0) { _dx = cx[meilleur]; _dy = cy[meilleur]; }
            _tete = (_tete + 1) % _x.Length;
            _x[_tete] = (tx + _dx + _l) % _l;
            _y[_tete] = (ty + _dy + _h) % _h;
            if (_x[_tete] == _px && _y[_tete] == _py)
            {
                if (_n < 60) _n++;
                Pomme();
            }
        }

        public override void Peindre(Graphics g)
        {
            g.FillRectangle(Theme.P(Color.FromArgb(230, 60, 70)),
                            _px * Cote + 1, _py * Cote + 1, Cote - 2, Cote - 2);
            for (int i = 0; i < _n; i++)
            {
                int k = (_tete - i + _x.Length) % _x.Length;
                Bloc(g, _x[k] * Cote, _y[k] * Cote, Cote - 1,
                     i == 0 ? Color.White : Fondu(Color.FromArgb(90, 240, 120),
                                                  Color.FromArgb(20, 90, 40), i, _n));
            }
        }
    }

    /// <summary>Tetris, 1984. Les pieces tombent, les lignes s'effacent.</summary>
    internal sealed class Tetris : Effet
    {
        public override string Nom { get { return "TETRIS   1984"; } }
        private const int Cote = 12, Colonnes = 10;
        private int _rangees;
        private byte[] _puits;
        private int _px, _py, _forme, _rot;

        private static readonly int[][] Formes = {
            new int[] { 0x0F00, 0x2222, 0x00F0, 0x4444 },   // I
            new int[] { 0x0660, 0x0660, 0x0660, 0x0660 },   // O
            new int[] { 0x4E00, 0x4640, 0x0E40, 0x4C40 },   // T
            new int[] { 0x6C00, 0x4620, 0x06C0, 0x8C40 },   // S
            new int[] { 0xC600, 0x2640, 0x0C60, 0x4C80 },   // Z
            new int[] { 0x8E00, 0x6440, 0x0E20, 0x44C0 },   // J
            new int[] { 0x2E00, 0x4460, 0x0E80, 0xC440 },   // L
        };

        protected override void Demarrer()
        {
            _rangees = H / Cote;
            _puits = new byte[Colonnes * _rangees];
            Nouvelle();
        }

        private void Nouvelle()
        {
            _forme = Hasard.Next(Formes.Length);
            _rot = Hasard.Next(4);
            _px = Hasard.Next(Colonnes - 3); _py = 0;
        }

        private bool Heurte(int px, int py, int rot)
        {
            int bits = Formes[_forme][rot & 3];
            for (int i = 0; i < 16; i++)
            {
                if ((bits & (0x8000 >> i)) == 0) continue;
                int x = px + i % 4, y = py + i / 4;
                if (x < 0 || x >= Colonnes || y >= _rangees) return true;
                if (y >= 0 && _puits[y * Colonnes + x] != 0) return true;
            }
            return false;
        }

        public override void Avancer()
        {
            base.Avancer();
            if (T % 4 != 0) return;
            if (!Heurte(_px, _py + 1, _rot)) { _py++; return; }

            int bits = Formes[_forme][_rot & 3];
            for (int i = 0; i < 16; i++)
            {
                if ((bits & (0x8000 >> i)) == 0) continue;
                int x = _px + i % 4, y = _py + i / 4;
                if (y >= 0 && y < _rangees && x >= 0 && x < Colonnes)
                    _puits[y * Colonnes + x] = (byte)(_forme + 1);
            }
            // Lignes pleines : on les efface et on fait descendre le reste.
            for (int y = _rangees - 1; y >= 0; y--)
            {
                bool pleine = true;
                for (int x = 0; x < Colonnes; x++) pleine &= _puits[y * Colonnes + x] != 0;
                if (!pleine) continue;
                for (int k = y; k > 0; k--)
                    for (int x = 0; x < Colonnes; x++)
                        _puits[k * Colonnes + x] = _puits[(k - 1) * Colonnes + x];
                for (int x = 0; x < Colonnes; x++) _puits[x] = 0;
                y++;
            }
            if (_py <= 0) Demarrer(); else Nouvelle();
        }

        public override void Peindre(Graphics g)
        {
            int x0 = (L - Colonnes * Cote) / 2;
            g.FillRectangle(Theme.P(Color.FromArgb(10, 10, 18)),
                            x0, 0, Colonnes * Cote, H);
            for (int y = 0; y < _rangees; y++)
                for (int x = 0; x < Colonnes; x++)
                {
                    byte v = _puits[y * Colonnes + x];
                    if (v != 0)
                        Bloc(g, x0 + x * Cote, y * Cote, Cote - 1, Roue(v * 36));
                }
            int bits = Formes[_forme][_rot & 3];
            for (int i = 0; i < 16; i++)
            {
                if ((bits & (0x8000 >> i)) == 0) continue;
                int x = _px + i % 4, y = _py + i / 4;
                if (y >= 0)
                    Bloc(g, x0 + x * Cote, y * Cote, Cote - 1, Roue((_forme + 1) * 36));
            }
        }
    }

    /// <summary>Space Invaders, 1978. La descente inexorable.</summary>
    internal sealed class Envahisseurs : Effet
    {
        public override string Nom { get { return "SPACE INVADERS   1978"; } }
        private const int Cols = 8, Rangs = 4, Pas = 22;
        private bool[] _vivants;
        private int _ox, _oy, _sens = 1, _canon, _tirX, _tirY = -1;

        protected override void Demarrer()
        {
            _vivants = new bool[Cols * Rangs];
            for (int i = 0; i < _vivants.Length; i++) _vivants[i] = true;
            _ox = 10; _oy = 26; _sens = 1; _canon = L / 2; _tirY = -1;
        }

        public override void Avancer()
        {
            base.Avancer();
            if (T % 6 == 0)
            {
                _ox += _sens * 5;
                if (_ox < 4 || _ox + Cols * Pas > L - 4) { _sens = -_sens; _oy += 8; }
                if (_oy > H - 60) Demarrer();
            }
            // Le canon vise la colonne vivante la plus proche.
            int cible = -1;
            for (int x = 0; x < Cols && cible < 0; x++)
                for (int y = Rangs - 1; y >= 0; y--)
                    if (_vivants[y * Cols + x]) { cible = _ox + x * Pas; break; }
            if (cible >= 0) _canon += cible > _canon ? 3 : -3;
            if (_tirY < 0 && T % 14 == 0) { _tirX = _canon + 4; _tirY = H - 34; }
            if (_tirY >= 0)
            {
                _tirY -= 8;
                if (_tirY < 0) _tirY = -1;
                else
                {
                    int cx = (_tirX - _ox) / Pas, cy = (_tirY - _oy) / 16;
                    if (cx >= 0 && cx < Cols && cy >= 0 && cy < Rangs
                        && _vivants[cy * Cols + cx])
                    {
                        _vivants[cy * Cols + cx] = false; _tirY = -1;
                        bool reste = false;
                        for (int i = 0; i < _vivants.Length; i++) reste |= _vivants[i];
                        if (!reste) Demarrer();
                    }
                }
            }
        }

        public override void Peindre(Graphics g)
        {
            int dandine = (T / 6) % 2;
            for (int y = 0; y < Rangs; y++)
                for (int x = 0; x < Cols; x++)
                {
                    if (!_vivants[y * Cols + x]) continue;
                    int px = _ox + x * Pas, py = _oy + y * 16;
                    Color teinte = y == 0 ? Color.FromArgb(255, 240, 120)
                        : Color.FromArgb(120, 255, 140);
                    g.FillRectangle(Theme.P(teinte), px + 2, py + 2, 10, 6);
                    g.FillRectangle(Theme.P(teinte), px, py + 4, 14, 3);
                    g.FillRectangle(Theme.P(teinte), px + dandine, py + 8, 4, 3);
                    g.FillRectangle(Theme.P(teinte), px + 10 - dandine, py + 8, 4, 3);
                }
            g.FillRectangle(Theme.P(Color.FromArgb(120, 255, 140)), _canon, H - 24, 10, 6);
            g.FillRectangle(Theme.P(Color.FromArgb(120, 255, 140)), _canon + 4, H - 30, 2, 6);
            if (_tirY >= 0)
                g.FillRectangle(Theme.P(Color.White), _tirX, _tirY, 2, 7);
        }
    }

    /// <summary>Le glouton de 1980, ses fantomes et son labyrinthe.</summary>
    internal sealed class Glouton : Effet
    {
        public override string Nom { get { return "GLOUTON   1980"; } }
        private const int Cote = 12, N = 4;
        private bool[] _mur, _pastille;
        private int _l, _h, _x, _y, _dx = 1, _dy;
        private int[] _fx, _fy, _fdx, _fdy;

        protected override void Demarrer()
        {
            _l = L / Cote; _h = H / Cote;
            _mur = new bool[_l * _h]; _pastille = new bool[_l * _h];
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _l; x++)
                {
                    // Un labyrinthe regulier : piliers un rang sur deux, comme
                    // les niveaux de l'epoque.
                    bool mur = y == 0 || y == _h - 1 || x == 0 || x == _l - 1
                               || (x % 2 == 0 && y % 2 == 0);
                    _mur[y * _l + x] = mur;
                    _pastille[y * _l + x] = !mur;
                }
            _x = 1; _y = 1; _dx = 1; _dy = 0;
            _fx = new int[N]; _fy = new int[N]; _fdx = new int[N]; _fdy = new int[N];
            for (int i = 0; i < N; i++)
            {
                _fx[i] = _l - 2 - i; _fy[i] = _h - 2; _fdx[i] = -1;
            }
        }

        private bool Libre(int x, int y)
        {
            return x >= 0 && y >= 0 && x < _l && y < _h && !_mur[y * _l + x];
        }

        private void Errer(int i)
        {
            int[] cx = { 1, -1, 0, 0 }, cy = { 0, 0, 1, -1 };
            int choix = Hasard.Next(4);
            for (int k = 0; k < 4; k++)
            {
                int j = (choix + k) % 4;
                if (!Libre(_fx[i] + cx[j], _fy[i] + cy[j])) continue;
                _fdx[i] = cx[j]; _fdy[i] = cy[j]; return;
            }
        }

        public override void Avancer()
        {
            base.Avancer();
            if (T % 4 != 0) return;
            if (!Libre(_x + _dx, _y + _dy) || Hasard.Next(8) == 0)
            {
                int[] cx = { 1, -1, 0, 0 }, cy = { 0, 0, 1, -1 };
                int choix = Hasard.Next(4);
                for (int k = 0; k < 4; k++)
                {
                    int j = (choix + k) % 4;
                    if (!Libre(_x + cx[j], _y + cy[j])) continue;
                    _dx = cx[j]; _dy = cy[j]; break;
                }
            }
            if (Libre(_x + _dx, _y + _dy)) { _x += _dx; _y += _dy; }
            _pastille[_y * _l + _x] = false;
            for (int i = 0; i < N; i++)
            {
                if (!Libre(_fx[i] + _fdx[i], _fy[i] + _fdy[i]) || Hasard.Next(6) == 0)
                    Errer(i);
                if (Libre(_fx[i] + _fdx[i], _fy[i] + _fdy[i]))
                { _fx[i] += _fdx[i]; _fy[i] += _fdy[i]; }
                if (_fx[i] == _x && _fy[i] == _y) Demarrer();
            }
            bool reste = false;
            for (int i = 0; i < _pastille.Length; i++) reste |= _pastille[i];
            if (!reste) Demarrer();
        }

        public override void Peindre(Graphics g)
        {
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _l; x++)
                {
                    int i = y * _l + x;
                    if (_mur[i])
                        g.FillRectangle(Theme.P(Color.FromArgb(30, 60, 180)),
                                        x * Cote + 2, y * Cote + 2, Cote - 4, Cote - 4);
                    else if (_pastille[i])
                        g.FillRectangle(Theme.P(Color.FromArgb(240, 220, 160)),
                                        x * Cote + Cote / 2 - 1, y * Cote + Cote / 2 - 1, 3, 3);
                }
            g.FillRectangle(Theme.P(Color.FromArgb(255, 230, 0)),
                            _x * Cote + 1, _y * Cote + 1, Cote - 2, Cote - 2);
            Color[] robes = { Color.FromArgb(255, 60, 60), Color.FromArgb(255, 160, 230),
                              Color.FromArgb(0, 230, 230), Color.FromArgb(255, 170, 60) };
            for (int i = 0; i < N; i++)
                g.FillRectangle(Theme.P(robes[i]),
                                _fx[i] * Cote + 1, _fy[i] * Cote + 1, Cote - 2, Cote - 3);
        }
    }

    /// <summary>Asteroides, 1979. Le vaisseau derive, les rochers se fendent.</summary>
    internal sealed class Asteroides : Effet
    {
        public override string Nom { get { return "ASTEROIDES   1979"; } }
        private const int Max = 14;
        private int[] _rx, _ry, _rdx, _rdy, _rt;   // _rt : taille, 0 = mort
        private int _sx, _sy, _sa;

        protected override void Demarrer()
        {
            _rx = new int[Max]; _ry = new int[Max]; _rdx = new int[Max];
            _rdy = new int[Max]; _rt = new int[Max];
            for (int i = 0; i < 5; i++) Semer(i, 3);
            _sx = L / 2; _sy = H / 2; _sa = 0;
        }

        private void Semer(int i, int taille)
        {
            _rx[i] = Hasard.Next(L); _ry[i] = Hasard.Next(H);
            _rdx[i] = Hasard.Next(-2, 3); _rdy[i] = Hasard.Next(-2, 3);
            if (_rdx[i] == 0 && _rdy[i] == 0) _rdx[i] = 1;
            _rt[i] = taille;
        }

        public override void Avancer()
        {
            base.Avancer();
            _sa = (_sa + 2) & 255;
            _sx = (_sx + C(_sa) / 400 + L) % L;
            _sy = (_sy + S(_sa) / 400 + H) % H;
            int vivants = 0;
            for (int i = 0; i < Max; i++)
            {
                if (_rt[i] == 0) continue;
                vivants++;
                _rx[i] = (_rx[i] + _rdx[i] + L) % L;
                _ry[i] = (_ry[i] + _rdy[i] + H) % H;
                // Le tir est implicite : un rocher touche se fend tout seul.
                int dx = _rx[i] - _sx, dy = _ry[i] - _sy;
                if (dx * dx + dy * dy < 500 && T % 20 == 0)
                {
                    int taille = _rt[i];
                    _rt[i] = 0;
                    if (taille > 1)
                        for (int k = 0; k < 2; k++)
                            for (int j = 0; j < Max; j++)
                                if (_rt[j] == 0)
                                {
                                    Semer(j, taille - 1);
                                    _rx[j] = _rx[i]; _ry[j] = _ry[i];
                                    break;
                                }
                }
            }
            if (vivants == 0) Demarrer();
        }

        public override void Peindre(Graphics g)
        {
            for (int i = 0; i < Max; i++)
            {
                if (_rt[i] == 0) continue;
                int r = _rt[i] * 6;
                // Un rocher en huit segments : le fil de fer d'origine.
                for (int a = 0; a < 256; a += 32)
                {
                    int r1 = r + ((a * 7 + i * 13) % 5) - 2;
                    int r2 = r + (((a + 32) * 7 + i * 13) % 5) - 2;
                    g.DrawLine(Theme.C(Color.FromArgb(200, 210, 220)),
                               _rx[i] + C(a) * r1 / 1024, _ry[i] + S(a) * r1 / 1024,
                               _rx[i] + C(a + 32) * r2 / 1024, _ry[i] + S(a + 32) * r2 / 1024);
                }
            }
            int[] ax = { 0, 110, 146 };
            int px = _sx + C(_sa) * 9 / 1024, py = _sy + S(_sa) * 9 / 1024;
            for (int k = 0; k < 3; k++)
            {
                int a1 = _sa + ax[k], a2 = _sa + ax[(k + 1) % 3];
                g.DrawLine(Theme.C(Color.White),
                           _sx + C(a1) * 9 / 1024, _sy + S(a1) * 9 / 1024,
                           _sx + C(a2) * 9 / 1024, _sy + S(a2) * 9 / 1024);
            }
            g.FillRectangle(Theme.P(Color.White), px, py, 2, 2);
        }
    }

    /// <summary>Simon, 1978 : quatre couleurs, une sequence qui s'allonge.</summary>
    internal sealed class Simon : Effet
    {
        public override string Nom { get { return "SIMON   1978"; } }
        private int[] _suite;
        private int _n, _pas;

        protected override void Demarrer()
        {
            _suite = new int[32]; _n = 1; _pas = 0;
            _suite[0] = Hasard.Next(4);
        }

        public override void Avancer()
        {
            base.Avancer();
            if (T % 14 != 0) return;
            _pas++;
            if (_pas > _n + 1)
            {
                _pas = 0;
                if (_n < _suite.Length) { _suite[_n] = Hasard.Next(4); _n++; }
                else Demarrer();
            }
        }

        public override void Peindre(Graphics g)
        {
            Color[] eteintes = { Color.FromArgb(60, 0, 0), Color.FromArgb(60, 50, 0),
                                 Color.FromArgb(0, 50, 0), Color.FromArgb(0, 20, 60) };
            Color[] vives = { Color.FromArgb(255, 60, 60), Color.FromArgb(255, 230, 60),
                              Color.FromArgb(60, 240, 90), Color.FromArgb(70, 140, 255) };
            int actif = _pas < _n ? _suite[_pas] : -1;
            int cote = Math.Min(L, H) / 2 - 14;
            int x0 = (L - cote * 2) / 2, y0 = (H - cote * 2) / 2;
            for (int i = 0; i < 4; i++)
            {
                int x = x0 + (i % 2) * (cote + 6);
                int y = y0 + (i / 2) * (cote + 6);
                g.FillRectangle(Theme.P(i == actif ? vives[i] : eteintes[i]),
                                x, y, cote, cote);
            }
            Centre(g, "SEQUENCE " + _n, Theme.Petite, Color.FromArgb(150, 150, 150),
                   y0 + cote * 2 + 14);
        }
    }

    /// <summary>Demineur : la grille se devoile toute seule, et saute parfois.</summary>
    internal sealed class Demineur : Effet
    {
        public override string Nom { get { return "DEMINEUR"; } }
        private const int Cote = 14;
        private bool[] _mine, _ouvert;
        private int _l, _h;
        private bool _perdu;

        protected override void Demarrer()
        {
            _l = L / Cote; _h = (H - 20) / Cote;
            _mine = new bool[_l * _h]; _ouvert = new bool[_l * _h];
            for (int i = 0; i < _mine.Length; i++) _mine[i] = Hasard.Next(100) < 14;
            _perdu = false;
        }

        private int Voisines(int x, int y)
        {
            int n = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= _l || ny >= _h) continue;
                    if (_mine[ny * _l + nx]) n++;
                }
            return n;
        }

        public override void Avancer()
        {
            base.Avancer();
            if (_perdu) { if (T % 40 == 0) Demarrer(); return; }
            if (T % 3 != 0) return;
            for (int essai = 0; essai < 6; essai++)
            {
                int i = Hasard.Next(_mine.Length);
                if (_ouvert[i]) continue;
                _ouvert[i] = true;
                if (_mine[i]) { _perdu = true; for (int k = 0; k < _mine.Length; k++) if (_mine[k]) _ouvert[k] = true; }
                return;
            }
        }

        public override void Peindre(Graphics g)
        {
            Color[] chiffres = { Color.Gray, Color.FromArgb(60, 110, 255),
                                 Color.FromArgb(40, 180, 60), Color.FromArgb(230, 60, 60),
                                 Color.FromArgb(40, 40, 160), Color.FromArgb(160, 40, 40),
                                 Color.FromArgb(40, 160, 160), Color.Black, Color.Gray };
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _l; x++)
                {
                    int i = y * _l + x, px = x * Cote, py = 20 + y * Cote;
                    if (!_ouvert[i])
                    {
                        g.FillRectangle(Theme.P(Color.FromArgb(190, 190, 190)),
                                        px, py, Cote - 1, Cote - 1);
                        g.FillRectangle(Theme.P(Color.FromArgb(240, 240, 240)), px, py, Cote - 1, 2);
                        continue;
                    }
                    g.FillRectangle(Theme.P(Color.FromArgb(130, 130, 130)),
                                    px, py, Cote - 1, Cote - 1);
                    if (_mine[i])
                        g.FillRectangle(Theme.P(Color.Black), px + 4, py + 4, 6, 6);
                    else
                    {
                        int n = Voisines(x, y);
                        if (n > 0)
                            g.DrawString(n.ToString(), Theme.PetiteGras,
                                         Theme.P(chiffres[n]), px + 3, py + 1);
                    }
                }
            g.DrawString(_perdu ? "PERDU" : "DEMINAGE", Theme.PetiteGras,
                         Theme.P(_perdu ? Color.FromArgb(255, 90, 90) : Color.White), 4, 3);
        }
    }

    /// <summary>Le labyrinthe en 3D de Windows 95, en couloirs de faux relief.</summary>
    internal sealed class Labyrinthe : Effet
    {
        public override string Nom { get { return "LABYRINTHE 3D   1995"; } }
        private int _profondeur, _virage;

        protected override void Demarrer() { _profondeur = 0; _virage = 0; }

        public override void Avancer()
        {
            base.Avancer();
            _profondeur += 4;
            if (_profondeur > 100)
            {
                _profondeur = 0;
                _virage = Hasard.Next(3) - 1;      // -1 gauche, 0 tout droit, 1 droite
            }
        }

        public override void Peindre(Graphics g)
        {
            int cx = L / 2 + _virage * _profondeur / 3, cy = H / 2;
            // Des parois pleines et non des cadres : un couloir se lit a ses
            // murs, pas a ses aretes. On peint du fond vers l'avant, chaque
            // tranche recouvrant la precedente.
            for (int i = 8; i >= 0; i--)
            {
                int d = i * 34 + (100 - _profondeur) * 34 / 100;
                int demiL = L * d / 300, demiH = H * d / 300;
                if (demiL < 4) continue;
                int clarte = 235 - d * 2;
                if (clarte < 20) clarte = 20;
                Color mur = Color.FromArgb(clarte / 4, clarte / 3, clarte / 2);
                Color sol = Color.FromArgb(clarte / 3, clarte / 3, clarte / 3);
                int x0 = cx - demiL, x1 = cx + demiL;
                int y0 = cy - demiH, y1 = cy + demiH;
                g.FillRectangle(Theme.P(mur), x0, y0, demiL * 2, demiH * 2);
                g.FillRectangle(Theme.P(sol), x0, y1 - demiH / 6, demiL * 2, demiH / 6);
                g.DrawRectangle(Theme.C(Color.FromArgb(clarte / 2, clarte, clarte)),
                                x0, y0, demiL * 2, demiH * 2);
                // Les briques d'une paroi sur deux : c'est ce qui donne la
                // vitesse quand elles defilent.
                if (i % 2 == 0)
                {
                    g.DrawLine(Theme.C(Color.FromArgb(clarte / 3, clarte / 2, clarte / 2)),
                               x0, y0 + demiH, x0 + demiL / 4, y0 + demiH);
                    g.DrawLine(Theme.C(Color.FromArgb(clarte / 3, clarte / 2, clarte / 2)),
                               x1 - demiL / 4, y0 + demiH, x1, y0 + demiH);
                }
            }
            g.DrawString(_virage < 0 ? "<" : _virage > 0 ? ">" : "^", Theme.Grande,
                         Theme.P(Color.FromArgb(90, 150, 170)), L - 22, H - 28);
        }
    }
}
