using System;
using System.IO;
using System.Net;
using System.Text;

namespace Inventaire
{
    /// <summary>Resultat d'un appel : distingue la panne reseau du refus metier.</summary>
    internal sealed class Reponse
    {
        public bool Joint;              // la requete a abouti, quel qu'en soit le verdict
        public bool Ok;                 // le serveur a repondu ok=1
        public string Erreur = "";
        public Kv Donnees = Kv.Analyser("");
        public byte[] Binaire;
        public int Millisecondes;

        public static Reponse Panne(string message)
        {
            Reponse r = new Reponse();
            r.Erreur = message;
            return r;
        }
    }

    /// <summary>
    /// Appels HTTP vers le serveur du PC.
    ///
    /// Trois precautions valent d'etre notees, chacune corrigeant un piege du
    /// Compact Framework 2.0 sur Windows CE :
    ///
    /// * KeepAlive desactive. Le terminal passe son temps en veille ; une
    ///   connexion gardee ouverte se retrouve morte au reveil, et le premier
    ///   appel suivant echoue sans raison visible.
    /// * Proxy vide impose. Sans cela la pile CE consulte les reglages de
    ///   connexion du terminal, souvent herites de l'exploitant precedent,
    ///   et tente de sortir par une passerelle inexistante.
    /// * Aucun code HTTP d'erreur attendu sur les routes metier : le serveur
    ///   repond 200 avec ok=0. Une WebException signale donc toujours un vrai
    ///   probleme de liaison, ce qui rend le diagnostic affichable a l'ecran.
    /// </summary>
    internal sealed class Api
    {
        private readonly Reglages _reglages;

        public Api(Reglages reglages)
        {
            _reglages = reglages;
        }

        private static string Encoder(string valeur)
        {
            return valeur == null ? "" : Uri.EscapeDataString(valeur);
        }

        // ------------------------------------------------------------ routes

        public Reponse Ping()
        {
            return Texte("/api/ping", "", false);
        }

        /// <summary>Version du binaire que le serveur tient a disposition.</summary>
        public Reponse Maj()
        {
            return Texte("/api/maj", "", false);
        }

        /// <summary>Le binaire lui-meme, tel que build.sh l'a depose dans dist/.</summary>
        public Reponse Executable(string nom)
        {
            return Binaire("/telecharger/" + Encoder(nom));
        }

        public Reponse Scan(string code)
        {
            return Texte("/api/scan", "code=" + Encoder(code), false);
        }

        public Reponse Ajouter(string code, int qte, string peremptionIso)
        {
            return Texte("/api/ajouter", "code=" + Encoder(code) + "&qte=" + qte
                         + "&peremption=" + Encoder(peremptionIso), true);
        }

        public Reponse Retirer(string code, int qte, int lot)
        {
            string p = "code=" + Encoder(code) + "&qte=" + qte;
            if (lot > 0)
                p += "&lot=" + lot;
            return Texte("/api/retirer", p, true);
        }

        public Reponse Detail(string code)
        {
            return Texte("/api/detail", "code=" + Encoder(code), false);
        }

        /// <summary>Corrige un lot. qte = 0 le supprime entierement.</summary>
        public Reponse Lot(int lot, int qte, string code)
        {
            return Texte("/api/lot", "lot=" + lot + "&qte=" + qte
                         + "&code=" + Encoder(code) + "&origine=terminal", true);
        }

        public Reponse Nommer(string code, string nom)
        {
            return Texte("/api/nommer", "code=" + Encoder(code) + "&nom=" + Encoder(nom),
                         true);
        }

        public Reponse Inventaire(string tri)
        {
            return Texte("/api/inventaire", "tri=" + Encoder(tri), false);
        }

        public Reponse Image(string code, int taille)
        {
            return Binaire("/api/image?code=" + Encoder(code) + "&l=" + taille
                           + "&h=" + taille + "&img=bmp");
        }

        // --------------------------------------------------------- transport

        private HttpWebRequest Preparer(string url, string methode)
        {
            HttpWebRequest requete = (HttpWebRequest)WebRequest.Create(url);
            requete.Method = methode;
            requete.Timeout = _reglages.DelaiMs;
            requete.KeepAlive = false;
            requete.UserAgent = "Inventaire-CE/" + Programme.Version;
            try
            {
                requete.Proxy = GlobalProxySelection.GetEmptyWebProxy();
            }
            catch (Exception)
            {
                // Pile reseau sans notion de proxy : rien a neutraliser.
            }
            return requete;
        }

        private Reponse Texte(string route, string parametres, bool post)
        {
            int depart = Environment.TickCount;
            Reponse reponse = new Reponse();
            try
            {
                string corpsEnvoye = parametres + (parametres.Length > 0 ? "&" : "") + "fmt=kv";
                string url = _reglages.BaseUrl + route;
                HttpWebRequest requete;
                if (post)
                {
                    requete = Preparer(url, "POST");
                    byte[] charge = Encoding.UTF8.GetBytes(corpsEnvoye);
                    requete.ContentType = "application/x-www-form-urlencoded";
                    requete.ContentLength = charge.Length;
                    using (Stream flux = requete.GetRequestStream())
                        flux.Write(charge, 0, charge.Length);
                }
                else
                {
                    requete = Preparer(url + "?" + corpsEnvoye, "GET");
                }

                byte[] recu;
                using (HttpWebResponse brut = (HttpWebResponse)requete.GetResponse())
                    recu = Aspirer(brut);

                reponse.Joint = true;
                // GetString(octets) n'existe pas sous CF 2.0 : seule la forme
                // a trois arguments est disponible.
                reponse.Donnees = Kv.Analyser(Encoding.UTF8.GetString(recu, 0, recu.Length));
                reponse.Ok = reponse.Donnees.B("ok");
                reponse.Erreur = reponse.Donnees.S("erreur");
                if (!reponse.Ok && reponse.Erreur.Length == 0)
                    reponse.Erreur = "reponse inattendue du serveur";
            }
            catch (WebException ex)
            {
                reponse.Erreur = Diagnostic(ex);
            }
            catch (Exception ex)
            {
                reponse.Erreur = ex.Message;
            }
            reponse.Millisecondes = Environment.TickCount - depart;
            return reponse;
        }

        private Reponse Binaire(string route)
        {
            int depart = Environment.TickCount;
            Reponse reponse = new Reponse();
            try
            {
                HttpWebRequest requete = Preparer(_reglages.BaseUrl + route, "GET");
                using (HttpWebResponse brut = (HttpWebResponse)requete.GetResponse())
                    reponse.Binaire = Aspirer(brut);
                reponse.Joint = true;
                reponse.Ok = reponse.Binaire != null && reponse.Binaire.Length > 0;
            }
            catch (WebException ex)
            {
                // 404 sur une photo est banal : le produit n'en a pas. On le
                // signale sans le presenter comme une panne.
                HttpWebResponse rep = ex.Response as HttpWebResponse;
                if (rep != null)
                {
                    reponse.Joint = true;
                    reponse.Erreur = "pas de photo";
                    try { rep.Close(); } catch (Exception) { }
                }
                else
                {
                    reponse.Erreur = Diagnostic(ex);
                }
            }
            catch (Exception ex)
            {
                reponse.Erreur = ex.Message;
            }
            reponse.Millisecondes = Environment.TickCount - depart;
            return reponse;
        }

        private static byte[] Aspirer(HttpWebResponse reponse)
        {
            using (Stream flux = reponse.GetResponseStream())
            using (MemoryStream tampon = new MemoryStream())
            {
                byte[] morceau = new byte[4096];
                int lus;
                while ((lus = flux.Read(morceau, 0, morceau.Length)) > 0)
                    tampon.Write(morceau, 0, lus);
                return tampon.ToArray();
            }
        }

        /// <summary>Message lisible a l'ecran plutot que la prose du framework.</summary>
        private string Diagnostic(WebException ex)
        {
            switch (ex.Status)
            {
                case WebExceptionStatus.Timeout:
                    return "serveur muet (" + (_reglages.DelaiMs / 1000) + " s)";
                case WebExceptionStatus.ConnectFailure:
                    return "connexion refusee par " + _reglages.Hote;
                case WebExceptionStatus.NameResolutionFailure:
                    return "nom " + _reglages.Hote + " introuvable";
                case WebExceptionStatus.ProxyNameResolutionFailure:
                    return "proxy du terminal a desactiver";
                default:
                    return "reseau : " + ex.Message;
            }
        }
    }
}
