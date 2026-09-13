using System;
using System.Collections.Generic;

namespace Inventaire
{
    /// <summary>Un lot : des unites identiques partageant une date de peremption.</summary>
    internal sealed class Lot
    {
        public int Id;
        public int Qte;
        public string Peremption = "";
        public int Jours;
        public bool SansDate;
        public bool SansJour;       // l'emballage n'indiquait qu'un mois
    }

    /// <summary>
    /// Un article tel que le serveur le decrit apres un bip. Toute la lecture
    /// du format `cle=valeur` est concentree ici ; les ecrans ne manipulent
    /// que des champs types.
    /// </summary>
    internal sealed class Article
    {
        public string Code = "";
        public string CodeLu = "";      // avant reparation de la cle, s'il a fallu
        public bool Connu;
        public string Source = "";
        public string Nom = "";
        public string Marque = "";
        public string Contenance = "";
        public bool Image;

        public string Nutriscore = "";
        public int Nova;
        public string Ecoscore = "";
        public string NivGraisses = "";
        public string NivSatures = "";
        public string NivSucres = "";
        public string NivSel = "";

        public double Kcal = -1, Lipides = -1, Satures = -1, Glucides = -1;
        public double Sucres = -1, Proteines = -1, Sel = -1, Fibres = -1;
        public string Allergenes = "";
        public string Traces = "";
        public string Labels = "";

        public int Stock;
        public string Peremption = "";
        public int Jours;
        public bool SansDate = true;
        public bool SansJour;
        public int Suggestion = -1;         // duree de conservation habituelle, en jours
        public Lot[] Lots = new Lot[0];
        public string Message = "";

        /// <summary>Libelle affichable : le nom, ou le code a defaut.</summary>
        public string Libelle
        {
            get { return Nom.Length > 0 ? Nom : Code; }
        }

        public static Article Depuis(Kv kv)
        {
            Article a = new Article();
            a.Code = kv.S("code");
            a.CodeLu = kv.S("code_lu");
            a.Connu = kv.B("connu");
            a.Source = kv.S("source");
            a.Nom = kv.S("nom");
            a.Marque = kv.S("marque");
            a.Contenance = kv.S("contenance");
            a.Image = kv.B("image");

            a.Nutriscore = kv.S("nutriscore");
            a.Nova = kv.I("nova", 0);
            a.Ecoscore = kv.S("ecoscore");
            a.NivGraisses = kv.S("niveaux.graisses");
            a.NivSatures = kv.S("niveaux.satures");
            a.NivSucres = kv.S("niveaux.sucres");
            a.NivSel = kv.S("niveaux.sel");

            a.Kcal = kv.D("nutrition.kcal", -1);
            a.Lipides = kv.D("nutrition.lipides", -1);
            a.Satures = kv.D("nutrition.satures", -1);
            a.Glucides = kv.D("nutrition.glucides", -1);
            a.Sucres = kv.D("nutrition.sucres", -1);
            a.Proteines = kv.D("nutrition.proteines", -1);
            a.Sel = kv.D("nutrition.sel", -1);
            a.Fibres = kv.D("nutrition.fibres", -1);
            a.Allergenes = kv.S("allergenes");
            a.Traces = kv.S("traces");
            a.Labels = kv.S("labels");

            a.Stock = kv.I("stock", 0);
            a.Peremption = kv.S("peremption");
            a.SansDate = a.Peremption.Length == 0;
            a.SansJour = kv.S("precision") == "mois";
            a.Jours = kv.I("jours", 0);
            a.Suggestion = kv.I("suggestion", -1);
            a.Message = kv.S("message");

            int nb = kv.Compte("lots");
            List<Lot> lots = new List<Lot>();
            for (int i = 0; i < nb; i++)
            {
                string p = "lot." + i + ".";
                Lot lot = new Lot();
                lot.Id = kv.I(p + "id", 0);
                lot.Qte = kv.I(p + "qte", 0);
                lot.Peremption = kv.S(p + "peremption");
                lot.SansDate = lot.Peremption.Length == 0;
                lot.SansJour = kv.S(p + "precision") == "mois";
                lot.Jours = kv.I(p + "jours", 0);
                lots.Add(lot);
            }
            a.Lots = lots.ToArray();
            return a;
        }
    }

    /// <summary>
    /// La fiche detaillee : tout ce qu'Open Food Facts sait du produit.
    /// Chargee a la demande, elle ne passe pas par le chemin du scan.
    /// </summary>
    internal sealed class Detail
    {
        public string Code = "";
        public string Nom = "";
        public string Marque = "";
        public string Contenance = "";
        public string Portion = "";
        public string Nutriscore = "";
        public int Nova;
        public string Ecoscore = "";
        public string NivGraisses = "", NivSatures = "", NivSucres = "", NivSel = "";
        public double Kcal = -1, Kj = -1, Lipides = -1, Satures = -1;
        public double Glucides = -1, Sucres = -1, Fibres = -1, Proteines = -1, Sel = -1;
        public string Allergenes = "";
        public string Traces = "";
        public string Additifs = "";
        public string Labels = "";
        public string Categories = "";
        public string Origine = "";
        public string Ingredients = "";
        public string Source = "";

        public static Detail Depuis(Kv kv)
        {
            Detail d = new Detail();
            d.Code = kv.S("code");
            d.Nom = kv.S("nom");
            d.Marque = kv.S("marque");
            d.Contenance = kv.S("contenance");
            d.Portion = kv.S("portion");
            d.Nutriscore = kv.S("nutriscore");
            d.Nova = kv.I("nova", 0);
            d.Ecoscore = kv.S("ecoscore");
            d.NivGraisses = kv.S("niveaux.graisses");
            d.NivSatures = kv.S("niveaux.satures");
            d.NivSucres = kv.S("niveaux.sucres");
            d.NivSel = kv.S("niveaux.sel");
            d.Kcal = kv.D("nutrition.kcal", -1);
            d.Kj = kv.D("nutrition.kj", -1);
            d.Lipides = kv.D("nutrition.lipides", -1);
            d.Satures = kv.D("nutrition.satures", -1);
            d.Glucides = kv.D("nutrition.glucides", -1);
            d.Sucres = kv.D("nutrition.sucres", -1);
            d.Fibres = kv.D("nutrition.fibres", -1);
            d.Proteines = kv.D("nutrition.proteines", -1);
            d.Sel = kv.D("nutrition.sel", -1);
            d.Allergenes = kv.S("allergenes");
            d.Traces = kv.S("traces");
            d.Additifs = kv.S("additifs");
            d.Labels = kv.S("labels");
            d.Categories = kv.S("categories");
            d.Origine = kv.S("origine");
            d.Ingredients = kv.S("ingredients");
            d.Source = kv.S("source");
            return d;
        }
    }

    /// <summary>Une ligne de liste : inventaire complet ou echeances proches.</summary>
    internal sealed class Ligne
    {
        public string Code = "";
        public string Nom = "";
        public string Detail = "";
        public string Nutriscore = "";
        public int Qte;
        public int Jours;
        public bool SansDate = true;
        public bool SansJour;
        public bool Image;

        public static Ligne[] Inventaire(Kv kv)
        {
            int nb = kv.Compte("postes");
            Ligne[] lignes = new Ligne[nb];
            for (int i = 0; i < nb; i++)
            {
                string p = "poste." + i + ".";
                Ligne l = new Ligne();
                l.Code = kv.S(p + "code");
                l.Nom = kv.S(p + "nom");
                l.Nutriscore = kv.S(p + "nutriscore");
                l.Image = kv.B(p + "image");
                l.Qte = kv.I(p + "total", 0);
                l.Peupler(kv.S(p + "peremption"), kv.I(p + "jours", 0));
                l.SansJour = kv.S(p + "precision") == "mois";
                int nbLots = kv.I(p + "nb_lots", 1);
                string marque = kv.S(p + "marque");
                l.Detail = nbLots > 1 ? nbLots + " lots" : marque;
                lignes[i] = l;
            }
            return lignes;
        }

        private void Peupler(string peremption, int jours)
        {
            SansDate = peremption.Length == 0;
            Jours = jours;
        }
    }

    /// <summary>Une ligne de la liste de courses.</summary>
    internal sealed class Course
    {
        public int Id;
        public string Code = "";
        public string Libelle = "";
        public string Detail = "";
        public int Qte;
        public bool Pris;
        public bool Image;

        public static Course[] Depuis(Kv kv)
        {
            int nb = kv.Compte("articles");
            Course[] articles = new Course[nb];
            for (int i = 0; i < nb; i++)
            {
                string p = "article." + i + ".";
                Course c = new Course();
                c.Id = kv.I(p + "id", 0);
                c.Code = kv.S(p + "code");
                c.Libelle = kv.S(p + "libelle");
                c.Qte = kv.I(p + "qte", 1);
                c.Pris = kv.B(p + "pris");
                c.Image = kv.B(p + "image");
                string marque = kv.S(p + "marque");
                string contenance = kv.S(p + "contenance");
                c.Detail = marque.Length > 0 && contenance.Length > 0
                    ? marque + " · " + contenance
                    : (marque.Length > 0 ? marque : contenance);
                articles[i] = c;
            }
            return articles;
        }
    }
}
