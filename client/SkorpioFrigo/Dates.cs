using System;

namespace SkorpioFrigo
{
    /// <summary>
    /// Saisie de date au pave numerique, miroir de frigo/dates.py.
    ///
    /// Le Skorpio-G a un pave de 28 ou 38 touches : pas de '/' commode, pas de
    /// selecteur de date. On accepte donc la frappe la plus courte possible.
    ///
    ///     "2510"      25 octobre (prochaine occurrence)
    ///     "251026"    25/10/2026
    ///     "25102026"  25/10/2026
    ///     ""          date inconnue
    ///
    /// En mode relatif (touche J+), le nombre saisi est un nombre de jours :
    /// "7" devient la date d'aujourd'hui plus sept jours.
    /// </summary>
    internal static class Dates
    {
        /// <summary>
        /// Analyse une saisie. Retourne true si la saisie est exploitable ;
        /// result vaut DateTime.MinValue quand l'utilisateur laisse le champ vide.
        /// </summary>
        public static bool TryParse(string text, bool relative, out DateTime result,
                                    out string error)
        {
            result = DateTime.MinValue;
            error = null;
            string digits = OnlyDigits(text);

            if (digits.Length == 0)
                return true;                            // date volontairement inconnue

            DateTime today = DateTime.Now.Date;

            if (relative)
            {
                int days;
                if (!TryInt(digits, out days) || days < 0 || days > 3650)
                {
                    error = "nombre de jours invalide";
                    return false;
                }
                result = today.AddDays(days);
                return true;
            }

            int d, m, y;
            switch (digits.Length)
            {
                case 4:                                 // JJMM
                    if (!TryInt(digits.Substring(0, 2), out d) ||
                        !TryInt(digits.Substring(2, 2), out m))
                    {
                        error = "date illisible";
                        return false;
                    }
                    y = today.Year;
                    if (!Build(y, m, d, out result, out error))
                        return false;
                    if (result < today)                 // une DDM est dans le futur
                        return Build(y + 1, m, d, out result, out error);
                    return true;

                case 6:                                 // JJMMAA
                    if (!TryInt(digits.Substring(0, 2), out d) ||
                        !TryInt(digits.Substring(2, 2), out m) ||
                        !TryInt(digits.Substring(4, 2), out y))
                    {
                        error = "date illisible";
                        return false;
                    }
                    return Build(ExpandYear(y, today), m, d, out result, out error);

                case 8:                                 // JJMMAAAA
                    if (!TryInt(digits.Substring(0, 2), out d) ||
                        !TryInt(digits.Substring(2, 2), out m) ||
                        !TryInt(digits.Substring(4, 4), out y))
                    {
                        error = "date illisible";
                        return false;
                    }
                    return Build(y, m, d, out result, out error);

                default:
                    error = "attendu JJMM, JJMMAA ou JJMMAAAA";
                    return false;
            }
        }

        /// <summary>Complete une annee sur deux chiffres dans une fenetre -10/+89 ans.</summary>
        private static int ExpandYear(int yy, DateTime today)
        {
            int century = today.Year - (today.Year % 100);
            int year = century + yy;
            if (year < today.Year - 10)
                year += 100;
            return year;
        }

        private static bool Build(int y, int m, int d, out DateTime result, out string error)
        {
            result = DateTime.MinValue;
            error = null;
            if (m < 1 || m > 12)
            {
                error = "mois " + m + " invalide";
                return false;
            }
            if (y < 1970 || y > 2999)
            {
                error = "annee " + y + " invalide";
                return false;
            }
            if (d < 1 || d > DateTime.DaysInMonth(y, m))
            {
                error = "jour " + d + " invalide pour ce mois";
                return false;
            }
            result = new DateTime(y, m, d);
            return true;
        }

        public static string ToIso(DateTime value)
        {
            return value == DateTime.MinValue ? "" : value.ToString("yyyy-MM-dd");
        }

        public static string ToShort(DateTime value)
        {
            return value == DateTime.MinValue ? "sans date" : value.ToString("dd/MM/yy");
        }

        /// <summary>Jours restants, pour colorer l'ecran apres un scan.</summary>
        public static int DaysLeft(DateTime value)
        {
            if (value == DateTime.MinValue)
                return int.MaxValue;
            return (int)(value.Date - DateTime.Now.Date).TotalDays;
        }

        private static string OnlyDigits(string text)
        {
            if (text == null)
                return "";
            char[] buf = new char[text.Length];
            int n = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] >= '0' && text[i] <= '9')
                    buf[n++] = text[i];
            }
            return new string(buf, 0, n);
        }

        // int.TryParse existe sous CF 2.0 mais reste peu fiable selon les images :
        // une conversion explicite evite toute surprise a l'execution.
        private static bool TryInt(string text, out int value)
        {
            value = 0;
            try
            {
                value = int.Parse(text);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
