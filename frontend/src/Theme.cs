using System;
using System.Collections.Generic;
using System.Drawing;

namespace Inventaire
{
    /// <summary>
    /// Couleurs, polices et primitives de dessin.
    ///
    /// Tout l'affichage est peint a la main plutot que compose de controles.
    /// Sous Compact Framework un controle coute un handle de fenetre et une
    /// passe de mise en page ; une trentaine sur un ecran de 240 pixels rend
    /// la navigation poussive sur un PXA270. Un seul controle par ecran, un
    /// seul OnPaint, et le rendu est immediat.
    ///
    /// Contraintes du CF 2.0 respectees ici : ni Brushes ni Pens (les caches
    /// statiques du .NET de bureau n'existent pas, on garde les notres), et
    /// aucun PointF, absent lui aussi.
    /// </summary>
    internal static class Theme
    {
        // Fond clair assume : l'ecran du terminal est transflectif, on s'en
        // sert en cuisine sous un plafonnier comme au soleil. Le contraste
        // maximal prime sur l'elegance.
        public static readonly Color Fond = Color.FromArgb(255, 255, 255);
        public static readonly Color FondDoux = Color.FromArgb(241, 243, 240);
        public static readonly Color Barre = Color.FromArgb(26, 127, 75);
        public static readonly Color SurBarre = Color.FromArgb(255, 255, 255);
        public static readonly Color Texte = Color.FromArgb(17, 17, 17);
        public static readonly Color Doux = Color.FromArgb(107, 114, 112);
        public static readonly Color Trait = Color.FromArgb(208, 213, 208);
        public static readonly Color Vert = Color.FromArgb(26, 127, 75);
        public static readonly Color Orange = Color.FromArgb(217, 119, 6);
        public static readonly Color Rouge = Color.FromArgb(192, 57, 43);
        public static readonly Color Jaune = Color.FromArgb(245, 196, 0);
        public static readonly Color Bleu = Color.FromArgb(29, 78, 216);
        public static readonly Color Gris = Color.FromArgb(150, 155, 150);

        // Un cran au-dessus de ce que permettrait l'ecran : sur un terminal
        // qu'on lit a bout de bras dans une cuisine, gagner deux lignes ne
        // vaut pas de plisser les yeux.
        public static readonly Font Minuscule = new Font("Tahoma", 7.5f, FontStyle.Regular);
        public static readonly Font Petite = new Font("Tahoma", 8.5f, FontStyle.Regular);
        public static readonly Font PetiteGras = new Font("Tahoma", 8.5f, FontStyle.Bold);
        public static readonly Font Normale = new Font("Tahoma", 9.5f, FontStyle.Regular);
        public static readonly Font NormaleGras = new Font("Tahoma", 9.5f, FontStyle.Bold);
        public static readonly Font Grande = new Font("Tahoma", 12f, FontStyle.Bold);
        public static readonly Font Enorme = new Font("Tahoma", 17f, FontStyle.Bold);

        private static readonly Dictionary<int, SolidBrush> _pinceaux =
            new Dictionary<int, SolidBrush>();
        private static readonly Dictionary<int, Pen> _crayons = new Dictionary<int, Pen>();

        /// <summary>Pinceau partage. Ne jamais le liberer : il resservira.</summary>
        public static SolidBrush P(Color couleur)
        {
            int cle = couleur.ToArgb();
            SolidBrush pinceau;
            if (!_pinceaux.TryGetValue(cle, out pinceau))
            {
                pinceau = new SolidBrush(couleur);
                _pinceaux[cle] = pinceau;
            }
            return pinceau;
        }

        public static Pen C(Color couleur)
        {
            int cle = couleur.ToArgb();
            Pen crayon;
            if (!_crayons.TryGetValue(cle, out crayon))
            {
                crayon = new Pen(couleur);
                _crayons[cle] = crayon;
            }
            return crayon;
        }

        // ------------------------------------------------------- indicateurs

        /// <summary>Couleur officielle du Nutri-Score, lettre 'a'..'e'.</summary>
        public static Color CouleurNutri(string note)
        {
            if (note == null || note.Length == 0)
                return Gris;
            switch (char.ToLower(note[0]))
            {
                case 'a': return Color.FromArgb(3, 129, 65);
                case 'b': return Color.FromArgb(133, 187, 47);
                case 'c': return Color.FromArgb(254, 203, 2);
                case 'd': return Color.FromArgb(238, 129, 0);
                case 'e': return Color.FromArgb(230, 62, 17);
            }
            return Gris;
        }

        /// <summary>Groupe NOVA : 1 brut, 4 ultra-transforme.</summary>
        public static Color CouleurNova(int groupe)
        {
            switch (groupe)
            {
                case 1: return Color.FromArgb(26, 127, 75);
                case 2: return Color.FromArgb(133, 187, 47);
                case 3: return Color.FromArgb(238, 129, 0);
                case 4: return Color.FromArgb(192, 57, 43);
            }
            return Gris;
        }

        /// <summary>Temoin nutritionnel Open Food Facts : low / moderate / high.</summary>
        public static Color CouleurNiveau(string niveau)
        {
            if (niveau == "low") return Vert;
            if (niveau == "moderate") return Color.FromArgb(232, 163, 61);
            if (niveau == "high") return Rouge;
            return Trait;
        }

        public static int RangNiveau(string niveau)
        {
            if (niveau == "low") return 1;
            if (niveau == "moderate") return 2;
            if (niveau == "high") return 3;
            return 0;
        }

        /// <summary>
        /// Couleur d'une echeance. C'est l'information la plus utile de tout
        /// le programme : elle doit se lire d'un coup d'oeil, sans lire la date.
        /// </summary>
        public static Color CouleurJours(int jours, bool sansDate)
        {
            if (sansDate) return Gris;
            if (jours < 0) return Rouge;
            if (jours <= 2) return Rouge;
            if (jours <= 6) return Orange;
            if (jours <= 20) return Jaune;
            return Vert;
        }

        /// <summary>Echeance en toutes lettres, calibree pour tenir sur l'ecran.</summary>
        public static string TexteJours(int jours, bool sansDate)
        {
            if (sansDate) return "sans date";
            if (jours < -1) return "perime " + (-jours) + " j";
            if (jours == -1) return "perime hier";
            if (jours == 0) return "AUJOURD'HUI";
            if (jours == 1) return "demain";
            if (jours < 100) return "dans " + jours + " j";
            if (jours < 365) return "dans " + (jours / 30) + " mois";
            return "dans " + (jours / 365) + " an(s)";
        }

        // ---------------------------------------------------------- primitives

        public static void Remplir(Graphics g, Rectangle r, Color couleur)
        {
            g.FillRectangle(P(couleur), r.X, r.Y, r.Width, r.Height);
        }

        public static void Cadre(Graphics g, Rectangle r, Color couleur)
        {
            g.DrawRectangle(C(couleur), r.X, r.Y, r.Width - 1, r.Height - 1);
        }

        public static void Ligne(Graphics g, int x1, int y, int x2, Color couleur)
        {
            g.DrawLine(C(couleur), x1, y, x2, y);
        }

        public static void Texte_(Graphics g, string texte, Font police, Color couleur,
                                  int x, int y)
        {
            if (texte == null || texte.Length == 0)
                return;
            g.DrawString(texte, police, P(couleur), x, y);
        }

        public static int Largeur(Graphics g, string texte, Font police)
        {
            if (texte == null || texte.Length == 0)
                return 0;
            return (int)Math.Ceiling(g.MeasureString(texte, police).Width);
        }

        public static int Hauteur(Graphics g, Font police)
        {
            return (int)Math.Ceiling(g.MeasureString("Hg", police).Height);
        }

        /// <summary>Texte centre horizontalement dans une bande.</summary>
        public static void TexteCentre(Graphics g, string texte, Font police, Color couleur,
                                       Rectangle bande, int y)
        {
            if (texte == null || texte.Length == 0)
                return;
            int l = Largeur(g, texte, police);
            g.DrawString(texte, police, P(couleur), bande.X + (bande.Width - l) / 2, y);
        }

        /// <summary>
        /// Coupe un texte a la largeur disponible, suffixe par un point de
        /// suspension. Sans cela un nom de produit Open Food Facts -- ils font
        /// couramment soixante caracteres -- deborde sur l'ecran voisin.
        /// </summary>
        public static string Tronquer(Graphics g, string texte, Font police, int largeurMax)
        {
            if (texte == null || texte.Length == 0)
                return "";
            if (Largeur(g, texte, police) <= largeurMax)
                return texte;
            int bas = 0, haut = texte.Length;
            while (bas < haut)
            {
                int milieu = (bas + haut + 1) / 2;
                if (Largeur(g, texte.Substring(0, milieu) + "...", police) <= largeurMax)
                    bas = milieu;
                else
                    haut = milieu - 1;
            }
            return bas <= 0 ? "" : texte.Substring(0, bas) + "...";
        }

        /// <summary>Decoupe un texte en lignes tenant dans la largeur donnee.</summary>
        public static string[] Replier(Graphics g, string texte, Font police,
                                       int largeurMax, int lignesMax)
        {
            List<string> lignes = new List<string>();
            if (texte == null || texte.Length == 0)
                return new string[0];
            string[] mots = texte.Split(' ');
            string courante = "";
            for (int i = 0; i < mots.Length; i++)
            {
                if (mots[i].Length == 0)
                    continue;
                string essai = courante.Length == 0 ? mots[i] : courante + " " + mots[i];
                if (Largeur(g, essai, police) <= largeurMax)
                {
                    courante = essai;
                    continue;
                }
                if (courante.Length > 0)
                    lignes.Add(courante);
                courante = mots[i];
                if (lignes.Count == lignesMax - 1)
                    break;
            }
            if (courante.Length > 0 && lignes.Count < lignesMax)
                lignes.Add(courante);
            // La derniere ligne absorbe le reste, tronque proprement.
            if (lignes.Count == lignesMax)
                lignes[lignesMax - 1] = Tronquer(g, lignes[lignesMax - 1], police, largeurMax);
            return lignes.ToArray();
        }

        /// <summary>Pastille lettree : Nutri-Score, groupe NOVA.</summary>
        public static void Pastille(Graphics g, Rectangle r, Color fond, string lettre,
                                    Font police)
        {
            Remplir(g, r, fond);
            Cadre(g, r, Assombrir(fond));
            int l = Largeur(g, lettre, police);
            int h = Hauteur(g, police);
            g.DrawString(lettre, police, P(Color.White),
                         r.X + (r.Width - l) / 2, r.Y + (r.Height - h) / 2);
        }

        public static Color Assombrir(Color couleur)
        {
            return Color.FromArgb(couleur.R * 2 / 3, couleur.G * 2 / 3, couleur.B * 2 / 3);
        }

        /// <summary>
        /// Triangle plein, pour les fleches de defilement.
        ///
        /// Dessine plutot qu'ecrit : U+25B2 n'est pas dans Latin-1, et la
        /// Tahoma d'une image Windows CE assemblee par un industriel n'est pas
        /// garantie complete. Un caractere manquant s'affiche en carre vide.
        /// </summary>
        public static void Triangle(Graphics g, Rectangle r, bool versLeHaut, Color couleur)
        {
            int largeur = Math.Min(r.Width, 14);
            int hauteur = largeur * 2 / 3;
            int x = r.X + (r.Width - largeur) / 2;
            int y = r.Y + (r.Height - hauteur) / 2;
            Point[] sommets = versLeHaut
                ? new Point[] { new Point(x + largeur / 2, y),
                                new Point(x + largeur, y + hauteur),
                                new Point(x, y + hauteur) }
                : new Point[] { new Point(x, y),
                                new Point(x + largeur, y),
                                new Point(x + largeur / 2, y + hauteur) };
            g.FillPolygon(P(couleur), sommets);
        }

        /// <summary>
        /// Anneau de focus clavier : un trait sombre double d'un trait clair.
        ///
        /// Le double trait n'est pas une coquetterie. Le focus doit se voir
        /// aussi bien sur un bouton gris clair que sur un aplat rouge ; une
        /// seule couleur disparaitrait sur l'un ou sur l'autre.
        /// </summary>
        public static void AnneauFocus(Graphics g, Rectangle r)
        {
            Cadre(g, r, Texte);
            Cadre(g, new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2),
                  Color.White);
            Cadre(g, new Rectangle(r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4), Texte);
        }

        /// <summary>Bouton peint : le rendu natif du CF est trop discret au doigt.</summary>
        public static void Bouton(Graphics g, Rectangle r, string libelle, Color fond,
                                  Color encre, Font police, bool enfonce)
        {
            Color corps = enfonce ? Assombrir(fond) : fond;
            Remplir(g, r, corps);
            Cadre(g, r, Assombrir(corps));
            if (libelle == null || libelle.Length == 0)
                return;
            int decalage = enfonce ? 1 : 0;
            // Un saut de ligne dans le libelle empile deux lignes centrees :
            // « F1 » au-dessus de « Stock » tient dans un bouton plus etroit
            // que « F1 Stock » sur une seule ligne.
            string[] lignes = libelle.Split('\n');
            int h = Hauteur(g, police);
            int depart = r.Y + (r.Height - h * lignes.Length) / 2 + decalage;
            for (int i = 0; i < lignes.Length; i++)
            {
                int l = Largeur(g, lignes[i], police);
                g.DrawString(lignes[i], police, P(encre),
                             r.X + (r.Width - l) / 2 + decalage, depart + i * h);
            }
        }
    }
}
