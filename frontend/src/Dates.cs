using System;

namespace Inventaire
{
    /// <summary>
    /// Saisie de date au pave numerique.
    ///
    /// Le terminal a un pave de 28 ou 38 touches : pas de '/' commode, pas de
    /// selecteur de date. On accepte donc la frappe la plus courte qui leve
    /// l'ambiguite.
    ///
    ///     "2510"      25 octobre, prochaine occurrence
    ///     "251026"    25/10/2026
    ///     "25102026"  25/10/2026
    ///     ""          date inconnue
    ///
    /// Toutes les methodes prennent une date de reference plutot que de lire
    /// DateTime.Now : l'horloge d'un terminal Windows CE repart a zero au cold
    /// boot, et une date de peremption calculee sur une horloge fausse est pire
    /// qu'une date absente. La reference vient du serveur, a chaque reponse.
    /// </summary>
    internal static class Dates
    {
        public static readonly DateTime Aucune = DateTime.MinValue;

        /// <summary>
        /// Analyse une saisie. true si elle est exploitable ; resultat vaut
        /// Aucune quand l'utilisateur laisse le champ vide.
        /// </summary>
        public static bool Analyser(string texte, DateTime reference, bool relatif,
                                    out DateTime resultat, out string erreur)
        {
            resultat = Aucune;
            erreur = null;
            string chiffres = Chiffres(texte);

            if (chiffres.Length == 0)
                return true;                        // date volontairement inconnue

            if (relatif)
            {
                int jours;
                if (!Entier(chiffres, out jours) || jours < 0 || jours > 3650)
                {
                    erreur = "nombre de jours invalide";
                    return false;
                }
                resultat = reference.AddDays(jours);
                return true;
            }

            int j, m, a;
            switch (chiffres.Length)
            {
                case 4:                             // JJMM
                    if (!Entier(chiffres.Substring(0, 2), out j) ||
                        !Entier(chiffres.Substring(2, 2), out m))
                    {
                        erreur = "date illisible";
                        return false;
                    }
                    if (!Batir(reference.Year, m, j, out resultat, out erreur))
                        return false;
                    // Une peremption est devant nous : 2501 en decembre veut
                    // dire janvier prochain, pas janvier dernier.
                    if (resultat < reference)
                        return Batir(reference.Year + 1, m, j, out resultat, out erreur);
                    return true;

                case 6:                             // JJMMAA
                    if (!Entier(chiffres.Substring(0, 2), out j) ||
                        !Entier(chiffres.Substring(2, 2), out m) ||
                        !Entier(chiffres.Substring(4, 2), out a))
                    {
                        erreur = "date illisible";
                        return false;
                    }
                    return Batir(EtendreAnnee(a, reference), m, j, out resultat, out erreur);

                case 8:                             // JJMMAAAA
                    if (!Entier(chiffres.Substring(0, 2), out j) ||
                        !Entier(chiffres.Substring(2, 2), out m) ||
                        !Entier(chiffres.Substring(4, 4), out a))
                    {
                        erreur = "date illisible";
                        return false;
                    }
                    return Batir(a, m, j, out resultat, out erreur);

                default:
                    erreur = "tapez JJMM, JJMMAA ou JJMMAAAA";
                    return false;
            }
        }

        /// <summary>Annee sur deux chiffres, dans une fenetre -10 / +89 ans.</summary>
        private static int EtendreAnnee(int aa, DateTime reference)
        {
            int siecle = reference.Year - (reference.Year % 100);
            int annee = siecle + aa;
            if (annee < reference.Year - 10)
                annee += 100;
            return annee;
        }

        private static bool Batir(int a, int m, int j, out DateTime resultat,
                                  out string erreur)
        {
            resultat = Aucune;
            erreur = null;
            if (m < 1 || m > 12)
            {
                erreur = "mois " + m + " invalide";
                return false;
            }
            if (a < 1970 || a > 2999)
            {
                erreur = "annee " + a + " invalide";
                return false;
            }
            if (j < 1 || j > DateTime.DaysInMonth(a, m))
            {
                erreur = "jour " + j + " invalide pour ce mois";
                return false;
            }
            resultat = new DateTime(a, m, j);
            return true;
        }

        /// <summary>Format attendu par le serveur.</summary>
        public static string VersIso(DateTime valeur)
        {
            return valeur == Aucune ? "" : valeur.ToString("yyyy-MM-dd");
        }

        public static DateTime DepuisIso(string iso)
        {
            if (iso == null || iso.Length != 10)
                return Aucune;
            try
            {
                return new DateTime(int.Parse(iso.Substring(0, 4)),
                                    int.Parse(iso.Substring(5, 2)),
                                    int.Parse(iso.Substring(8, 2)));
            }
            catch (Exception)
            {
                return Aucune;
            }
        }

        public static string Court(DateTime valeur)
        {
            return valeur == Aucune ? "sans date" : valeur.ToString("dd/MM/yy");
        }

        /// <summary>
        /// « fin 11/26 » : ce que porte l'emballage quand il n'indique qu'un
        /// mois. Afficher « 30/11/26 » laisserait croire a une precision que
        /// le produit n'a pas.
        /// </summary>
        public static string Mois(DateTime valeur)
        {
            return valeur == Aucune ? "sans date" : "fin " + valeur.ToString("MM/yy");
        }

        /// <summary>Libelle d'une echeance, selon ce que l'emballage indiquait.</summary>
        public static string Libelle(DateTime valeur, bool sansJour)
        {
            if (valeur == Aucune)
                return "sans date";
            return sansJour ? Mois(valeur) : Jour(valeur) + " " + Court(valeur);
        }

        /// <summary>Format attendu par le serveur quand seul le mois est connu.</summary>
        public static string VersIsoMois(DateTime valeur)
        {
            return valeur == Aucune ? "" : valeur.ToString("yyyy-MM");
        }

        /// <summary>Jour de la semaine abrege : utile pour relire une date tapee.</summary>
        public static string Jour(DateTime valeur)
        {
            if (valeur == Aucune)
                return "";
            string[] noms = { "dim", "lun", "mar", "mer", "jeu", "ven", "sam" };
            return noms[(int)valeur.DayOfWeek];
        }

        public static int JoursRestants(DateTime valeur, DateTime reference)
        {
            if (valeur == Aucune)
                return int.MaxValue;
            return (int)(valeur.Date - reference.Date).TotalDays;
        }

        public static string Chiffres(string texte)
        {
            if (texte == null)
                return "";
            char[] tampon = new char[texte.Length];
            int n = 0;
            for (int i = 0; i < texte.Length; i++)
            {
                if (texte[i] >= '0' && texte[i] <= '9')
                    tampon[n++] = texte[i];
            }
            return new string(tampon, 0, n);
        }

        // int.TryParse n'existe pas sous CF 2.0.
        private static bool Entier(string texte, out int valeur)
        {
            valeur = 0;
            try
            {
                valeur = int.Parse(texte);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
