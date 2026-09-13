using System;
using System.Drawing;
using System.Windows.Forms;

namespace Inventaire
{
    /// <summary>
    /// L'accueil : on vise, on tire, c'est tout.
    ///
    /// Le lecteur du terminal fonctionne en emulation clavier (« wedge ») : la
    /// gachette tape le code-barres comme le ferait un clavier, puis un retour
    /// chariot. Aucun SDK Datalogic n'est donc necessaire, et le programme
    /// marche sur toute la gamme. Le code en cours est peint en gros plutot
    /// que place dans un TextBox natif : a bout de bras devant une etagere,
    /// c'est la difference entre lisible et illisible.
    ///
    /// L'assemblage du code revient a <see cref="Lecture"/>, y compris la
    /// conclusion : ni la longueur ni le suffixe du lecteur ne sont supposes.
    /// </summary>
    internal sealed class EcranScan : Ecran
    {
        public EcranScan(Fenetre fenetre) : base(fenetre) { }

        // Ici tout chiffre construit le code-barres : pas de raccourci a un
        // caractere, sinon taper un code a la main serait impossible.
        protected override bool RaccourcisActifs { get { return false; } }

        public override bool EstRacine { get { return true; } }

        public override void Entrer(object argument)
        {
            EffacerFrappe();
        }

        public override void Retour()
        {
            // Deja a la racine : la fleche retour n'y est pas affichee.
        }

        public override bool Touche(Keys touche)
        {
            if (ToucheBase(touche))
                return true;
            if (touche == Keys.Delete)
            {
                EffacerFrappe();
                return true;
            }
            // ToucheBase a deja traite Echap si une saisie etait en cours. Ici
            // le champ est vide et l'on est a la racine : plus rien a annuler,
            // autant s'en servir pour poser l'appareil.
            if (touche == Keys.Escape)
            {
                Fenetre.Endormir();
                return true;
            }
            return false;
        }

        protected override void CodeLu(string code)
        {
            Envoyer(code);
        }

        protected override void Activer(string id)
        {
            if (id == "envoyer") Envoyer(Frappe);
            else if (id == "effacer") EffacerFrappe();
            else if (id == "stock") Fenetre.Aller(Fenetre.Liste, null);
            else if (id == "reglages") Fenetre.Aller(Fenetre.Configuration, null);
        }

        private void Envoyer(string brut)
        {
            string code = brut == null ? "" : brut.Trim();
            EffacerFrappe();
            if (code.Length < 4)
            {
                Fenetre.Dire("code trop court", false);
                return;
            }
            Fenetre.Appeler("lecture", new FonctionReseau(delegate()
            {
                return Api.Scan(code);
            }), new SuiteReseau(delegate(Reponse reponse)
            {
                if (!reponse.Joint || !reponse.Ok)
                {
                    Fenetre.Dire(reponse.Erreur, false);
                    return;
                }
                Fenetre.Aller(Fenetre.Article, Article.Depuis(reponse.Donnees));
            }));
        }

        protected override void Peindre(Graphics g, Rectangle zone)
        {
            PeindreBandeau(g, "INVENTAIRE", false);

            int marge = 8;
            int largeur = Width - 2 * marge;
            int y = HauteurBandeau + 20;
            string code = Frappe;

            Theme.TexteCentre(g, "Visez un article", Theme.Grande, Theme.Texte, zone, y);
            y += 20;
            Theme.TexteCentre(g, "et appuyez sur la gachette", Theme.Petite, Theme.Doux,
                              zone, y);
            y += 26;

            // Champ de saisie peint : le code en cours, ou une invite.
            Rectangle champ = new Rectangle(marge, y, largeur, 40);
            Theme.Remplir(g, champ, Theme.FondDoux);
            Theme.Cadre(g, champ, code.Length > 0 ? Theme.Barre : Theme.Trait);
            if (code.Length > 0)
            {
                Theme.TexteCentre(g, Theme.Tronquer(g, code, Theme.Enorme, largeur - 12),
                                  Theme.Enorme, Theme.Texte, champ, y + 8);
            }
            else
            {
                Theme.TexteCentre(g, "- - - - - - -", Theme.Grande, Theme.Trait,
                                  champ, y + 10);
            }
            y += 46;

            if (code.Length > 0)
            {
                Rectangle valider = new Rectangle(marge, y, largeur * 2 / 3 - 3, 32);
                BoutonZone(g, valider, "envoyer", "VALIDER", Theme.Vert,
                           Color.White, Theme.NormaleGras);
                BoutonZone(g, new Rectangle(valider.Right + 6, y,
                                            largeur - valider.Width - 6, 32),
                           "effacer", "Effacer", Theme.FondDoux, Theme.Texte,
                           Theme.Petite);
            }
            else
            {
                Theme.TexteCentre(g, "ou tapez le code au pave numerique",
                                  Theme.Minuscule, Theme.Doux, zone, y + 6);
                Theme.TexteCentre(g, "Echap : mise en veille", Theme.Minuscule,
                                  Theme.Trait, zone, y + 20);
            }

            // Deux destinations, ancrees en bas : leur position ne bouge
            // jamais, on finit par appuyer sans regarder.
            int hauteur = 42;
            int yMenu = BasUtile - 4 - hauteur;
            int demi = (largeur - 6) / 2;
            BoutonZone(g, new Rectangle(marge, yMenu, demi, hauteur), "stock",
                       "F1\nTout le frigo", Theme.FondDoux, Theme.Texte, Theme.Petite);
            BoutonZone(g, new Rectangle(marge + demi + 6, yMenu, demi, hauteur),
                       "reglages", "F3\nReglages", Theme.FondDoux, Theme.Texte,
                       Theme.Petite);
        }
    }
}
