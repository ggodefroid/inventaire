using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SkorpioFrigo
{
    /// <summary>Lien bidirectionnel en lignes de texte vers le serveur PC.</summary>
    internal interface ILink : IDisposable
    {
        void WriteLine(string line);
        /// <summary>Lit une ligne. Retourne null si le delai expire.</summary>
        string ReadLine(int timeoutMs);
    }

    /// <summary>
    /// Liaison serie : socle RS-232, ou port USB du terminal.
    ///
    /// Sous Windows CE les noms de port portent un deux-points ("COM1:").
    /// On ne s'appuie pas sur SerialPort.ReadLine, dont le comportement varie
    /// selon les implementations du Compact Framework : la lecture est faite
    /// octet par octet avec une echeance explicite.
    /// </summary>
    internal sealed class SerialLinkCE : ILink
    {
        private SerialPort _port;

        public SerialLinkCE(string portName, int baud)
        {
            _port = new SerialPort(portName, baud, Parity.None, 8, StopBits.One);
            _port.Handshake = Handshake.None;
            _port.ReadTimeout = 200;
            _port.WriteTimeout = 5000;
            _port.Open();
            try { _port.DiscardInBuffer(); }
            catch (Exception) { }
        }

        public void WriteLine(string line)
        {
            byte[] data = Encoding.ASCII.GetBytes(line + Protocol.LineEnd);
            _port.Write(data, 0, data.Length);
        }

        public string ReadLine(int timeoutMs)
        {
            StringBuilder sb = new StringBuilder();
            DateTime deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < deadline)
            {
                int b;
                try { b = _port.ReadByte(); }
                catch (TimeoutException) { continue; }
                if (b < 0)
                    continue;
                if (b == '\n')
                    return sb.ToString().Trim();
                if (b != '\r')
                    sb.Append((char)b);
                if (sb.Length > 2048)      // flux non protocolaire : on abandonne
                    return sb.ToString();
            }
            return sb.Length > 0 ? sb.ToString().Trim() : null;
        }

        public void Dispose()
        {
            try { if (_port != null && _port.IsOpen) _port.Close(); }
            catch (Exception) { }
            _port = null;
        }

        /// <summary>
        /// Essaie d'ouvrir COM1: a COM9: et rapporte le resultat.
        /// Indispensable ici : le nom du port USB client varie selon la
        /// configuration "PC Connection" du terminal.
        /// </summary>
        public static string Probe(int baud)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 1; i <= 9; i++)
            {
                string name = "COM" + i + ":";
                try
                {
                    using (SerialPort p = new SerialPort(name, baud, Parity.None, 8, StopBits.One))
                    {
                        p.Open();
                        sb.Append(name + " OUVRABLE" + Protocol.LineEnd);
                    }
                }
                catch (Exception ex)
                {
                    string msg = ex.Message;
                    if (msg.Length > 28)
                        msg = msg.Substring(0, 28);
                    sb.Append(name + " - " + msg + Protocol.LineEnd);
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>Liaison TCP, si le Wi-Fi 802.11b/g du terminal est operationnel.</summary>
    internal sealed class TcpLinkCE : ILink
    {
        private Socket _sock;
        private readonly List<byte> _pending = new List<byte>();

        public TcpLinkCE(string host, int port, int connectTimeoutMs)
        {
            IPAddress addr = Resolve(host);
            _sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            // Socket.SendTimeout n'existe pas sous CF 2.0 : on passe par l'option
            // de socket. La lecture, elle, est bornee par Poll dans ReadLine.
            _sock.SetSocketOption(SocketOptionLevel.Socket,
                                  SocketOptionName.SendTimeout, connectTimeoutMs);
            _sock.Connect(new IPEndPoint(addr, port));
        }

        private static IPAddress Resolve(string host)
        {
            try
            {
                return IPAddress.Parse(host);
            }
            catch (FormatException)
            {
                IPHostEntry entry = Dns.GetHostEntry(host);
                if (entry.AddressList.Length == 0)
                    throw new SocketException();
                return entry.AddressList[0];
            }
        }

        public void WriteLine(string line)
        {
            byte[] data = Encoding.ASCII.GetBytes(line + Protocol.LineEnd);
            int sent = 0;
            while (sent < data.Length)
                sent += _sock.Send(data, sent, data.Length - sent, SocketFlags.None);
        }

        public string ReadLine(int timeoutMs)
        {
            byte[] chunk = new byte[256];
            DateTime deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            while (true)
            {
                int nl = _pending.IndexOf((byte)'\n');
                if (nl >= 0)
                {
                    StringBuilder sb = new StringBuilder(nl);
                    for (int i = 0; i < nl; i++)
                        sb.Append((char)_pending[i]);
                    _pending.RemoveRange(0, nl + 1);
                    return sb.ToString().Trim();
                }
                int remaining = (int)(deadline - DateTime.Now).TotalMilliseconds;
                if (remaining <= 0)
                    return null;
                // Poll est disponible partout, contrairement a ReceiveTimeout.
                if (!_sock.Poll(Math.Min(remaining, 500) * 1000, SelectMode.SelectRead))
                    continue;
                int n = _sock.Receive(chunk, 0, chunk.Length, SocketFlags.None);
                if (n <= 0)
                    return null;                       // connexion fermee
                for (int i = 0; i < n; i++)
                    _pending.Add(chunk[i]);
            }
        }

        public void Dispose()
        {
            try
            {
                if (_sock != null)
                {
                    try { _sock.Shutdown(SocketShutdown.Both); }
                    catch (Exception) { }
                    _sock.Close();
                }
            }
            catch (Exception) { }
            _sock = null;
        }
    }

    internal sealed class UploadResult
    {
        public int Sent;
        public int Ok;
        public int Dup;
        public int Err;
        public string ServerInfo = "";
        public string Error;                            // null = succes

        public bool Success { get { return Error == null; } }

        public override string ToString()
        {
            if (!Success)
                return "ECHEC : " + Error;
            return Ok + " envoye(s), " + Dup + " deja connu(s)"
                   + (Err > 0 ? ", " + Err + " refuse(s)" : "");
        }
    }

    internal delegate void ProgressCallback(int done, int total, string reply);

    /// <summary>
    /// Transfert du tampon vers le PC.
    ///
    /// La sequence est volontairement synchrone et une ligne a la fois : sur une
    /// liaison serie de socle, un pipeline apporterait un gain negligeable et
    /// rendrait la reprise apres coupure beaucoup plus fragile. Rien n'est
    /// efface du tampon avant que le serveur ait confirme.
    /// </summary>
    internal static class Uploader
    {
        private const int ReplyTimeoutMs = 6000;
        private const int HelloTimeoutMs = 8000;

        public static ILink Open(Settings s)
        {
            if (s.Transport == "tcp")
                return new TcpLinkCE(s.Host, s.Port, 8000);
            return new SerialLinkCE(s.ComPort, s.Baud);
        }

        public static UploadResult Send(Settings s, ScanBuffer buffer, ProgressCallback progress)
        {
            UploadResult result = new UploadResult();
            ILink link = null;
            try
            {
                link = Open(s);

                link.WriteLine(Protocol.Encode("HELLO", s.DeviceId,
                                               Protocol.Version, Program.Version));
                string hello = link.ReadLine(HelloTimeoutMs);
                if (hello == null)
                {
                    result.Error = "pas de reponse du PC. Le serveur tourne-t-il ?";
                    return result;
                }

                string[] ready = Protocol.Decode(hello);
                if (Protocol.Field(ready, 0) == "ERR")
                {
                    result.Error = Protocol.Field(ready, 2);
                    return result;
                }
                if (Protocol.Field(ready, 0) != "READY")
                {
                    result.Error = "reponse inattendue : " + hello;
                    return result;
                }
                result.ServerInfo = "serveur " + Protocol.Field(ready, 2);

                // Le serveur annonce le dernier numero qu'il connait : on repart
                // de la, ce qui evite de reemettre un tampon deja ingere.
                int watermark = buffer.AckedSeq;
                try
                {
                    int serverSeq = int.Parse(Protocol.Field(ready, 4));
                    if (serverSeq > watermark)
                        watermark = serverSeq;
                }
                catch (Exception) { }

                List<string> lines = buffer.Pending(watermark);
                result.Sent = lines.Count;
                int delivered = watermark;

                for (int i = 0; i < lines.Count; i++)
                {
                    link.WriteLine(lines[i]);
                    string reply = link.ReadLine(ReplyTimeoutMs);
                    if (reply == null)
                    {
                        result.Error = "PC muet apres " + i + " scan(s). Tampon conserve.";
                        break;
                    }

                    string[] f;
                    try { f = Protocol.Decode(reply); }
                    catch (FormatException) { result.Err++; continue; }

                    string verb = Protocol.Field(f, 0);
                    if (verb == "OK")
                        result.Ok++;
                    else if (verb == "DUP")
                        result.Dup++;
                    else
                        result.Err++;

                    // OK comme DUP valent remise : dans les deux cas le PC a la donnee.
                    if (verb == "OK" || verb == "DUP")
                    {
                        try
                        {
                            int seq = int.Parse(Protocol.Field(f, 1));
                            if (seq > delivered)
                                delivered = seq;
                        }
                        catch (Exception) { }
                    }

                    if (progress != null)
                        progress(i + 1, lines.Count, reply);
                }

                if (delivered > buffer.AckedSeq)
                    buffer.Ack(delivered);

                try
                {
                    link.WriteLine(Protocol.Encode("BYE", result.Ok.ToString()));
                    link.ReadLine(2000);
                }
                catch (Exception) { }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }
            finally
            {
                if (link != null)
                    link.Dispose();
            }
            return result;
        }

        /// <summary>Demande au PC le libelle d'un code, pour l'afficher a l'ecran.</summary>
        public static string Lookup(Settings s, string barcode)
        {
            ILink link = null;
            try
            {
                link = Open(s);
                link.WriteLine(Protocol.Encode("LOOK", barcode));
                string reply = link.ReadLine(4000);
                if (reply == null)
                    return null;
                string[] f = Protocol.Decode(reply);
                if (Protocol.Field(f, 0) != "INFO")
                    return null;
                return Protocol.Field(f, 2);
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (link != null)
                    link.Dispose();
            }
        }
    }
}
