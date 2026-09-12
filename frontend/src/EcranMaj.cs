using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Inventaire
{
    /// <summary>
    /// Mise a jour du programme depuis le serveur.
    ///
    /// Le terminal n'a pas de gestionnaire de paquets, et recopier un binaire
    /// a la main demande de sortir la carte memoire ou d'ouvrir le navigateur.
    /// Puisqu'il parle deja au serveur a chaque bip, autant lui laisser
    /// recuperer sa propre version.
    ///
    /// Un executable en cours d'execution ne peut pas etre ecrase : Windows
    /// garde son fichier ouvert. On le renomme donc -- ce que Windows CE
    /// accepte -- pour mettre le nouveau a sa place, et l'on quitte. La
    /// manoeuvre est faite dans un ordre qui laisse toujours un binaire
    /// valide sur le disque, meme si elle echoue en cours de route.
    /// </summary>
    internal sealed class EcranMaj : Ecran
    {
        private const int Verification = 0, AJour = 1, Disponible = 2,
                          Telechargement = 3, Installee = 4, Echec = 5;

        private const string SuffixeNouveau = ".maj";
        private const string SuffixeAncien = ".old";

        private int _etat = Verification;
        private string _version = "";
        private int _taille;
        private string _nom = "Inventaire.exe";
        private string _detail = "";
        private bool _automatique;

        public EcranMaj(Fenetre fenetre) : base(fenetre) { }

        protected override bool RaccourcisActifs { get { return false; } }

        /// <summary>
        /// argument : la reponse deja obtenue par la fenetre au demarrage, ou
        /// null pour une verification demandee depuis les reglages.
        /// </summary>
        public override void Entrer(object argument)
        {
            PoserCurseur("installer");
            Reponse annonce = argument as Reponse;
            if (annonce != null)
            {
                _automatique = true;
                Retenir(annonce);
                return;
            }
            _automatique = false;
            if (_etat != Installee)
                Verifier();
        }

        private void Retenir(Reponse reponse)
        {
            _version = reponse.Donnees.S("version");
            _taille = reponse.Donnees.I("taille", 0);
            _nom = reponse.Donnees.S("nom", "Inventaire.exe");
            _etat = Disponible_(reponse) ? Disponible : AJour;
            Invalidate();
        }

        /// <summary>Le serveur a un binaire, et ce n'est pas celui qui tourne.</summary>
        private static bool Disponible_(Reponse reponse)
        {
            return reponse.Donnees.B("disponible")
                   && reponse.Donnees.S("version") != Construction.Empreinte;
        }

        public static bool Annonce(Reponse reponse)
        {
            return reponse != null && reponse.Joint && reponse.Ok
                   && Disponible_(reponse);
        }

        public override void Retour()
        {
            if (_etat == Installee)
            {
                Fenetre.Quitter();
                return;
            }
            Fenetre.Aller(_automatique ? (Ecran)Fenetre.Scan : Fenetre.Configuration,
                          null);
        }

        public override bool Touche(Keys touche)
        {
            if (ToucheBase(touche))
                return true;
            switch (touche)
            {
                case Keys.Escape:
                    Retour();
                    return true;
                case Keys.Left:
                    CurseurDeplacer(-1);
                    return true;
                case Keys.Right:
                    CurseurDeplacer(1);
                    return true;
            }
            return false;
        }

        protected override bool Valider()
        {
            if (CurseurActiver())
                return true;
            Retour();
            return true;
        }

        protected override void Activer(string id)
        {
            if (id == "installer") Installer();
            else if (id == "plus-tard") Retour();
            else if (id == "quitter") Fenetre.Quitter();
            else if (id == "reessayer") Verifier();
        }

        // ------------------------------------------------------- verification

        private void Verifier()
        {
            _etat = Verification;
            _detail = "";
            Invalidate();
            Fenetre.Appeler("version", new FonctionReseau(delegate()
            {
                return Api.Maj();
            }), new SuiteReseau(delegate(Reponse reponse)
            {
                if (!reponse.Joint || !reponse.Ok)
                {
                    _etat = Echec;
                    _detail = reponse.Erreur.Length > 0 ? reponse.Erreur
                        : "serveur muet";
                    Invalidate();
                    return;
                }
                Retenir(reponse);
            }));
        }

        // ------------------------------------------------------ installation

        private void Installer()
        {
            if (_etat != Disponible)
                return;
            _etat = Telechargement;
            _detail = "";
            Invalidate();
            string nom = _nom;
            Fenetre.Appeler("telechargement", new FonctionReseau(delegate()
            {
                return Api.Executable(nom);
            }), new SuiteReseau(delegate(Reponse reponse)
            {
                if (!reponse.Joint || !reponse.Ok || reponse.Binaire == null)
                {
                    Echouer(reponse.Erreur.Length > 0 ? reponse.Erreur
                            : "telechargement interrompu");
                    return;
                }
                // Un binaire tronque remplacerait un programme qui marche par
                // un qui ne demarre pas : on refuse avant d'y toucher.
                if (_taille > 0 && reponse.Binaire.Length != _taille)
                {
                    Echouer("recu " + reponse.Binaire.Length + " o au lieu de "
                            + _taille);
                    return;
                }
                if (reponse.Binaire.Length < 20000)
                {
                    Echouer("binaire trop petit, refuse");
                    return;
                }
                Remplacer(reponse.Binaire);
            }));
        }

        private void Echouer(string raison)
        {
            _etat = Echec;
            _detail = raison;
            Fenetre.Dire(raison, false);
            Invalidate();
        }

        /// <summary>Met le nouveau binaire a la place de celui qui tourne.</summary>
        private void Remplacer(byte[] donnees)
        {
            string exe = Reglages.CheminExe;
            string nouveau = exe + SuffixeNouveau;
            string ancien = exe + SuffixeAncien;

            try
            {
                using (FileStream flux = new FileStream(nouveau, FileMode.Create,
                                                        FileAccess.Write))
                    flux.Write(donnees, 0, donnees.Length);
            }
            catch (Exception ex)
            {
                Echouer("ecriture refusee : " + ex.Message);
                return;
            }

            try { File.Delete(ancien); }
            catch (Exception) { }

            try
            {
                // Premier deplacement : s'il echoue, rien n'a bouge.
                File.Move(exe, ancien);
            }
            catch (Exception ex)
            {
                _etat = Echec;
                _detail = "remplacement refuse. Le nouveau binaire est dans "
                          + Nom(nouveau) + ", a renommer a la main.";
                Fenetre.Dire(ex.Message, false);
                Invalidate();
                return;
            }

            try
            {
                File.Move(nouveau, exe);
            }
            catch (Exception ex)
            {
                // Second deplacement rate : on remet l'ancien en place plutot
                // que de laisser le terminal sans programme du tout.
                try { File.Move(ancien, exe); }
                catch (Exception) { }
                Echouer("installation refusee : " + ex.Message);
                return;
            }

            _etat = Installee;
            _detail = "";
            Fenetre.Dire("mise a jour installee", true, Sons.Succes);
            Invalidate();
        }

        private static string Nom(string chemin)
        {
            try { return Path.GetFileName(chemin); }
            catch (Exception) { return chemin; }
        }

        /// <summary>Efface l'ancien binaire laisse par une mise a jour precedente.</summary>
        public static void Nettoyer()
        {
            try
            {
                string ancien = Reglages.CheminExe + SuffixeAncien;
                if (File.Exists(ancien))
                    File.Delete(ancien);
            }
            catch (Exception)
            {
                // Carte en lecture seule, ou fichier encore verrouille : sans
                // consequence, il sera efface au prochain lancement.
            }
        }

        // ------------------------------------------------------------ rendu

        protected override void Peindre(Graphics g, Rectangle zone)
        {
            PeindreBandeau(g, "Mise a jour", true);
            int marge = 8;
            int largeur = Width - 2 * marge;
            int y = HauteurBandeau + 16;

            Theme.Texte_(g, "Version installee", Theme.Petite, Theme.Doux, marge, y);
            Theme.Texte_(g, Construction.Empreinte, Theme.NormaleGras, Theme.Texte,
                         marge, y + 14);
            Theme.Texte_(g, "du " + Construction.Date, Theme.Minuscule, Theme.Doux,
                         marge, y + 30);
            y += 52;
            Theme.Ligne(g, marge, y, Width - marge, Theme.Trait);
            y += 10;

            if (_etat == Verification)
            {
                Theme.Texte_(g, "Interrogation du serveur...", Theme.Normale,
                             Theme.Doux, marge, y);
                return;
            }
            if (_etat == AJour)
            {
                Theme.Texte_(g, "Le programme est a jour.", Theme.Grande, Theme.Vert,
                             marge, y);
                PeindreBoutons(g, marge, largeur, "plus-tard", "Retour", null, null);
                return;
            }
            if (_etat == Echec)
            {
                Theme.Texte_(g, "Echec", Theme.Grande, Theme.Rouge, marge, y);
                y += 22;
                string[] lignes = Theme.Replier(g, _detail, Theme.Petite, largeur, 4);
                for (int i = 0; i < lignes.Length; i++)
                    Theme.Texte_(g, lignes[i], Theme.Petite, Theme.Texte, marge,
                                 y + i * 14);
                PeindreBoutons(g, marge, largeur, "reessayer", "Reessayer",
                               "plus-tard", "Retour");
                return;
            }
            if (_etat == Telechargement)
            {
                Theme.Texte_(g, "Telechargement...", Theme.Grande, Theme.Bleu,
                             marge, y);
                Theme.Texte_(g, (_taille / 1024) + " ko", Theme.Petite, Theme.Doux,
                             marge, y + 22);
                return;
            }
            if (_etat == Installee)
            {
                Theme.Texte_(g, "Installee", Theme.Grande, Theme.Vert, marge, y);
                y += 24;
                string[] lignes = Theme.Replier(g,
                    "Fermez, puis relancez le programme pour demarrer la nouvelle "
                    + "version.", Theme.Petite, largeur, 4);
                for (int i = 0; i < lignes.Length; i++)
                    Theme.Texte_(g, lignes[i], Theme.Petite, Theme.Texte, marge,
                                 y + i * 14);
                PeindreBoutons(g, marge, largeur, "quitter", "Fermer maintenant",
                               null, null);
                return;
            }

            Theme.Texte_(g, "Nouvelle version", Theme.Grande, Theme.Barre, marge, y);
            Theme.Texte_(g, _version, Theme.NormaleGras, Theme.Texte, marge, y + 24);
            Theme.Texte_(g, (_taille / 1024) + " ko a telecharger", Theme.Petite,
                         Theme.Doux, marge, y + 40);
            PeindreBoutons(g, marge, largeur, "installer", "INSTALLER",
                           "plus-tard", "Plus tard");
        }

        private void PeindreBoutons(Graphics g, int marge, int largeur, string id1,
                                    string libelle1, string id2, string libelle2)
        {
            int hauteur = 42;
            int y = BasUtile - 3 - hauteur;
            if (id2 == null)
            {
                BoutonZone(g, new Rectangle(marge, y, largeur, hauteur), id1, libelle1,
                           Theme.Vert, Color.White, Theme.NormaleGras);
                return;
            }
            int demi = (largeur - 6) / 2;
            BoutonZone(g, new Rectangle(marge, y, demi, hauteur), id1, libelle1,
                       Theme.Vert, Color.White, Theme.NormaleGras);
            BoutonZone(g, new Rectangle(marge + demi + 6, y, largeur - demi - 6,
                                        hauteur), id2, libelle2, Theme.FondDoux,
                       Theme.Texte, Theme.Petite);
        }
    }
}
