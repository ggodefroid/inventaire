using System;
using System.Threading;

namespace Inventaire
{
    internal delegate Reponse FonctionReseau();
    internal delegate void SuiteReseau(Reponse reponse);

    /// <summary>
    /// Un appel reseau mene sur un fil de fond, recupere par la boucle d'interface.
    ///
    /// Control.Invoke existe sous CF 2.0, mais son comportement varie d'une
    /// image CE a l'autre et un rendez-vous rate fige l'application sans
    /// message. On s'en passe : le fil de fond depose son resultat sous verrou,
    /// et un Timer de l'interface vient le chercher. Un seul mecanisme, aucun
    /// marshalling, et l'ecran reste vivant pendant l'attente.
    /// </summary>
    internal sealed class Tache
    {
        private readonly FonctionReseau _travail;
        private readonly object _verrou = new object();
        private Reponse _resultat;
        private bool _fini;

        public readonly string Libelle;
        public readonly SuiteReseau Suite;

        public Tache(string libelle, FonctionReseau travail, SuiteReseau suite)
        {
            Libelle = libelle;
            _travail = travail;
            Suite = suite;
        }

        public void Demarrer()
        {
            Thread fil = new Thread(new ThreadStart(Executer));
            fil.IsBackground = true;        // ne doit jamais retenir la fermeture
            fil.Start();
        }

        private void Executer()
        {
            Reponse resultat;
            try
            {
                resultat = _travail();
            }
            catch (Exception ex)
            {
                // Une exception non rattrapee sur un fil de fond tue le
                // processus sous Compact Framework, sans boite de dialogue.
                resultat = Reponse.Panne(ex.Message);
            }
            lock (_verrou)
            {
                _resultat = resultat;
                _fini = true;
            }
        }

        public bool Fini
        {
            get { lock (_verrou) { return _fini; } }
        }

        public Reponse Resultat
        {
            get { lock (_verrou) { return _resultat; } }
        }
    }
}
