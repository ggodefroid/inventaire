using System;
using System.Collections.Generic;

namespace Inventaire
{
    /// <summary>
    /// Analyseur du format `cle=valeur` renvoye par le serveur.
    ///
    /// Le serveur sait parler JSON, mais ecrire un analyseur JSON solide en
    /// C# 2.0 represente quelques centaines de lignes a risque pour des
    /// reponses qui tiennent en un kilo-octet. Le format plat se lit ici en
    /// une trentaine de lignes, se relit dans un navigateur, et se compare a
    /// l'oeil quand quelque chose cloche.
    ///
    ///     stock=3
    ///     lots=2
    ///     lot.0.qte=2
    ///     lot.0.peremption=2026-10-01
    /// </summary>
    internal sealed class Kv
    {
        private readonly Dictionary<string, string> _valeurs =
            new Dictionary<string, string>();

        public static Kv Analyser(string texte)
        {
            Kv kv = new Kv();
            if (texte == null)
                return kv;
            string[] lignes = texte.Split('\n');
            for (int i = 0; i < lignes.Length; i++)
            {
                string ligne = lignes[i];
                if (ligne.Length == 0)
                    continue;
                if (ligne[ligne.Length - 1] == '\r')
                    ligne = ligne.Substring(0, ligne.Length - 1);
                int egal = ligne.IndexOf('=');
                if (egal <= 0)
                    continue;
                // Une valeur peut contenir des '=' : on ne coupe qu'au premier.
                kv._valeurs[ligne.Substring(0, egal)] =
                    Desechapper(ligne.Substring(egal + 1));
            }
            return kv;
        }

        private static string Desechapper(string valeur)
        {
            if (valeur.IndexOf('\\') < 0)
                return valeur;
            System.Text.StringBuilder sb = new System.Text.StringBuilder(valeur.Length);
            for (int i = 0; i < valeur.Length; i++)
            {
                if (valeur[i] != '\\' || i + 1 >= valeur.Length)
                {
                    sb.Append(valeur[i]);
                    continue;
                }
                i++;
                if (valeur[i] == 'n') sb.Append('\n');
                else if (valeur[i] == 'r') sb.Append('\r');
                else sb.Append(valeur[i]);          // '\\' et tout le reste
            }
            return sb.ToString();
        }

        public bool Contient(string cle)
        {
            return _valeurs.ContainsKey(cle);
        }

        public string S(string cle)
        {
            return S(cle, "");
        }

        public string S(string cle, string defaut)
        {
            string valeur;
            if (_valeurs.TryGetValue(cle, out valeur) && valeur.Length > 0)
                return valeur;
            return defaut;
        }

        // int.TryParse n'existe pas sous CF 2.0 : conversion explicite et rattrapage.
        public int I(string cle, int defaut)
        {
            string valeur;
            if (!_valeurs.TryGetValue(cle, out valeur) || valeur.Length == 0)
                return defaut;
            try { return int.Parse(valeur.Trim()); }
            catch (Exception) { return defaut; }
        }

        public double D(string cle, double defaut)
        {
            string valeur;
            if (!_valeurs.TryGetValue(cle, out valeur) || valeur.Length == 0)
                return defaut;
            try
            {
                // Le serveur ecrit toujours un point decimal ; le terminal, lui,
                // est en locale francaise. Sans la culture invariante, 30.9
                // deviendrait 309.
                return double.Parse(valeur.Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception) { return defaut; }
        }

        public bool B(string cle)
        {
            return S(cle) == "1";
        }

        /// <summary>Longueur annoncee d'une liste (`lots=2`).</summary>
        public int Compte(string cle)
        {
            return I(cle, 0);
        }
    }
}
