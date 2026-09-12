using System;
using System.Drawing;
using System.Windows.Forms;

namespace Inventaire
{
    /// <summary>
    /// La fenetre unique : elle heberge les ecrans, route le clavier et porte
    /// la seule requete reseau en cours.
    ///
    /// Un seul appel a la fois, volontairement. La gachette d'un terminal
    /// code-barres part vite et part parfois deux fois ; serialiser les appels
    /// evite qu'un double bip ajoute deux unites, et rend l'etat affiche
    /// toujours coherent avec la derniere reponse recue.
    /// </summary>
    internal sealed class Fenetre : Form
    {
        public readonly Reglages Reglages;
        public readonly Api Api;
        public readonly CachePhotos Photos;

        /// <summary>
        /// Date du jour telle que la connait le serveur. L'horloge d'un
        /// terminal Windows CE derive et repart a zero au cold boot ; caler le
        /// calendrier sur le PC evite qu'un « J+7 » tombe en 2005.
        /// </summary>
        public DateTime Aujourdhui = DateTime.Now.Date;

        public string Message = "";
        public bool MessageOk = true;

        public EcranScan Scan;
        public EcranArticle Article;
        public EcranSaisie Saisie;
        public EcranDetail Detail;
        public EcranListe Liste;
        public EcranConfiguration Configuration;
        public EcranPhoto Visionneuse;
        public EcranMaj Maj;

        private readonly TextBox _puits;
        private readonly Veille _veille;
        private readonly Timer _horloge;
        private int _derniereActivite;
        private bool _endormi;
        private int _echapTraiteA;
        private bool _majVerifiee;
        private Ecran _actif;
        private Tache _tache;
        private int _messageExpire;
        private int _battements;

        // Horloge calee sur le serveur : secondes depuis minuit, et le
        // compteur de ticks au moment ou on les a recues.
        private int _secondesServeur = -1;
        private int _ticksServeur;

        private int _charge = Systeme.Inconnu;
        private bool _secteur;
        private int _batterieLue;

        public Fenetre(Reglages reglages)
        {
            Reglages = reglages;
            Api = new Api(reglages);
            Photos = new CachePhotos(this);
            Sons.Niveau(reglages.Bip);
            if (reglages.VolumeMax)
                Sons.VolumeMaximum();

            Text = "Inventaire";
            BackColor = Theme.Fond;
            FormBorderStyle = FormBorderStyle.None;
            // Voir OnKeyDown : sans cela, Echap n'arrive jamais jusqu'a nous.
            KeyPreview = true;

            // Puits de touches : un TextBox d'un pixel, toujours focalise.
            // C'est lui qui recoit ce que tape la gachette en emulation clavier.
            //
            // Il accumule le texte **nativement**, comme le ferait un
            // bloc-notes, et les ecrans se contentent de lire son contenu a
            // chaque battement. C'est ce qui garantit qu'aucun caractere ne se
            // perd : le controle d'edition traite WM_CHAR en interne, sans
            // dependre de l'ordre dans lequel la file de messages se vide.
            //
            // Les ecrans peignent leur propre saisie en gros caracteres : a
            // bout de bras, un TextBox natif d'un pixel ne se lit pas.
            _puits = new TextBox();
            _puits.Bounds = new Rectangle(0, 0, 1, 1);
            _puits.MaxLength = 64;
            _puits.BorderStyle = BorderStyle.None;
            _puits.BackColor = Theme.Fond;
            _puits.ForeColor = Theme.Fond;
            _puits.KeyDown += new KeyEventHandler(PuitsTouche);
            _puits.KeyPress += new KeyPressEventHandler(PuitsCaractere);
            Controls.Add(_puits);

            Scan = new EcranScan(this);
            Article = new EcranArticle(this);
            Saisie = new EcranSaisie(this);
            Detail = new EcranDetail(this);
            Liste = new EcranListe(this);
            Configuration = new EcranConfiguration(this);
            Visionneuse = new EcranPhoto(this);
            Maj = new EcranMaj(this);
            foreach (Ecran ecran in new Ecran[] { Scan, Article, Saisie, Detail,
                                                  Liste, Configuration, Visionneuse,
                                                  Maj })
            {
                ecran.Visible = false;
                Controls.Add(ecran);
            }

            PleinEcran();

            _veille = new Veille();
            _veille.MouseDown += new MouseEventHandler(VeilleTouchee);
            Controls.Add(_veille);
            _derniereActivite = Environment.TickCount;

            _horloge = new Timer();
            _horloge.Interval = 90;
            _horloge.Tick += new EventHandler(Battement);
            _horloge.Enabled = true;
        }

        // -------------------------------------------------------- plein ecran

        /// <summary>
        /// Occuper reellement les 240x320.
        ///
        /// FormBorderStyle.None ne suffit pas, et WindowState.Maximized non
        /// plus : ce dernier se cale sur la zone de travail, c'est-a-dire
        /// l'ecran moins la barre des taches. Il faut donc poser les bornes
        /// soi-meme sur l'ecran entier, et cacher la barre.
        /// </summary>
        private void PleinEcran()
        {
#if WINCE
            // Cacher la barre d'abord : la zone de travail s'en trouve
            // agrandie, et ce qui suit n'a plus a lutter contre elle.
            Systeme.BarreDesTaches(false);
            if (!Systeme.PleinEcran(Handle))
            {
                Rectangle ecran = Screen.PrimaryScreen.Bounds;
                if (ecran.Width > 0 && ecran.Height > 0 && Bounds != ecran)
                    Bounds = ecran;
            }
#else
            if (ClientSize.Width != 240)
            {
                FormBorderStyle = FormBorderStyle.FixedSingle;
                StartPosition = FormStartPosition.CenterScreen;
                MaximizeBox = false;
                ClientSize = new Size(240, 320);
            }
#endif
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            // Une boite de dialogue, le clavier virtuel ou une notification du
            // systeme font remonter la barre des taches : on la recouche.
            PleinEcran();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Sans cela, l'utilisateur se retrouve devant un bureau Windows CE
            // sans barre des taches ni menu Demarrer.
            Systeme.BarreDesTaches(true);
            base.OnClosing(e);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            PleinEcran();
            Poser();
            // Restes d'une mise a jour precedente : sans consequence, mais
            // autant ne pas laisser trainer un binaire mort sur la carte.
            EcranMaj.Nettoyer();
            Sons.Jouer(Sons.Demarrage);
            Aller(Scan, null);
            // Premier contact : sans lui, l'utilisateur ne saurait pas que le
            // serveur est injoignable avant son premier bip.
            Appeler("connexion", new FonctionReseau(PingBrut),
                    new SuiteReseau(ApresPing));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Poser();
        }

        private void Poser()
        {
            // OnResize part des le constructeur, quand les bornes sont posees --
            // donc avant que les controles existent.
            if (_puits == null)
                return;
            Rectangle zone = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
            foreach (Control controle in Controls)
            {
                if (controle is Ecran || controle == _veille)
                    controle.Bounds = zone;
            }
            // Le puits se cache dans le dernier pixel : invisible, mais bien
            // dans la surface cliente, donc capable de garder le focus.
            _puits.Bounds = new Rectangle(Math.Max(0, ClientSize.Width - 1),
                                          Math.Max(0, ClientSize.Height - 1), 1, 1);
        }

        // ------------------------------------------------------- navigation

        public void Aller(Ecran destination, object argument)
        {
            if (_actif != null && _actif != destination)
            {
                _actif.Sortir();
                _actif.Visible = false;
            }
            if (_actif != destination)
                Sons.Jouer(Sons.Navigation);
            _actif = destination;
            ViderPuits();
            destination.Visible = true;
            destination.BringToFront();
            destination.Entrer(argument);
            destination.Invalidate();
            Focaliser();
        }

        public Ecran Actif { get { return _actif; } }

        private void Focaliser()
        {
            try { _puits.Focus(); }
            catch (Exception) { }
        }

        /// <summary>
        /// Vide la zone de saisie. Appelee a la navigation et quand un ecran a
        /// conclu sa lecture -- jamais a la fin d'une requete reseau, qui peut
        /// tomber au milieu d'une rafale.
        /// </summary>
        public void ViderPuits()
        {
            _puits.Text = "";
        }

        // ---------------------------------------------------------- clavier

        /// <summary>
        /// Echap, interceptee avant le puits de touches.
        ///
        /// Les fleches et les touches de fonction arrivent bien jusqu'au
        /// TextBox ; Echap, non. Le controle d'edition de Windows CE la
        /// consomme pour son propre compte, comme le font les champs de saisie
        /// depuis toujours. KeyPreview place le formulaire devant lui dans la
        /// chaine, ce qui est le seul moyen de la recuperer.
        /// </summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && _actif != null)
            {
                _echapTraiteA = Environment.TickCount;
                if (_echapTraiteA == 0)
                    _echapTraiteA = 1;
                Reveiller(true);
                if (_actif.Touche(Keys.Escape))
                    e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        private void PuitsTouche(object envoyeur, KeyEventArgs e)
        {
            // Si KeyPreview a deja servi Echap, ne pas la traiter deux fois :
            // sur un ecran de detail, cela reculerait de deux crans.
            if (e.KeyCode == Keys.Escape && _echapTraiteA != 0
                && Environment.TickCount - _echapTraiteA < 200)
                return;
            // Une touche reveille et agit : une gachette ne doit pas demander
            // deux tirs, le premier pour reveiller et le second pour scanner.
            Reveiller(true);
            // On ne touche surtout pas a _puits.Text ici : ecrire dans un
            // TextBox depuis son propre gestionnaire de touche peut avaler le
            // WM_CHAR qui suit. Le texte ne s'accumule pas de toute facon,
            // PuitsCaractere consomme tout.
            if (_actif == null)
                return;
            if (ToucheGlobale(e.KeyCode))
            {
                e.Handled = true;
                return;
            }
            if (_actif.Touche(e.KeyCode))
                e.Handled = true;
        }

        private void PuitsCaractere(object envoyeur, KeyPressEventArgs e)
        {
            // Seul un caractere imprimable reveille. Echap en produit un aussi
            // -- le code 27 -- et reveillerait l'appareil au moment meme ou
            // elle vient de l'endormir.
            if (e.KeyChar >= ' ')
                Reveiller(false);   // sans son : une rafale en produirait treize
            if (_actif == null)
            {
                e.Handled = true;
                return;
            }
            if (_actif.LectureActive)
                return;         // on laisse le controle d'edition accumuler
            // Ecrans de saisie : pas de rafale attendue, donc pas d'ordre a
            // craindre. On consomme le caractere et on le remet a l'ecran.
            e.Handled = true;
            _actif.Caractere(e.KeyChar);
        }

        /// <summary>
        /// Les touches de fonction menent toujours au meme endroit, depuis
        /// n'importe quel ecran. Sur un terminal tenu a bout de bras, un
        /// raccourci qui change de sens selon le contexte ne s'apprend jamais.
        /// </summary>
        private bool ToucheGlobale(Keys touche)
        {
            if (!_actif.ToucheGlobaleAutorisee)
                return false;
            switch (touche)
            {
                case Keys.F1:
                    Aller(Liste, null);
                    return true;
                case Keys.F2:
                    Aller(Scan, null);
                    return true;
                case Keys.F3:
                    Aller(Configuration, null);
                    return true;
            }
            return false;
        }

        // ----------------------------------------------------------- reseau

        public bool Occupe { get { return _tache != null; } }

        public string LibelleOccupe
        {
            get { return _tache == null ? "" : _tache.Libelle; }
        }

        public string Sablier()
        {
            string points = "...";
            return points.Substring(0, 1 + (_battements / 4) % 3);
        }

        /// <summary>
        /// Lance un appel en tache de fond. Un appel deja en cours a la
        /// priorite : le nouveau est simplement ignore, et l'utilisateur le
        /// voit a la barre bleue qui n'a pas bouge.
        /// </summary>
        public bool Appeler(string libelle, FonctionReseau travail, SuiteReseau suite)
        {
            if (_tache != null)
                return false;
            _tache = new Tache(libelle, travail, suite);
            _tache.Demarrer();
            if (_actif != null)
                _actif.Invalidate();
            return true;
        }

        private void Battement(object envoyeur, EventArgs e)
        {
            _battements++;
            if (_actif != null)
            {
                // Relever d'abord ce que la zone de saisie contient, decider
                // ensuite : Battement() doit travailler sur un tampon a jour.
                if (_actif.LectureActive)
                    _actif.ObserverPuits(_puits.Text);
                _actif.Battre();
            }
            RelireBatterie();
            Sons.Battement();

            if (_tache != null && _tache.Fini)
            {
                Tache terminee = _tache;
                _tache = null;
                Reponse reponse = terminee.Resultat;
                Retenir(reponse);
                try
                {
                    if (terminee.Suite != null)
                        terminee.Suite(reponse);
                }
                catch (Exception ex)
                {
                    Dire("erreur interne : " + ex.Message, false);
                }
                Focaliser();
                if (_actif != null)
                    _actif.Invalidate();
                return;
            }

            if (_messageExpire != 0 && Environment.TickCount > _messageExpire)
            {
                _messageExpire = 0;
                Message = "";
                if (_actif != null)
                    _actif.Invalidate();
            }

            GererVeille();

            // Les photos se chargent dans les creux : jamais devant une action
            // de l'utilisateur, toujours des que le reseau est libre -- et
            // jamais pendant la veille, ou personne ne les regarde.
            if (_tache == null && !_endormi)
                Photos.Travailler();
            // Le sablier s'anime, et l'horloge du bandeau avance de minute en
            // minute : un rafraichissement par seconde suffit a l'un comme a
            // l'autre, et epargne le processeur du terminal.
            if (_actif != null && (_tache != null || _battements % 11 == 0))
                _actif.Invalidate();
        }

        // ------------------------------------------------------------ veille

        /// <summary>Note qu'il vient de se passer quelque chose.</summary>
        public void Activite()
        {
            _derniereActivite = Environment.TickCount;
            if (_derniereActivite == 0)
                _derniereActivite = 1;
        }

        private void GererVeille()
        {
            int inactif = (Environment.TickCount - _derniereActivite) / 1000;
            if (_endormi)
            {
                if (Reglages.NoirS > 0 && inactif >= Reglages.NoirS)
                    _veille.Noircir();
                else
                    _veille.Animer();
                return;
            }
            // veille_s = 0 ne desactive que le declenchement automatique :
            // Echap doit endormir l'appareil quoi qu'il arrive.
            if (Reglages.VeilleS > 0 && inactif >= Reglages.VeilleS)
                Endormir();
        }

        /// <summary>Met en veille tout de suite, sans attendre le delai.</summary>
        public void Endormir()
        {
            if (_endormi)
                return;
            _endormi = true;
            _veille.Bounds = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
            _veille.Demarrer();
            _veille.Visible = true;
            _veille.BringToFront();
            Sons.Jouer(Sons.Balayage);
        }

        /// <summary>
        /// Sort de veille. Appelee a chaque entree, qu'on dorme ou non : c'est
        /// elle qui tient le compteur d'inactivite.
        /// </summary>
        public void Reveiller(bool sonner)
        {
            Activite();
            if (!_endormi)
                return;
            _endormi = false;
            _veille.Visible = false;
            if (_actif != null)
            {
                _actif.BringToFront();
                _actif.Invalidate();
            }
            Focaliser();
            if (sonner)
                Sons.Jouer(Sons.Navigation);
        }

        /// <summary>
        /// Un appui sur l'ecran en veille reveille sans rien declencher : le
        /// voile est au-dessus, le bouton qui se trouvait dessous n'est pas
        /// presse par megarde.
        /// </summary>
        private void VeilleTouchee(object envoyeur, MouseEventArgs e)
        {
            Reveiller(true);
        }

        /// <summary>Cale calendrier et horloge sur le serveur.</summary>
        private void Retenir(Reponse reponse)
        {
            if (reponse == null || !reponse.Joint)
                return;
            string jour = reponse.Donnees.S("aujourdhui");
            if (jour.Length == 10)
            {
                DateTime lue = Dates.DepuisIso(jour);
                if (lue != Dates.Aucune)
                    Aujourdhui = lue;
            }
            string heure = reponse.Donnees.S("heure");
            if (heure.Length >= 5)
            {
                try
                {
                    int h = int.Parse(heure.Substring(0, 2));
                    int m = int.Parse(heure.Substring(3, 2));
                    int s = heure.Length >= 8 ? int.Parse(heure.Substring(6, 2)) : 0;
                    _secondesServeur = h * 3600 + m * 60 + s;
                    _ticksServeur = Environment.TickCount;
                }
                catch (Exception) { }
            }
        }

        /// <summary>
        /// Heure a afficher. Celle du serveur, extrapolee entre deux appels :
        /// l'horloge du terminal, elle, n'est pas fiable.
        /// </summary>
        public string TexteHeure()
        {
            if (_secondesServeur < 0)
                return DateTime.Now.ToString("HH:mm");
            int secondes = _secondesServeur
                           + (Environment.TickCount - _ticksServeur) / 1000;
            secondes = ((secondes % 86400) + 86400) % 86400;
            int heures = secondes / 3600;
            int minutes = (secondes % 3600) / 60;
            return (heures < 10 ? "0" : "") + heures + ":"
                   + (minutes < 10 ? "0" : "") + minutes;
        }

        /// <summary>Charge de la batterie, relue au plus une fois par quinze secondes.</summary>
        public int Batterie(out bool secteur)
        {
            secteur = _secteur;
            return _charge;
        }

        private void RelireBatterie()
        {
            if (_batterieLue != 0 && Environment.TickCount - _batterieLue < 15000)
                return;
            _batterieLue = Environment.TickCount;
            if (_batterieLue == 0)
                _batterieLue = 1;
            bool secteur;
            int charge = Systeme.Batterie(out secteur);
            if (charge != _charge || secteur != _secteur)
            {
                _charge = charge;
                _secteur = secteur;
                if (_actif != null)
                    _actif.Invalidate();
            }
        }

        private Reponse PingBrut()
        {
            return Api.Ping();
        }

        private void ApresPing(Reponse reponse)
        {
            if (reponse.Joint && reponse.Ok)
            {
                Dire("serveur " + reponse.Donnees.S("version") + " · "
                     + reponse.Millisecondes + " ms", true);
                if (Reglages.MajAuto && !_majVerifiee)
                {
                    _majVerifiee = true;
                    Appeler("version", new FonctionReseau(VersionBrute),
                            new SuiteReseau(ApresVersion));
                }
            }
            else
            {
                Dire(reponse.Erreur.Length > 0 ? reponse.Erreur : "serveur injoignable",
                     false);
            }
        }

        private Reponse VersionBrute()
        {
            return Api.Maj();
        }

        /// <summary>
        /// On ne derange l'utilisateur que s'il y a vraiment quelque chose a
        /// installer : sinon le demarrage reste une page blanche et un bip.
        /// </summary>
        private void ApresVersion(Reponse reponse)
        {
            if (EcranMaj.Annonce(reponse))
                Aller(Maj, reponse);
        }

        // --------------------------------------------------------- messages

        public void Dire(string texte, bool succes)
        {
            Dire(texte, succes, succes ? Sons.Succes : Sons.Erreur);
        }

        /// <summary>
        /// Message du bas, avec l'effet sonore de son choix. Les actions qui
        /// ont leur propre son -- ajout, retrait, suppression -- le passent
        /// ici plutot que d'en declencher un second par-dessus.
        /// </summary>
        public void Dire(string texte, bool succes, int son)
        {
            Message = texte == null ? "" : texte;
            MessageOk = succes;
            _messageExpire = Environment.TickCount + (succes ? 3500 : 7000);
            Sons.Jouer(son);
            if (_actif != null)
                _actif.Invalidate();
        }

        public void Quitter()
        {
            _horloge.Enabled = false;
            Sons.Jouer(Sons.Fermeture);     // synchrone : on l'entend avant de partir
            Systeme.BarreDesTaches(true);
            Close();
        }
    }
}
