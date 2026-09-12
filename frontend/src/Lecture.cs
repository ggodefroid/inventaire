using System;

namespace Inventaire
{
    /// <summary>
    /// Decide quand un code-barres est complet. Elle n'assemble rien elle-meme :
    /// c'est la zone de saisie qui accumule, et cette classe ne fait que
    /// l'observer.
    ///
    /// Cette separation est le fruit d'une erreur qui a coute un chiffre par
    /// scan pendant deux versions. Windows produit deux flux distincts pour une
    /// touche : WM_KEYDOWN, puis WM_CHAR fabrique par TranslateMessage. Tant
    /// qu'on assemble le code soi-meme a partir des evenements clavier, on
    /// depend de leur entrelacement -- et sur la rafale d'un lecteur
    /// code-barres, rien ne le garantit. Un bloc-notes, lui, n'a jamais ce
    /// probleme : le controle d'edition traite WM_CHAR en interne, et affiche
    /// les treize chiffres.
    ///
    /// On fait donc pareil. Le TextBox accumule nativement, exactement comme
    /// dans un bloc-notes, et l'on se contente de lire son contenu **une fois
    /// la file de messages vidée**. La touche Entree ne conclut plus : elle
    /// annonce que la rafale se termine, et la lecture a lieu
    /// <see cref="ApresEntreeMs"/> plus tard, quand tous les caracteres sont
    /// arrives. Aucun ordonnancement n'est plus suppose.
    /// </summary>
    internal sealed class Lecture
    {
        /// <summary>
        /// Delai entre la touche Entree et la lecture du tampon. Il doit couvrir
        /// le temps qu'il reste aux derniers WM_CHAR pour etre traites ; il est
        /// invisible a cote d'un aller-retour HTTP.
        /// </summary>
        public const int ApresEntreeMs = 170;
        /// <summary>Silence qui clot une rafale sans suffixe.</summary>
        public const int SilenceRafaleMs = 220;
        /// <summary>Au-dela, un caractere isole est une touche, pas un tir.</summary>
        public const int DelaiRaccourciMs = 300;
        /// <summary>Longueur minimale d'une rafale auto-conclue.</summary>
        public const int LongueurRafale = 4;
        /// <summary>Duree pendant laquelle un terminateur retardataire est ignore.</summary>
        public const int GraceApresRafaleMs = 600;

        private string _tampon = "";
        private int _tChangement;
        private bool _rafale;
        private int _entreeRecue;           // 0 = aucune Entree en attente
        private int _clotureAuto;           // 0 = la derniere cloture etait voulue

        public string Tampon { get { return _tampon; } }

        public bool Vide { get { return _tampon.Length == 0; } }

        /// <summary>true si le tampon a ete rempli par un lecteur, pas par un doigt.</summary>
        public bool Rafale { get { return _rafale; } }

        public void Effacer()
        {
            _tampon = "";
            _rafale = false;
            _entreeRecue = 0;
            _clotureAuto = 0;
        }

        /// <summary>
        /// Contenu actuel de la zone de saisie, releve a chaque battement.
        ///
        /// Noter ce que cette methode ne fait **pas** : elle n'annule pas une
        /// Entree en attente. Des caracteres qui arrivent apres le WM_KEYDOWN
        /// d'Entree sont precisement le cas qu'on cherche a rattraper.
        /// </summary>
        public void Observer(string texte)
        {
            if (texte == null)
                texte = "";
            if (texte == _tampon)
                return;
            // Deux caracteres apparus dans le meme battement de 90 ms : aucun
            // doigt ne fait cela, c'est un lecteur. Ce constat autorise la
            // conclusion automatique, et la refuse a une saisie manuelle.
            if (texte.Length >= _tampon.Length + 2)
                _rafale = true;
            if (texte.Length == 0)
                _rafale = false;
            _tampon = texte;
            _tChangement = Environment.TickCount;
        }

        /// <summary>Touche Entree : la rafale se termine, on lira dans un instant.</summary>
        public void Entree()
        {
            if (_tampon.Length == 0 && EnGrace())
            {
                _clotureAuto = 0;           // suffixe retardataire du lecteur : absorbe
                return;
            }
            _entreeRecue = Environment.TickCount;
            if (_entreeRecue == 0)
                _entreeRecue = 1;           // 0 est notre valeur « aucune »
        }

        /// <summary>
        /// A appeler a chaque battement, apres <see cref="Observer"/>. Retourne
        /// true et le tampon lorsqu'il y a lieu de conclure.
        /// </summary>
        public bool Battement(bool raccourcisActifs, out string tampon)
        {
            tampon = null;
            int maintenant = Environment.TickCount;

            // Soustraction d'entiers : reste juste au passage a zero du
            // compteur de ticks, tous les vingt-cinq jours.
            if (_entreeRecue != 0 && maintenant - _entreeRecue >= ApresEntreeMs)
            {
                tampon = Clore();
                return true;
            }
            if (_tampon.Length == 0)
                return false;

            int silence = maintenant - _tChangement;
            if (_rafale && _tampon.Length >= LongueurRafale
                && silence >= SilenceRafaleMs)
            {
                tampon = CloreSeul();       // rafale sans suffixe configure
                return true;
            }
            if (raccourcisActifs && !_rafale && _tampon.Length == 1
                && silence >= DelaiRaccourciMs)
            {
                tampon = CloreSeul();       // touche isolee
                return true;
            }
            return false;
        }

        private string Clore()
        {
            string resultat = _tampon;
            Effacer();
            return resultat;
        }

        /// <summary>
        /// Conclusion decidee par le silence, pas par l'utilisateur.
        ///
        /// Le suffixe du lecteur peut encore arriver derriere : s'il tombait
        /// sur un tampon vide, il declencherait l'action par defaut de l'ecran
        /// -- un ajout non demande. On le laisse donc passer dans le vide
        /// pendant un peu plus d'une demi-seconde.
        /// </summary>
        private string CloreSeul()
        {
            string resultat = Clore();
            _clotureAuto = Environment.TickCount;
            if (_clotureAuto == 0)
                _clotureAuto = 1;
            return resultat;
        }

        private bool EnGrace()
        {
            if (_clotureAuto == 0)
                return false;
            if (Environment.TickCount - _clotureAuto < GraceApresRafaleMs)
                return true;
            _clotureAuto = 0;
            return false;
        }
    }
}
