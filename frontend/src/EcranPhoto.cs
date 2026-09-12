using System;
using System.Drawing;
using System.Windows.Forms;

namespace Inventaire
{
    /// <summary>
    /// Une photo en grand. Rien d'autre.
    ///
    /// Sur 240 pixels de large, la vignette de la fiche sert a reconnaitre un
    /// produit, pas a lire son emballage. Cet ecran demande au serveur la meme
    /// image a la taille de l'ecran -- ce qui pese dix fois plus lourd sur le
    /// fil, d'ou le fait qu'on ne le fasse que sur demande explicite.
    /// </summary>
    internal sealed class EcranPhoto : Ecran
    {
        /// <summary>Cote de l'image demandee au serveur, en pixels.</summary>
        public const int Taille = 200;

        private DemandePhoto _demande;

        public EcranPhoto(Fenetre fenetre) : base(fenetre) { }

        protected override bool RaccourcisActifs { get { return false; } }

        public override void Entrer(object argument)
        {
            DemandePhoto demande = argument as DemandePhoto;
            if (demande != null)
                _demande = demande;
            if (_demande != null)
                Fenetre.Photos.Demander(_demande.Code, Taille);
        }

        public override void Retour()
        {
            if (_demande != null && _demande.Retour != null)
                Fenetre.Aller(_demande.Retour, _demande.Argument);
            else
                Fenetre.Aller(Fenetre.Scan, null);
        }

        public override bool Touche(Keys touche)
        {
            if (ToucheBase(touche))
                return true;
            if (touche == Keys.Escape || touche == Keys.Left || touche == Keys.Right)
            {
                Retour();
                return true;
            }
            return false;
        }

        protected override bool Valider()
        {
            Retour();
            return true;
        }

        protected override void Activer(string id)
        {
            if (id == "fermer")
                Retour();
        }

        protected override void CodeLu(string code)
        {
            Fenetre.Appeler("lecture", new FonctionReseau(delegate()
            {
                return Api.Scan(code);
            }), new SuiteReseau(delegate(Reponse reponse)
            {
                if (reponse.Joint && reponse.Ok)
                    Fenetre.Aller(Fenetre.Article, Article.Depuis(reponse.Donnees));
                else
                    Fenetre.Dire(reponse.Erreur, false);
            }));
        }

        protected override void Peindre(Graphics g, Rectangle zone)
        {
            string nom = _demande == null ? "" : _demande.Nom;
            PeindreBandeau(g, nom.Length > 0 ? nom : "Photo", true);

            int haut = HauteurBandeau;
            int bas = BasUtile - 22;
            Rectangle cadre = new Rectangle(0, haut, Width, bas - haut);
            // Toute la surface renvoie a l'ecran d'ou l'on vient : sur un ecran
            // qui ne montre qu'une image, il n'y a rien d'autre a viser.
            Zones.Add(new Zone(cadre, "fermer"));

            Bitmap image = _demande == null ? null
                : Fenetre.Photos.Obtenir(_demande.Code, Taille);
            if (image != null)
            {
                g.DrawImage(image, cadre.X + (cadre.Width - image.Width) / 2,
                            cadre.Y + (cadre.Height - image.Height) / 2);
            }
            else if (_demande != null && Fenetre.Photos.Absente(_demande.Code, Taille))
            {
                Theme.TexteCentre(g, "Pas de photo pour ce produit", Theme.Normale,
                                  Theme.Doux, zone, cadre.Y + cadre.Height / 2 - 8);
            }
            else
            {
                Theme.TexteCentre(g, "Chargement de la photo...", Theme.Normale,
                                  Theme.Doux, zone, cadre.Y + cadre.Height / 2 - 8);
            }

            Theme.TexteCentre(g, "Entree ou Echap pour revenir", Theme.Minuscule,
                              Theme.Doux, zone, bas + 8);
        }
    }
}
