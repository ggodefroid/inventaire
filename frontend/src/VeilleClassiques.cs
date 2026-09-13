using System;
using System.Drawing;

namespace Inventaire
{
    // ====================================================================
    //  Les cinq premiers economiseurs, ceux d'avant la table d'effets. Meme
    //  code, meme rendu ; seule la forme a change pour rejoindre les autres.
    // ====================================================================

    internal static class Teintes
    {
        public static readonly Color[] Six = {
            Color.FromArgb(0, 255, 128), Color.FromArgb(255, 220, 0),
            Color.FromArgb(255, 90, 60), Color.FromArgb(90, 170, 255),
            Color.FromArgb(255, 120, 220), Color.FromArgb(255, 255, 255),
        };
    }

    /// <summary>Pluie de caracteres. Matrix, 1999, et mille demos avant.</summary>
    internal sealed class Pluie : Effet
    {
        public override string Nom { get { return "PLUIE"; } }
        private int[] _y, _vitesse, _longueur;

        protected override void Demarrer()
        {
            int colonnes = L / 8;
            _y = new int[colonnes]; _vitesse = new int[colonnes];
            _longueur = new int[colonnes];
            for (int i = 0; i < colonnes; i++)
            {
                _y[i] = -Hasard.Next(H);
                _vitesse[i] = 4 + Hasard.Next(10);
                _longueur[i] = 5 + Hasard.Next(10);
            }
        }

        public override void Avancer()
        {
            base.Avancer();
            for (int i = 0; i < _y.Length; i++)
            {
                _y[i] += _vitesse[i];
                if (_y[i] - _longueur[i] * 8 <= H) continue;
                _y[i] = -Hasard.Next(60);
                _vitesse[i] = 4 + Hasard.Next(10);
                _longueur[i] = 5 + Hasard.Next(10);
            }
        }

        public override void Peindre(Graphics g)
        {
            for (int i = 0; i < _y.Length; i++)
            {
                int x = i * 8 + 1;
                for (int cran = 0; cran < _longueur[i]; cran++)
                {
                    int y = _y[i] - cran * 8;
                    if (y < -8 || y > H) continue;
                    // La tete est blanche, la traine s'eteint vers le vert
                    // sombre : c'est ce degrade qui donne la chute.
                    int reste = 255 - cran * 255 / _longueur[i];
                    Color teinte = cran == 0 ? Color.White
                        : Color.FromArgb(reste / 5, 90 + reste / 2, reste / 4);
                    g.FillRectangle(Theme.P(teinte), x, y, 5, 6);
                }
            }
        }
    }

    /// <summary>Champ d'etoiles en vol, l'economiseur de Windows 3.1.</summary>
    internal sealed class Etoiles : Effet
    {
        public override string Nom { get { return "ETOILES"; } }
        private const int Fond_ = 800, Avant = 60, Focale = 150, Vitesse = 14;
        private int[] _x, _y, _z;

        protected override void Demarrer()
        {
            _x = new int[80]; _y = new int[80]; _z = new int[80];
            for (int i = 0; i < _x.Length; i++) Semer(i, true);
        }

        /// <summary>
        /// Les bornes sont calibrees pour qu'au plus loin toutes les etoiles
        /// tiennent dans l'ecran : c'est ce qui donne l'impression de foncer
        /// dans un champ dense plutot que de regarder trois points perdus.
        /// </summary>
        private void Semer(int i, bool partout)
        {
            _x[i] = Hasard.Next(-600, 600);
            _y[i] = Hasard.Next(-600, 600);
            _z[i] = partout ? Avant + Hasard.Next(Fond_ - Avant) : Fond_;
        }

        public override void Avancer()
        {
            base.Avancer();
            int cx = L / 2, cy = H / 2;
            for (int i = 0; i < _z.Length; i++)
            {
                _z[i] -= Vitesse;
                if (_z[i] > Avant)
                {
                    int x = cx + _x[i] * Focale / _z[i];
                    int y = cy + _y[i] * Focale / _z[i];
                    if (x >= 0 && y >= 0 && x < L && y < H) continue;
                }
                Semer(i, false);
            }
        }

        public override void Peindre(Graphics g)
        {
            int cx = L / 2, cy = H / 2;
            for (int i = 0; i < _z.Length; i++)
            {
                int z = _z[i];
                if (z <= Avant) continue;
                int x = cx + _x[i] * Focale / z, y = cy + _y[i] * Focale / z;
                if (x < 0 || y < 0 || x >= L || y >= H) continue;
                int taille = z < 180 ? 3 : z < 420 ? 2 : 1;
                int clarte = 255 - (z - Avant) * 150 / (Fond_ - Avant);
                if (clarte < 90) clarte = 90;
                Bloc(g, x, y, taille, Gris(clarte));
            }
        }
    }

    /// <summary>Le logo qui rebondit, et qu'on guette au coin de l'ecran.</summary>
    internal sealed class Logo : Effet
    {
        public override string Nom { get { return "LOGO"; } }
        private int _x, _y, _dx, _dy, _teinte;

        protected override void Demarrer()
        {
            _x = Hasard.Next(Math.Max(1, L - 120));
            _y = Hasard.Next(Math.Max(1, H - 40));
            _dx = Hasard.Next(2) == 0 ? -2 : 2;
            _dy = Hasard.Next(2) == 0 ? -2 : 2;
        }

        public override void Avancer()
        {
            base.Avancer();
            _x += _dx; _y += _dy;
            bool rebond = false;
            if (_x <= 0) { _x = 0; _dx = -_dx; rebond = true; }
            if (_y <= 0) { _y = 0; _dy = -_dy; rebond = true; }
            if (_x > L - 118) { _x = L - 118; _dx = -_dx; rebond = true; }
            if (_y > H - 26) { _y = H - 26; _dy = -_dy; rebond = true; }
            if (rebond) _teinte = (_teinte + 1) % Teintes.Six.Length;
        }

        public override void Peindre(Graphics g)
        {
            Color teinte = Teintes.Six[_teinte];
            g.DrawString("INVENTAIRE", Theme.Grande, Theme.P(teinte), _x, _y);
            g.DrawRectangle(Theme.C(teinte), _x - 6, _y - 5, 124, 26);
        }
    }

    /// <summary>Mystify : des polygones qui rebondissent, en laissant leur trace.</summary>
    internal sealed class Lignes : Effet
    {
        public override string Nom { get { return "MYSTIFY"; } }
        private const int Sommets = 4, Traces = 8;
        private int[] _sx, _sy, _dx, _dy, _tx, _ty;
        private int _tete;

        protected override void Demarrer()
        {
            _sx = new int[Sommets]; _sy = new int[Sommets];
            _dx = new int[Sommets]; _dy = new int[Sommets];
            _tx = new int[Sommets * Traces]; _ty = new int[Sommets * Traces];
            for (int i = 0; i < Sommets; i++)
            {
                _sx[i] = Hasard.Next(L); _sy[i] = Hasard.Next(H);
                _dx[i] = 2 + Hasard.Next(5); _dy[i] = 2 + Hasard.Next(5);
                if (Hasard.Next(2) == 0) _dx[i] = -_dx[i];
                if (Hasard.Next(2) == 0) _dy[i] = -_dy[i];
            }
            for (int i = 0; i < _tx.Length; i++)
            {
                _tx[i] = _sx[i % Sommets]; _ty[i] = _sy[i % Sommets];
            }
            _tete = 0;
        }

        public override void Avancer()
        {
            base.Avancer();
            for (int i = 0; i < Sommets; i++)
            {
                _sx[i] += _dx[i]; _sy[i] += _dy[i];
                if (_sx[i] < 0 || _sx[i] > L) _dx[i] = -_dx[i];
                if (_sy[i] < 0 || _sy[i] > H) _dy[i] = -_dy[i];
            }
            _tete = (_tete + 1) % Traces;
            for (int i = 0; i < Sommets; i++)
            {
                _tx[_tete * Sommets + i] = _sx[i];
                _ty[_tete * Sommets + i] = _sy[i];
            }
        }

        public override void Peindre(Graphics g)
        {
            for (int t = 0; t < Traces; t++)
            {
                int age = (Traces + _tete - t) % Traces;
                int clarte = 255 - age * 26;
                Color teinte = Color.FromArgb(clarte / 3, clarte / 2, clarte);
                int bloc = t * Sommets;
                for (int i = 0; i < Sommets; i++)
                {
                    int j = (i + 1) % Sommets;
                    g.DrawLine(Theme.C(teinte), _tx[bloc + i], _ty[bloc + i],
                               _tx[bloc + j], _ty[bloc + j]);
                }
            }
        }
    }

    /// <summary>
    /// Les tuyaux de Windows 95. Un marcheur sur une grille qui laisse sa
    /// trace : cela suffit a les evoquer sans projeter quoi que ce soit en
    /// trois dimensions.
    /// </summary>
    internal sealed class Tuyaux : Effet
    {
        public override string Nom { get { return "TUYAUX   1995"; } }
        private const int Pas = 10, Max = 260;
        private int[] _x, _y, _teinte;
        private int _n, _tete, _dx, _dy, _teinteCourante;

        protected override void Demarrer()
        {
            _x = new int[Max]; _y = new int[Max]; _teinte = new int[Max];
            _x[0] = (L / 2 / Pas) * Pas; _y[0] = (H / 2 / Pas) * Pas;
            _n = 1; _tete = 0; _dx = 1; _dy = 0;
            _teinteCourante = Hasard.Next(Teintes.Six.Length);
        }

        public override void Avancer()
        {
            base.Avancer();
            int x = _x[_tete] + _dx * Pas, y = _y[_tete] + _dy * Pas;
            bool coince = x < Pas || y < Pas || x > L - Pas || y > H - Pas;
            if (coince || Hasard.Next(7) == 0)
            {
                int ancien = _dx; _dx = _dy; _dy = ancien;   // virage a angle droit
                if (Hasard.Next(2) == 0) { _dx = -_dx; _dy = -_dy; }
                if (coince)
                    _teinteCourante = (_teinteCourante + 1) % Teintes.Six.Length;
                x = _x[_tete] + _dx * Pas; y = _y[_tete] + _dy * Pas;
                if (x < Pas || y < Pas || x > L - Pas || y > H - Pas)
                    return;          // accule dans un coin : on attend le tour suivant
            }
            _tete = (_tete + 1) % Max;
            _x[_tete] = x; _y[_tete] = y; _teinte[_tete] = _teinteCourante;
            if (_n < Max) _n++;
        }

        public override void Peindre(Graphics g)
        {
            for (int i = 0; i < _n; i++)
            {
                int k = (_tete - i + Max) % Max;
                Color teinte = Teintes.Six[_teinte[k]];
                // Le tuyau garde sa couleur sur les trois premiers quarts et ne
                // s'eteint que sur sa queue : tout degrader d'un bout a l'autre
                // rendrait invisible la plus grande partie du trace.
                int seuil = _n * 7 / 10;
                if (i > seuil && _n > seuil)
                {
                    int reste = 255 - (i - seuil) * 200 / (_n - seuil);
                    teinte = Color.FromArgb(teinte.R * reste / 255,
                                            teinte.G * reste / 255,
                                            teinte.B * reste / 255);
                }
                Bloc(g, _x[k] - 4, _y[k] - 4, 9, teinte);
            }
        }
    }
}
