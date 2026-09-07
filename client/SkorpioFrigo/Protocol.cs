using System;
using System.Text;

namespace SkorpioFrigo
{
    /// <summary>
    /// Protocole de ligne, miroir exact de frigo/protocol.py cote serveur.
    /// Toute modification ici doit etre repercutee la-bas, et inversement.
    /// </summary>
    internal static class Protocol
    {
        public const string Version = "1";
        public const char Sep = '|';
        public const string LineEnd = "\r\n";
        public const char ChecksumMark = '*';

        /// <summary>XOR de tous les octets, sur deux chiffres hexadecimaux.</summary>
        public static string Checksum(string payload)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            int acc = 0;
            for (int i = 0; i < bytes.Length; i++)
                acc ^= bytes[i];
            return acc.ToString("X2");
        }

        /// <summary>Retire separateurs et caracteres de controle d'un champ.</summary>
        public static string Sanitize(string value)
        {
            if (value == null)
                return "";
            StringBuilder sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == Sep || c == ChecksumMark || c < ' ')
                    sb.Append(' ');
                else
                    sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        /// <summary>Construit une ligne complete, checksum incluse, sans CRLF.</summary>
        public static string Encode(params string[] parts)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                    sb.Append(Sep);
                sb.Append(i == 0 ? Sanitize(parts[i]).ToUpper() : Sanitize(parts[i]));
            }
            string payload = sb.ToString();
            return payload + Sep + ChecksumMark + Checksum(payload);
        }

        /// <summary>
        /// Decoupe une ligne recue et verifie la checksum si elle est presente.
        /// Retourne les champs, le verbe en position 0.
        /// </summary>
        public static string[] Decode(string line)
        {
            if (line == null)
                throw new FormatException("ligne vide");
            string text = line.Trim();
            if (text.Length == 0)
                throw new FormatException("ligne vide");

            string[] raw = text.Split(Sep);
            int count = raw.Length;
            if (count > 1 && raw[count - 1].Length > 0 && raw[count - 1][0] == ChecksumMark)
            {
                string received = raw[count - 1].Substring(1).ToUpper();
                count--;
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < count; i++)
                {
                    if (i > 0)
                        sb.Append(Sep);
                    sb.Append(raw[i]);
                }
                string expected = Checksum(sb.ToString());
                if (received != expected)
                    throw new FormatException("checksum " + received + " attendue " + expected);
            }

            string[] fields = new string[count];
            for (int i = 0; i < count; i++)
                fields[i] = raw[i].Trim();
            fields[0] = fields[0].ToUpper();
            return fields;
        }

        /// <summary>Champ a l'indice demande, chaine vide si absent.</summary>
        public static string Field(string[] fields, int index)
        {
            return (fields != null && index < fields.Length) ? fields[index] : "";
        }

        /// <summary>Horodatage ISO local, a la seconde (le terminal n'a pas de fuseau).</summary>
        public static string NowIso()
        {
            return DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss");
        }
    }
}
