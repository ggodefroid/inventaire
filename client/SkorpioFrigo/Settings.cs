using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace SkorpioFrigo
{
    /// <summary>
    /// Reglages persistes dans frigo.ini, a cote de l'executable.
    ///
    /// Windows CE n'a pas de repertoire courant : tous les chemins sont calcules
    /// a partir de l'emplacement de l'assembly, sinon les fichiers finissent
    /// dans la racine du terminal.
    /// </summary>
    internal sealed class Settings
    {
        public string DeviceId = "SKORPIO1";
        public string Transport = "serie";      // "serie" ou "tcp"
        public string ComPort = "COM1:";
        public int Baud = 115200;
        public string Host = "192.168.1.10";
        public int Port = 9101;
        public string Location = "frigo";
        public bool AskExpiry = true;
        public int DefaultShelfLife = 0;        // jours proposes par defaut, 0 = aucun

        private static string _baseDir;

        /// <summary>Repertoire de l'executable : seul chemin fiable sous CE.</summary>
        public static string BaseDir
        {
            get
            {
                if (_baseDir == null)
                {
                    try
                    {
                        string module = Assembly.GetExecutingAssembly()
                            .GetModules()[0].FullyQualifiedName;
                        _baseDir = Path.GetDirectoryName(module);
                    }
                    catch (Exception)
                    {
                        _baseDir = "\\Program Files\\SkorpioFrigo";
                    }
                    if (_baseDir == null || _baseDir.Length == 0)
                        _baseDir = "\\";
                }
                return _baseDir;
            }
        }

        public static string PathIn(string fileName)
        {
            return Path.Combine(BaseDir, fileName);
        }

        private static string IniPath { get { return PathIn("frigo.ini"); } }

        public static Settings Load()
        {
            Settings s = new Settings();
            if (!File.Exists(IniPath))
                return s;
            Dictionary<string, string> kv = new Dictionary<string, string>();
            using (StreamReader r = new StreamReader(IniPath, Encoding.UTF8))
            {
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';' || line[0] == '[')
                        continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;
                    kv[line.Substring(0, eq).Trim().ToLower()] = line.Substring(eq + 1).Trim();
                }
            }
            s.DeviceId = Get(kv, "terminal", s.DeviceId);
            s.Transport = Get(kv, "transport", s.Transport).ToLower();
            s.ComPort = Get(kv, "port_com", s.ComPort);
            s.Baud = GetInt(kv, "bauds", s.Baud);
            s.Host = Get(kv, "hote", s.Host);
            s.Port = GetInt(kv, "port_tcp", s.Port);
            s.Location = Get(kv, "lieu", s.Location);
            s.AskExpiry = Get(kv, "demander_peremption", "1") != "0";
            s.DefaultShelfLife = GetInt(kv, "duree_par_defaut", s.DefaultShelfLife);
            return s;
        }

        public void Save()
        {
            using (StreamWriter w = new StreamWriter(IniPath, false, Encoding.UTF8))
            {
                w.WriteLine("# Reglages du client d'inventaire du frigo");
                w.WriteLine("# transport = serie (socle) ou tcp (Wi-Fi)");
                w.WriteLine("terminal = " + DeviceId);
                w.WriteLine("transport = " + Transport);
                w.WriteLine("port_com = " + ComPort);
                w.WriteLine("bauds = " + Baud);
                w.WriteLine("hote = " + Host);
                w.WriteLine("port_tcp = " + Port);
                w.WriteLine("lieu = " + Location);
                w.WriteLine("demander_peremption = " + (AskExpiry ? "1" : "0"));
                w.WriteLine("duree_par_defaut = " + DefaultShelfLife);
            }
        }

        private static string Get(Dictionary<string, string> kv, string key, string fallback)
        {
            string v;
            if (kv.TryGetValue(key, out v) && v.Length > 0)
                return v;
            return fallback;
        }

        private static int GetInt(Dictionary<string, string> kv, string key, int fallback)
        {
            string v;
            if (kv.TryGetValue(key, out v))
            {
                try { return int.Parse(v.Trim()); }
                catch (Exception) { }
            }
            return fallback;
        }
    }
}
