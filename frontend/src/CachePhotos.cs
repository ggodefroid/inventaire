using System;
using System.Collections.Generic;
using System.Drawing;

namespace Inventaire
{
    /// <summary>Ou revenir apres avoir regarde une photo en grand.</summary>
    internal sealed class DemandePhoto
    {
        public readonly string Code;
        public readonly string Nom;
        public readonly Ecran Retour;
        public readonly object Argument;

        public DemandePhoto(string code, string nom, Ecran retour, object argument)
        {
            Code = code;
            Nom = nom;
            Retour = retour;
            Argument = argument;
        }
    }

    /// <summary>
    /// Photos gardees en memoire, et file d'attente de celles qui manquent.
    ///
    /// L'ecran de liste demande sept vignettes d'un coup. Les charger l'une
    /// apres l'autre a la volee, en bloquant l'interface, serait insupportable ;
    /// les charger en parallele ferait sept connexions simultanees sur une pile
    /// reseau de 2005. On les met donc en file, et elles se chargent **dans les
    /// creux** : des qu'aucune action de l'utilisateur n'occupe le reseau, une
    /// vignette part. La liste s'affiche tout de suite, les images arrivent
    /// derriere, et un geste de l'utilisateur passe toujours devant.
    ///
    /// Une photo deja vue ne se redemande jamais : c'est ce qui rend le
    /// defilement de la liste immediat au second passage.
    /// </summary>
    internal sealed class CachePhotos
    {
        /// <summary>Au-dela, on libere les plus anciennes.</summary>
        private const int Maximum = 40;
        /// <summary>Apres une panne reseau, on laisse le temps a la liaison.</summary>
        private const int PauseReseauMs = 5000;

        private readonly Fenetre _fenetre;
        private readonly Dictionary<string, Bitmap> _images =
            new Dictionary<string, Bitmap>();
        private readonly List<string> _ordre = new List<string>();
        private readonly List<string> _file = new List<string>();
        private readonly Dictionary<string, bool> _tentees =
            new Dictionary<string, bool>();
        private string _encours;
        private int _pauseJusqua;

        public CachePhotos(Fenetre fenetre)
        {
            _fenetre = fenetre;
        }

        private static string Cle(string code, int taille)
        {
            return taille + ":" + code;
        }

        /// <summary>La photo si elle est la, null sinon. Ne declenche rien.</summary>
        public Bitmap Obtenir(string code, int taille)
        {
            Bitmap image;
            if (code != null && _images.TryGetValue(Cle(code, taille), out image))
                return image;
            return null;
        }

        /// <summary>true si la photo a ete demandee et n'existe pas.</summary>
        public bool Absente(string code, int taille)
        {
            string cle = Cle(code, taille);
            return _tentees.ContainsKey(cle) && !_images.ContainsKey(cle);
        }

        /// <summary>Met la photo en file si elle manque. Sans effet sinon.</summary>
        public void Demander(string code, int taille)
        {
            if (code == null || code.Length == 0 || !_fenetre.Reglages.Photos)
                return;
            string cle = Cle(code, taille);
            if (_images.ContainsKey(cle) || _tentees.ContainsKey(cle)
                || cle == _encours || _file.Contains(cle))
                return;
            _file.Add(cle);
        }

        /// <summary>Oublie toutes les tailles d'un code : sa photo a change.</summary>
        public void Oublier(string code)
        {
            for (int i = _ordre.Count - 1; i >= 0; i--)
            {
                if (_ordre[i].EndsWith(":" + code))
                    Liberer(_ordre[i]);
            }
            List<string> a_retirer = new List<string>();
            foreach (KeyValuePair<string, bool> tentee in _tentees)
            {
                if (tentee.Key.EndsWith(":" + code))
                    a_retirer.Add(tentee.Key);
            }
            for (int i = 0; i < a_retirer.Count; i++)
                _tentees.Remove(a_retirer[i]);
        }

        /// <summary>
        /// Appelee a chaque battement, uniquement quand la fenetre est libre.
        /// </summary>
        public void Travailler()
        {
            if (_encours != null || _file.Count == 0)
                return;
            if (_pauseJusqua != 0)
            {
                if (Environment.TickCount - _pauseJusqua < 0)
                    return;
                _pauseJusqua = 0;
            }

            string cle = _file[0];
            _file.RemoveAt(0);
            int separateur = cle.IndexOf(':');
            int taille;
            try { taille = int.Parse(cle.Substring(0, separateur)); }
            catch (Exception) { return; }
            string code = cle.Substring(separateur + 1);
            _encours = cle;

            bool lance = _fenetre.Appeler("photo", new FonctionReseau(delegate()
            {
                return _fenetre.Api.Image(code, taille);
            }), new SuiteReseau(delegate(Reponse reponse)
            {
                _encours = null;
                if (!reponse.Joint)
                {
                    // Liaison coupee : on remet en file et on souffle, plutot
                    // que de marteler le reseau a chaque battement.
                    _file.Insert(0, cle);
                    _pauseJusqua = Environment.TickCount + PauseReseauMs;
                    if (_pauseJusqua == 0)
                        _pauseJusqua = 1;
                    return;
                }
                _tentees[cle] = true;       // le serveur a repondu : plus de reprise
                if (reponse.Ok && reponse.Binaire != null)
                {
                    Bitmap image = Photo.Depuis(reponse.Binaire,
                        _fenetre.Reglages.DecodeurBmp == "maison");
                    if (image != null)
                        Ranger(cle, image);
                }
            }));
            if (!lance)
            {
                _file.Insert(0, cle);       // une action de l'utilisateur est passee devant
                _encours = null;
            }
        }

        private void Ranger(string cle, Bitmap image)
        {
            Liberer(cle);
            _images[cle] = image;
            _ordre.Add(cle);
            while (_ordre.Count > Maximum)
                Liberer(_ordre[0]);
        }

        private void Liberer(string cle)
        {
            Bitmap image;
            if (_images.TryGetValue(cle, out image))
            {
                // Sans danger : l'eviction a lieu depuis le minuteur, jamais au
                // milieu d'un rendu, et les ecrans ne gardent pas de reference
                // d'une peinture a l'autre.
                image.Dispose();
                _images.Remove(cle);
            }
            _ordre.Remove(cle);
        }

        public void Vider()
        {
            while (_ordre.Count > 0)
                Liberer(_ordre[0]);
            _file.Clear();
            _tentees.Clear();
        }
    }
}
