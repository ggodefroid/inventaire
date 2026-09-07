using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SkorpioFrigo
{
    /// <summary>
    /// Tampon de scans sur le terminal : un fichier texte en ajout seul.
    ///
    /// Deux exigences dictent ce choix. D'abord la batterie : sur un terminal de
    /// 2008 elle lache sans prevenir, donc chaque scan est ecrit et vide sur le
    /// disque immediatement, jamais garde en memoire. Ensuite l'idempotence :
    /// chaque scan porte un numero de sequence croissant et definitif, ce qui
    /// permet de tout renvoyer sans risque apres un transfert interrompu.
    ///
    /// Les lignes stockees sont exactement celles du protocole. Le fichier peut
    /// donc etre copie tel quel sur la carte memoire et importe sur le PC avec
    /// `inventaire.py importer`, sans transformation.
    /// </summary>
    internal sealed class ScanBuffer
    {
        private const string BufferName = "tampon.txt";
        private const string StateName = "tampon.state";
        private const int CompactThreshold = 300;

        private readonly string _bufferPath;
        private readonly string _statePath;

        // Champs explicites plutot que proprietes auto-implementees : celles-ci
        // sont du C# 3.0, hors du langage que cible le Compact Framework 2.0.
        private int _lastSeq;
        private int _ackedSeq;

        public int LastSeq { get { return _lastSeq; } }
        public int AckedSeq { get { return _ackedSeq; } }

        public ScanBuffer()
        {
            _bufferPath = Settings.PathIn(BufferName);
            _statePath = Settings.PathIn(StateName);
            LoadState();
        }

        public string BufferPath { get { return _bufferPath; } }

        /// <summary>Nombre de scans pas encore confirmes par le serveur.</summary>
        public int PendingCount
        {
            get { return Math.Max(0, LastSeq - AckedSeq); }
        }

        // ------------------------------------------------------------- ecriture

        /// <summary>
        /// Enregistre un scan et retourne son numero de sequence.
        /// La ligne est sur le disque avant que la methode ne rende la main.
        /// </summary>
        public int Append(string barcode, string expiryIso, int qty,
                          string location, string note)
        {
            int seq = LastSeq + 1;
            string line = Protocol.Encode("SCAN",
                seq.ToString(),
                barcode,
                expiryIso == null ? "" : expiryIso,
                qty.ToString(),
                location,
                Protocol.NowIso(),
                note == null ? "" : note);

            AppendRaw(line);
            _lastSeq = seq;
            SaveState();
            return seq;
        }

        private void AppendRaw(string line)
        {
            byte[] data = Encoding.UTF8.GetBytes(line + Protocol.LineEnd);
            using (FileStream fs = new FileStream(_bufferPath, FileMode.Append,
                                                  FileAccess.Write, FileShare.Read))
            {
                fs.Write(data, 0, data.Length);
                fs.Flush();
            }
        }

        // ------------------------------------------------------------- lecture

        /// <summary>Lignes SCAN dont le numero depasse celui fourni.</summary>
        public List<string> Pending(int afterSeq)
        {
            List<string> result = new List<string>();
            if (!File.Exists(_bufferPath))
                return result;
            using (StreamReader r = new StreamReader(_bufferPath, Encoding.UTF8))
            {
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0)
                        continue;
                    int seq = SeqOf(line);
                    if (seq > afterSeq)
                        result.Add(line);
                }
            }
            return result;
        }

        /// <summary>Numero de sequence d'une ligne SCAN, -1 si ce n'en est pas une.</summary>
        private static int SeqOf(string line)
        {
            try
            {
                string[] f = Protocol.Decode(line);
                if (Protocol.Field(f, 0) != "SCAN")
                    return -1;
                return int.Parse(Protocol.Field(f, 1));
            }
            catch (Exception)
            {
                return -1;
            }
        }

        /// <summary>Les N derniers scans, pour l'affichage a l'ecran.</summary>
        public List<string> Tail(int count)
        {
            List<string> all = Pending(-1);
            List<string> tail = new List<string>();
            int start = Math.Max(0, all.Count - count);
            for (int i = start; i < all.Count; i++)
                tail.Add(all[i]);
            return tail;
        }

        // ---------------------------------------------------------------- etat

        /// <summary>Enregistre la confirmation du serveur jusqu'au numero donne.</summary>
        public void Ack(int seq)
        {
            if (seq > AckedSeq)
            {
                _ackedSeq = Math.Min(seq, _lastSeq);
                SaveState();
            }
            if (AckedSeq >= CompactThreshold)
                Compact();
        }

        /// <summary>
        /// Reecrit le tampon en ne gardant que les scans non confirmes.
        /// Les lignes confirmees partent dans envoyes.txt, jamais supprimees
        /// directement : en cas de doute on peut toujours les reimporter.
        /// </summary>
        public void Compact()
        {
            if (!File.Exists(_bufferPath))
                return;
            string keepPath = _bufferPath + ".tmp";
            string archivePath = Settings.PathIn("envoyes.txt");
            int kept = 0;

            using (StreamReader r = new StreamReader(_bufferPath, Encoding.UTF8))
            using (StreamWriter keep = new StreamWriter(keepPath, false, Encoding.UTF8))
            using (StreamWriter arch = new StreamWriter(archivePath, true, Encoding.UTF8))
            {
                keep.NewLine = Protocol.LineEnd;
                arch.NewLine = Protocol.LineEnd;
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    if (line.Trim().Length == 0)
                        continue;
                    int seq = SeqOf(line);
                    if (seq > AckedSeq)
                    {
                        keep.WriteLine(line);
                        kept++;
                    }
                    else
                    {
                        arch.WriteLine(line);
                    }
                }
            }

            // Remplacement en deux temps : File.Replace n'existe pas sous CF 2.0.
            string oldPath = _bufferPath + ".old";
            if (File.Exists(oldPath))
                File.Delete(oldPath);
            File.Move(_bufferPath, oldPath);
            File.Move(keepPath, _bufferPath);
            File.Delete(oldPath);
        }

        /// <summary>
        /// Copie le tampon sur la carte memoire : transport de secours qui ne
        /// depend d'aucun pilote PC. On sort la carte, on l'insere dans le PC.
        /// </summary>
        public string ExportTo(string directory, string deviceId)
        {
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException(directory + " introuvable");
            string name = "frigo-" + deviceId + "-"
                          + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt";
            string target = Path.Combine(directory, name);

            using (StreamWriter w = new StreamWriter(target, false, Encoding.UTF8))
            {
                w.NewLine = Protocol.LineEnd;
                w.WriteLine("# tampon exporte par " + deviceId + " le " + Protocol.NowIso());
                w.WriteLine(Protocol.Encode("HELLO", deviceId, Protocol.Version, Program.Version));
                foreach (string line in Pending(-1))
                    w.WriteLine(line);
            }
            return target;
        }

        /// <summary>Repertoires de cartes memoire presents sur le terminal.</summary>
        public static List<string> StorageCards()
        {
            List<string> found = new List<string>();
            string[] candidates = new string[] {
                "\\Storage Card", "\\Storage Card2", "\\SDMMC Disk", "\\SD Card",
                "\\CF Card", "\\Mounted Volume", "\\Hard Disk", "\\Temp"
            };
            foreach (string c in candidates)
            {
                try
                {
                    if (Directory.Exists(c))
                        found.Add(c);
                }
                catch (Exception) { }
            }
            return found;
        }

        private void LoadState()
        {
            _lastSeq = 0;
            _ackedSeq = 0;
            try
            {
                if (File.Exists(_statePath))
                {
                    using (StreamReader r = new StreamReader(_statePath, Encoding.UTF8))
                    {
                        _lastSeq = int.Parse((r.ReadLine() ?? "0").Trim());
                        _ackedSeq = int.Parse((r.ReadLine() ?? "0").Trim());
                    }
                }
            }
            catch (Exception)
            {
                _lastSeq = 0;
                _ackedSeq = 0;
            }

            // Le fichier d'etat peut avoir ete perdu alors que le tampon existe :
            // on se recale sur le plus grand numero reellement present.
            int maxSeq = 0;
            foreach (string line in Pending(-1))
            {
                int seq = SeqOf(line);
                if (seq > maxSeq)
                    maxSeq = seq;
            }
            if (maxSeq > _lastSeq)
            {
                _lastSeq = maxSeq;
                SaveState();
            }
        }

        private void SaveState()
        {
            try
            {
                using (StreamWriter w = new StreamWriter(_statePath, false, Encoding.UTF8))
                {
                    w.NewLine = Protocol.LineEnd;
                    w.WriteLine(LastSeq.ToString());
                    w.WriteLine(AckedSeq.ToString());
                }
            }
            catch (Exception) { }
        }
    }
}
