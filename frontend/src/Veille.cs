using System;
using System.Drawing;
using System.Windows.Forms;

namespace Inventaire
{
    /// <summary>
    /// Ce que le terminal affiche quand on le pose et qu'on l'oublie.
    ///
    /// Quatre economiseurs d'ecran, tires au hasard et relayes toutes les
    /// trente secondes, puis l'ecran noir. Ils ne servent a rien d'utile, et
    /// c'est assume : un terminal de 2007 pose sur un plan de travail merite
    /// mieux qu'une image figee, et un ecran fixe use les afficheurs.
    ///
    /// Contrainte reelle derriere le decor : un PXA270 a 520 MHz sans
    /// accelerateur. Tout est donc peint en rectangles pleins, la primitive la
    /// moins chere du Compact Framework -- une pluie de pixels en rectangles
    /// coute dix fois moins qu'en caracteres, et ressemble davantage a ce
    /// qu'on veut.
    /// </summary>
    internal sealed class Veille : Control
    {
        public const int Pluie = 0, Etoiles = 1, Logo = 2, Lignes = 3,
                         Tuyaux = 4, Effets = 5;

        /// <summary>Duree d'un economiseur avant de passer au suivant.</summary>
        private const int DureeEffetMs = 30000;

        private readonly Random _hasard = new Random();
        private Bitmap _tampon;
        private Graphics _dessin;

        private int _effet;
        private int _debutEffet;
        private bool _noir;

        // -- pluie de pixels
        private int[] _colonneY;
        private int[] _colonneVitesse;
        private int[] _colonneLongueur;

        // -- champ d'etoiles
        private int[] _etoileX, _etoileY, _etoileZ;

        // -- logo rebondissant
        private int _logoX, _logoY, _logoDX = 2, _logoDY = 2, _logoTeinte;

        // -- lignes qui rebondissent
        private int[] _sommetX, _sommetY, _sommetDX, _sommetDY;
        private int[] _traceX, _traceY;
        private int _traceTete;
        private const int Sommets = 4, Traces = 8;

        // -- tuyaux : un marcheur sur une grille, qui laisse sa trace
        private int[] _tuyauX, _tuyauY, _tuyauTeinte;
        private int _tuyauN, _tuyauTete, _tuyauDX = 1, _tuyauDY;
        private int _tuyauTeinteCourante;
        private const int Pas = 10, TuyauxMax = 260;

        public Veille()
        {
            BackColor = Color.Black;
            Visible = false;
        }

        /// <summary>Entre en veille sur un economiseur tire au hasard.</summary>
        public void Demarrer()
        {
            _noir = false;
            Choisir(_hasard.Next(Effets));
        }

        public void Noircir()
        {
            if (_noir)
                return;
            _noir = true;
            Invalidate();
        }

        public bool Noire { get { return _noir; } }

        private void Choisir(int effet)
        {
            _effet = effet;
            _debutEffet = Environment.TickCount;
            int l = Width > 0 ? Width : 240;
            int h = Height > 0 ? Height : 320;

            if (effet == Pluie)
            {
                int colonnes = l / 8;
                _colonneY = new int[colonnes];
                _colonneVitesse = new int[colonnes];
                _colonneLongueur = new int[colonnes];
                for (int i = 0; i < colonnes; i++)
                {
                    _colonneY[i] = -_hasard.Next(h);
                    _colonneVitesse[i] = 4 + _hasard.Next(10);
                    _colonneLongueur[i] = 5 + _hasard.Next(10);
                }
            }
            else if (effet == Etoiles)
            {
                _etoileX = new int[80];
                _etoileY = new int[80];
                _etoileZ = new int[80];
                for (int i = 0; i < _etoileX.Length; i++)
                    Semer(i, true);
            }
            else if (effet == Logo)
            {
                _logoX = _hasard.Next(Math.Max(1, l - 120));
                _logoY = _hasard.Next(Math.Max(1, h - 40));
                _logoDX = _hasard.Next(2) == 0 ? -2 : 2;
                _logoDY = _hasard.Next(2) == 0 ? -2 : 2;
            }
            else if (effet == Tuyaux)
            {
                _tuyauX = new int[TuyauxMax];
                _tuyauY = new int[TuyauxMax];
                _tuyauTeinte = new int[TuyauxMax];
                _tuyauN = 0;
                _tuyauTete = 0;
                _tuyauX[0] = (l / 2 / Pas) * Pas;
                _tuyauY[0] = (h / 2 / Pas) * Pas;
                _tuyauTeinte[0] = 0;
                _tuyauN = 1;
                _tuyauDX = 1;
                _tuyauDY = 0;
                _tuyauTeinteCourante = _hasard.Next(TeintesLogo.Length);
            }
            else
            {
                _sommetX = new int[Sommets];
                _sommetY = new int[Sommets];
                _sommetDX = new int[Sommets];
                _sommetDY = new int[Sommets];
                _traceX = new int[Sommets * Traces];
                _traceY = new int[Sommets * Traces];
                for (int i = 0; i < Sommets; i++)
                {
                    _sommetX[i] = _hasard.Next(l);
                    _sommetY[i] = _hasard.Next(h);
                    _sommetDX[i] = 2 + _hasard.Next(5);
                    _sommetDY[i] = 2 + _hasard.Next(5);
                    if (_hasard.Next(2) == 0) _sommetDX[i] = -_sommetDX[i];
                    if (_hasard.Next(2) == 0) _sommetDY[i] = -_sommetDY[i];
                }
                for (int i = 0; i < _traceX.Length; i++)
                {
                    _traceX[i] = _sommetX[i % Sommets];
                    _traceY[i] = _sommetY[i % Sommets];
                }
                _traceTete = 0;
            }
            Invalidate();
        }

        /// <summary>
        /// Place une etoile. Les bornes sont calibrees pour qu'au plus loin
        /// -- z = Fond -- toutes tiennent dans l'ecran : c'est ce qui donne
        /// l'impression de foncer dans un champ dense plutot que de regarder
        /// trois points perdus.
        /// </summary>
        private void Semer(int i, bool partout)
        {
            _etoileX[i] = _hasard.Next(-600, 600);
            _etoileY[i] = _hasard.Next(-600, 600);
            _etoileZ[i] = partout ? Avant + _hasard.Next(Fond - Avant) : Fond;
        }

        private const int Fond = 800, Avant = 60, Focale = 150, Vitesse = 14;

        // ------------------------------------------------------------ animation

        /// <summary>Avance d'une image. Appelee par le battement de la fenetre.</summary>
        public void Animer()
        {
            if (_noir)
                return;
            if (Environment.TickCount - _debutEffet > DureeEffetMs)
            {
                Choisir((_effet + 1) % Effets);
                return;
            }
            if (_effet == Pluie) AvancerPluie();
            else if (_effet == Etoiles) AvancerEtoiles();
            else if (_effet == Logo) AvancerLogo();
            else if (_effet == Tuyaux) AvancerTuyaux();
            else AvancerLignes();
            Invalidate();
        }

        private void AvancerPluie()
        {
            for (int i = 0; i < _colonneY.Length; i++)
            {
                _colonneY[i] += _colonneVitesse[i];
                if (_colonneY[i] - _colonneLongueur[i] * 8 > Height)
                {
                    _colonneY[i] = -_hasard.Next(60);
                    _colonneVitesse[i] = 4 + _hasard.Next(10);
                    _colonneLongueur[i] = 5 + _hasard.Next(10);
                }
            }
        }

        private void AvancerEtoiles()
        {
            int cx = Width / 2, cy = Height / 2;
            for (int i = 0; i < _etoileZ.Length; i++)
            {
                _etoileZ[i] -= Vitesse;
                if (_etoileZ[i] > Avant)
                {
                    // Une etoile sortie de l'ecran n'y reviendra pas : sa
                    // projection ne fait que s'ecarter. On la resseme au fond.
                    int x = cx + _etoileX[i] * Focale / _etoileZ[i];
                    int y = cy + _etoileY[i] * Focale / _etoileZ[i];
                    if (x >= 0 && y >= 0 && x < Width && y < Height)
                        continue;
                }
                Semer(i, false);
            }
        }

        private void AvancerLogo()
        {
            _logoX += _logoDX;
            _logoY += _logoDY;
            bool rebond = false;
            if (_logoX <= 0) { _logoX = 0; _logoDX = -_logoDX; rebond = true; }
            if (_logoY <= 0) { _logoY = 0; _logoDY = -_logoDY; rebond = true; }
            if (_logoX > Width - 118) { _logoX = Width - 118; _logoDX = -_logoDX; rebond = true; }
            if (_logoY > Height - 26) { _logoY = Height - 26; _logoDY = -_logoDY; rebond = true; }
            if (rebond)
                _logoTeinte = (_logoTeinte + 1) % 6;
        }

        /// <summary>
        /// Le marcheur avance d'une case et tourne de temps en temps. Quand il
        /// se cogne au bord, il repart dans une autre direction en changeant de
        /// couleur -- ce qui suffit a evoquer les tuyaux d'antan sans avoir a
        /// projeter quoi que ce soit en trois dimensions.
        /// </summary>
        private void AvancerTuyaux()
        {
            int tete = _tuyauTete;
            int x = _tuyauX[tete] + _tuyauDX * Pas;
            int y = _tuyauY[tete] + _tuyauDY * Pas;

            bool coince = x < Pas || y < Pas || x > Width - Pas || y > Height - Pas;
            if (coince || _hasard.Next(7) == 0)
            {
                // Virage a angle droit : on echange les axes.
                int ancien = _tuyauDX;
                _tuyauDX = _tuyauDY;
                _tuyauDY = ancien;
                if (_hasard.Next(2) == 0)
                {
                    _tuyauDX = -_tuyauDX;
                    _tuyauDY = -_tuyauDY;
                }
                if (coince)
                    _tuyauTeinteCourante =
                        (_tuyauTeinteCourante + 1) % TeintesLogo.Length;
                x = _tuyauX[tete] + _tuyauDX * Pas;
                y = _tuyauY[tete] + _tuyauDY * Pas;
                if (x < Pas || y < Pas || x > Width - Pas || y > Height - Pas)
                    return;         // acculé dans un coin : on attend le tour suivant
            }

            _tuyauTete = (_tuyauTete + 1) % TuyauxMax;
            _tuyauX[_tuyauTete] = x;
            _tuyauY[_tuyauTete] = y;
            _tuyauTeinte[_tuyauTete] = _tuyauTeinteCourante;
            if (_tuyauN < TuyauxMax)
                _tuyauN++;
        }

        private void AvancerLignes()
        {
            for (int i = 0; i < Sommets; i++)
            {
                _sommetX[i] += _sommetDX[i];
                _sommetY[i] += _sommetDY[i];
                if (_sommetX[i] < 0 || _sommetX[i] > Width) _sommetDX[i] = -_sommetDX[i];
                if (_sommetY[i] < 0 || _sommetY[i] > Height) _sommetDY[i] = -_sommetDY[i];
            }
            _traceTete = (_traceTete + 1) % Traces;
            for (int i = 0; i < Sommets; i++)
            {
                _traceX[_traceTete * Sommets + i] = _sommetX[i];
                _traceY[_traceTete * Sommets + i] = _sommetY[i];
            }
        }

        // --------------------------------------------------------------- rendu

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Le tampon couvre tout : effacer d'abord ne ferait que clignoter.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 0 || Height <= 0)
                return;
            if (_tampon == null || _tampon.Width != Width || _tampon.Height != Height)
            {
                if (_dessin != null) _dessin.Dispose();
                if (_tampon != null) _tampon.Dispose();
                _tampon = new Bitmap(Width, Height);
                _dessin = Graphics.FromImage(_tampon);
            }
            _dessin.Clear(Color.Black);
            if (!_noir)
            {
                if (_effet == Pluie) PeindrePluie(_dessin);
                else if (_effet == Etoiles) PeindreEtoiles(_dessin);
                else if (_effet == Logo) PeindreLogo(_dessin);
                else if (_effet == Tuyaux) PeindreTuyaux(_dessin);
                else PeindreLignes(_dessin);
            }
            e.Graphics.DrawImage(_tampon, 0, 0);
        }

        private void PeindrePluie(Graphics g)
        {
            for (int i = 0; i < _colonneY.Length; i++)
            {
                int x = i * 8 + 1;
                for (int cran = 0; cran < _colonneLongueur[i]; cran++)
                {
                    int y = _colonneY[i] - cran * 8;
                    if (y < -8 || y > Height)
                        continue;
                    // La tete est blanche, la traine s'eteint vers le vert
                    // sombre : c'est ce degrade qui donne l'impression de chute.
                    int reste = 255 - cran * 255 / _colonneLongueur[i];
                    Color teinte = cran == 0 ? Color.White
                        : Color.FromArgb(reste / 5, 90 + reste / 2, reste / 4);
                    g.FillRectangle(Theme.P(teinte), x, y, 5, 6);
                }
            }
        }

        private void PeindreEtoiles(Graphics g)
        {
            int cx = Width / 2, cy = Height / 2;
            for (int i = 0; i < _etoileZ.Length; i++)
            {
                int z = _etoileZ[i];
                if (z <= Avant)
                    continue;
                int x = cx + _etoileX[i] * Focale / z;
                int y = cy + _etoileY[i] * Focale / z;
                if (x < 0 || y < 0 || x >= Width || y >= Height)
                    continue;
                // Plus une etoile est proche, plus elle est grosse et claire.
                int taille = z < 180 ? 3 : z < 420 ? 2 : 1;
                int clarte = 255 - (z - Avant) * 150 / (Fond - Avant);
                if (clarte < 90) clarte = 90;
                g.FillRectangle(Theme.P(Color.FromArgb(clarte, clarte, clarte)),
                                x, y, taille, taille);
            }
        }

        private static readonly Color[] TeintesLogo = {
            Color.FromArgb(0, 255, 128), Color.FromArgb(255, 220, 0),
            Color.FromArgb(255, 90, 60), Color.FromArgb(90, 170, 255),
            Color.FromArgb(255, 120, 220), Color.FromArgb(255, 255, 255),
        };

        private void PeindreLogo(Graphics g)
        {
            Color teinte = TeintesLogo[_logoTeinte];
            g.DrawString("INVENTAIRE", Theme.Grande, Theme.P(teinte), _logoX, _logoY);
            g.DrawRectangle(Theme.C(teinte), _logoX - 6, _logoY - 5, 124, 26);
        }

        private void PeindreTuyaux(Graphics g)
        {
            for (int i = 0; i < _tuyauN; i++)
            {
                int k = (_tuyauTete - i + TuyauxMax) % TuyauxMax;
                Color teinte = TeintesLogo[_tuyauTeinte[k]];
                // Le tuyau garde sa couleur sur les trois premiers quarts et
                // ne s'eteint que sur sa queue : tout degrader d'un bout a
                // l'autre rendrait invisible la plus grande partie du trace.
                int seuil = _tuyauN * 7 / 10;
                if (i > seuil && _tuyauN > seuil)
                {
                    int reste = 255 - (i - seuil) * 200 / (_tuyauN - seuil);
                    teinte = Color.FromArgb(teinte.R * reste / 255,
                                            teinte.G * reste / 255,
                                            teinte.B * reste / 255);
                }
                g.FillRectangle(Theme.P(teinte), _tuyauX[k] - 4, _tuyauY[k] - 4, 9, 9);
            }
        }

        private void PeindreLignes(Graphics g)
        {
            for (int t = 0; t < Traces; t++)
            {
                int age = (Traces + _traceTete - t) % Traces;
                int clarte = 255 - age * 26;
                Color teinte = Color.FromArgb(clarte / 3, clarte / 2, clarte);
                int bloc = t * Sommets;
                for (int i = 0; i < Sommets; i++)
                {
                    int j = (i + 1) % Sommets;
                    g.DrawLine(Theme.C(teinte), _traceX[bloc + i], _traceY[bloc + i],
                               _traceX[bloc + j], _traceY[bloc + j]);
                }
            }
        }

        protected override void Dispose(bool liberer)
        {
            if (liberer)
            {
                if (_dessin != null) { _dessin.Dispose(); _dessin = null; }
                if (_tampon != null) { _tampon.Dispose(); _tampon = null; }
            }
            base.Dispose(liberer);
        }
    }
}
