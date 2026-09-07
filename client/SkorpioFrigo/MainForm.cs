using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SkorpioFrigo
{
    /// <summary>
    /// Ecran unique du client, en deux temps : on scanne, on date.
    ///
    /// Le lecteur laser fonctionne en emulation clavier (le "wedge" Datalogic,
    /// actif par defaut) : la gachette tape le code-barres dans le champ qui a
    /// le focus, suivi d'un Entree. Aucun SDK Datalogic n'est donc necessaire,
    /// ce qui rend le programme portable sur toute la gamme.
    ///
    /// Tout est concu pour une main : le focus ne quitte jamais le champ de
    /// saisie, et la validation se fait a la touche Entree du pave.
    /// </summary>
    internal sealed class MainForm : Form
    {
        private enum Step { Scan, Expiry }

        private readonly Settings _settings;
        private readonly ScanBuffer _buffer;

        private Step _step = Step.Scan;
        private string _barcode = "";
        private string _lastLabel = "";
        private bool _relativeMode;
        private int _sessionCount;

        private readonly Label _lblDevice = new Label();
        private readonly Label _lblPending = new Label();
        private readonly Label _lblPrompt = new Label();
        private readonly TextBox _txtInput = new TextBox();
        private readonly Label _lblHint = new Label();
        private readonly Label _lblStatus = new Label();
        private readonly Button _btnValidate = new Button();
        private readonly Button _btnMode = new Button();

        private static readonly Color Green = Color.FromArgb(0, 110, 55);
        private static readonly Color Orange = Color.FromArgb(170, 100, 0);
        private static readonly Color Red = Color.FromArgb(180, 30, 25);
        private static readonly Color Grey = Color.FromArgb(100, 100, 100);

        public MainForm(Settings settings, ScanBuffer buffer)
        {
            _settings = settings;
            _buffer = buffer;

            Text = "Frigo";
            WindowState = FormWindowState.Maximized;
            KeyPreview = true;
            BackColor = Color.White;

            BuildControls();
            BuildMenu();
            EnterScanStep();
        }

        // ------------------------------------------------------------- interface

        private readonly Panel _header = new Panel();

        private void BuildControls()
        {
            _header.BackColor = Color.FromArgb(30, 60, 100);
            Controls.Add(_header);

            _lblDevice.Text = _settings.DeviceId;
            _lblDevice.ForeColor = Color.White;
            _lblDevice.Font = new Font("Tahoma", 8f, FontStyle.Bold);
            _header.Controls.Add(_lblDevice);

            _lblPending.ForeColor = Color.White;
            _lblPending.Font = new Font("Tahoma", 8f, FontStyle.Regular);
            // CF 2.0 ne connait que TopLeft / TopCenter / TopRight.
            _lblPending.TextAlign = ContentAlignment.TopRight;
            _header.Controls.Add(_lblPending);

            _lblPrompt.Font = new Font("Tahoma", 11f, FontStyle.Bold);
            _lblPrompt.TextAlign = ContentAlignment.TopCenter;
            Controls.Add(_lblPrompt);

            _txtInput.Font = new Font("Tahoma", 14f, FontStyle.Bold);
            _txtInput.TextAlign = HorizontalAlignment.Center;
            _txtInput.MaxLength = 32;
            _txtInput.KeyPress += new KeyPressEventHandler(OnInputKeyPress);
            _txtInput.TextChanged += new EventHandler(OnInputChanged);
            Controls.Add(_txtInput);

            _lblHint.Font = new Font("Tahoma", 8f, FontStyle.Regular);
            _lblHint.ForeColor = Grey;
            _lblHint.TextAlign = ContentAlignment.TopCenter;
            Controls.Add(_lblHint);

            _lblStatus.Font = new Font("Tahoma", 9f, FontStyle.Bold);
            _lblStatus.TextAlign = ContentAlignment.TopCenter;
            Controls.Add(_lblStatus);

            _btnValidate.Text = "Valider";
            _btnValidate.Click += delegate { Validate(); };
            Controls.Add(_btnValidate);

            _btnMode.Click += delegate { ToggleMode(); };
            Controls.Add(_btnMode);
        }

        /// <summary>
        /// Sous Windows CE la taille utile n'est connue qu'apres maximisation par
        /// le systeme : calculer les positions dans le constructeur donnerait la
        /// taille de conception, pas celle des 240x320 reels du Skorpio. On
        /// replace donc tout ici, ce qui gere aussi le passage portrait/paysage.
        /// </summary>
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            if (w <= 0 || h <= 0)
                return;

            _header.Bounds = new Rectangle(0, 0, w, 20);
            _lblDevice.Bounds = new Rectangle(4, 3, w / 2, 15);
            _lblPending.Bounds = new Rectangle(w / 2, 3, w / 2 - 4, 15);

            _lblPrompt.Bounds = new Rectangle(2, 28, w - 4, 22);
            _txtInput.Bounds = new Rectangle(8, 54, w - 16, 32);
            _lblHint.Bounds = new Rectangle(2, 90, w - 4, 32);

            int btnY = h - 32;
            int statusH = Math.Max(24, btnY - 128);
            _lblStatus.Bounds = new Rectangle(4, 126, w - 8, statusH);

            int btnW = (w - 24) / 2;
            _btnValidate.Bounds = new Rectangle(8, btnY, btnW, 26);
            _btnMode.Bounds = new Rectangle(16 + btnW, btnY, btnW, 26);
        }

        private void BuildMenu()
        {
            MainMenu menu = new MainMenu();

            MenuItem actions = new MenuItem();
            actions.Text = "Actions";
            actions.MenuItems.Add(Item("Envoyer au PC", new EventHandler(OnUpload)));
            actions.MenuItems.Add(Item("Exporter sur carte", new EventHandler(OnExport)));
            actions.MenuItems.Add(Item("Voir le tampon", new EventHandler(OnShowBuffer)));
            menu.MenuItems.Add(actions);

            MenuItem tools = new MenuItem();
            tools.Text = "Outils";
            tools.MenuItems.Add(Item("Reglages", new EventHandler(OnSettings)));
            tools.MenuItems.Add(Item("Diagnostic ports", new EventHandler(OnProbePorts)));
            tools.MenuItems.Add(Item("A propos", new EventHandler(OnAbout)));
            tools.MenuItems.Add(Item("Quitter", new EventHandler(OnQuit)));
            menu.MenuItems.Add(tools);

            Menu = menu;
        }

        private static MenuItem Item(string text, EventHandler handler)
        {
            MenuItem mi = new MenuItem();
            mi.Text = text;
            mi.Click += handler;
            return mi;
        }

        // ---------------------------------------------------------- etats de saisie

        private void EnterScanStep()
        {
            _step = Step.Scan;
            _barcode = "";
            _lblPrompt.Text = "SCANNEZ UN PRODUIT";
            _lblPrompt.ForeColor = Color.FromArgb(30, 60, 100);
            _lblHint.Text = "Appuyez sur la gachette du lecteur";
            _btnMode.Text = "Manuel";
            _txtInput.Text = "";
            RefreshCounters();
            FocusInput();
        }

        private void EnterExpiryStep()
        {
            _step = Step.Expiry;
            _lblPrompt.Text = _relativeMode ? "PEREMPTION : J + ?" : "PEREMPTION JJMMAA";
            _lblPrompt.ForeColor = Color.FromArgb(150, 80, 0);
            _lblHint.Text = _lastLabel.Length > 0
                ? _lastLabel + "\r\n" + _barcode
                : _barcode;
            _btnMode.Text = _relativeMode ? "-> JJMMAA" : "-> J+n";
            _txtInput.Text = "";
            if (_settings.DefaultShelfLife > 0 && _relativeMode)
                _txtInput.Text = _settings.DefaultShelfLife.ToString();
            FocusInput();
        }

        private void FocusInput()
        {
            try
            {
                _txtInput.Focus();
                _txtInput.SelectionStart = _txtInput.Text.Length;
            }
            catch (Exception) { }
        }

        private void ToggleMode()
        {
            if (_step == Step.Scan)
            {
                // Hors etape de date, le bouton sert a saisir un code a la main.
                _lblHint.Text = "Tapez le code-barres puis Entree";
                FocusInput();
                return;
            }
            _relativeMode = !_relativeMode;
            EnterExpiryStep();
        }

        private void OnInputKeyPress(object sender, KeyPressEventArgs e)
        {
            // Le wedge ajoute Entree (parfois Tab) apres le code-barres.
            if (e.KeyChar == '\r' || e.KeyChar == '\n' || e.KeyChar == '\t')
            {
                e.Handled = true;
                Validate();
            }
            else if (e.KeyChar == (char)27)              // Echap : on repart a zero
            {
                e.Handled = true;
                EnterScanStep();
            }
        }

        private void OnInputChanged(object sender, EventArgs e)
        {
            if (_step != Step.Expiry)
                return;
            // Apercu en direct : on voit tout de suite qu'on s'est trompe de frappe.
            DateTime parsed;
            string error;
            if (Dates.TryParse(_txtInput.Text, _relativeMode, out parsed, out error))
            {
                string preview = (parsed == DateTime.MinValue)
                    ? "aucune date"
                    : Dates.ToShort(parsed) + "   (J-" + Dates.DaysLeft(parsed) + ")";
                _lblHint.Text = (_lastLabel.Length > 0 ? _lastLabel + "\r\n" : "") + preview;
                _lblHint.ForeColor = Grey;
            }
            else
            {
                _lblHint.Text = error;
                _lblHint.ForeColor = Red;
            }
        }

        private void Validate()
        {
            if (_step == Step.Scan)
                ValidateBarcode();
            else
                ValidateExpiry();
        }

        private void ValidateBarcode()
        {
            string code = _txtInput.Text.Trim();
            if (code.Length == 0)
                return;
            if (code.Length < 4)
            {
                SetStatus("Code trop court, rescannez.", Red);
                _txtInput.Text = "";
                return;
            }
            _barcode = code;
            _lastLabel = "";

            if (!_settings.AskExpiry)
            {
                Store(DateTime.MinValue);
                return;
            }
            EnterExpiryStep();
        }

        private void ValidateExpiry()
        {
            DateTime parsed;
            string error;
            if (!Dates.TryParse(_txtInput.Text, _relativeMode, out parsed, out error))
            {
                SetStatus(error, Red);
                FocusInput();
                return;
            }
            Store(parsed);
        }

        private void Store(DateTime expiry)
        {
            try
            {
                int seq = _buffer.Append(_barcode, Dates.ToIso(expiry), 1,
                                         _settings.Location, "");
                _sessionCount++;
                SetStatus("#" + seq + " enregistre\r\n" + _barcode + "\r\n"
                          + Dates.ToShort(expiry),
                          expiry != DateTime.MinValue && Dates.DaysLeft(expiry) < 0
                              ? Orange : Green);
            }
            catch (Exception ex)
            {
                // Ecriture impossible : mieux vaut un message franc qu'un scan perdu.
                SetStatus("ECRITURE IMPOSSIBLE\r\n" + ex.Message, Red);
                MessageBox.Show("Le scan n'a pas pu etre enregistre : " + ex.Message,
                                "Erreur disque", MessageBoxButtons.OK,
                                MessageBoxIcon.None, MessageBoxDefaultButton.Button1);
            }
            EnterScanStep();
        }

        private void SetStatus(string text, Color color)
        {
            _lblStatus.Text = text;
            _lblStatus.ForeColor = color;
        }

        private void RefreshCounters()
        {
            _lblPending.Text = "tampon " + _buffer.PendingCount
                               + " | session " + _sessionCount;
        }

        // ------------------------------------------------------------- actions menu

        private void OnUpload(object sender, EventArgs e)
        {
            int pending = _buffer.PendingCount;
            if (pending == 0)
            {
                SetStatus("Rien a envoyer.", Grey);
                return;
            }

            string via = _settings.Transport == "tcp"
                ? _settings.Host + ":" + _settings.Port
                : _settings.ComPort;
            SetStatus("Envoi de " + pending + " scan(s)\r\nvia " + via + "...", Grey);
            Enabled = false;
            Application.DoEvents();

            UploadResult result;
            try
            {
                // Envoi synchrone volontaire : sur une liaison de socle le gain
                // d'un thread est nul, et la reprise apres coupure resterait
                // a gerer de toute facon. DoEvents suffit a garder l'ecran vivant.
                result = Uploader.Send(_settings, _buffer, new ProgressCallback(OnProgress));
            }
            finally
            {
                Enabled = true;
            }

            if (result.Success)
            {
                SetStatus("Transfert termine\r\n" + result.ToString()
                          + "\r\nreste " + _buffer.PendingCount + " en tampon",
                          result.Err > 0 ? Orange : Green);
            }
            else
            {
                SetStatus("Echec du transfert\r\n" + result.Error
                          + "\r\nAucun scan perdu.", Red);
            }
            RefreshCounters();
            FocusInput();
        }

        private void OnProgress(int done, int total, string reply)
        {
            _lblStatus.Text = "Envoi " + done + "/" + total + "\r\n" + reply;
            Application.DoEvents();
        }

        private void OnExport(object sender, EventArgs e)
        {
            List<string> cards = ScanBuffer.StorageCards();
            if (cards.Count == 0)
            {
                MessageBox.Show("Aucune carte memoire detectee.\r\n"
                                + "Inserez une carte SD ou CF, puis reessayez.",
                                "Export", MessageBoxButtons.OK, MessageBoxIcon.None,
                                MessageBoxDefaultButton.Button1);
                return;
            }
            try
            {
                string path = _buffer.ExportTo(cards[0], _settings.DeviceId);
                SetStatus("Exporte vers\r\n" + path, Green);
                MessageBox.Show("Tampon copie dans :\r\n" + path
                                + "\r\n\r\nSur le PC :\r\ninventaire.py importer <fichier>",
                                "Export", MessageBoxButtons.OK, MessageBoxIcon.None,
                                MessageBoxDefaultButton.Button1);
            }
            catch (Exception ex)
            {
                SetStatus("Export impossible : " + ex.Message, Red);
            }
            FocusInput();
        }

        private void OnShowBuffer(object sender, EventArgs e)
        {
            List<string> tail = _buffer.Tail(60);
            string body = "tampon : " + _buffer.PendingCount + " en attente"
                          + "\r\ndernier no : " + _buffer.LastSeq
                          + "\r\nconfirme jusqu'a : " + _buffer.AckedSeq
                          + "\r\nfichier : " + _buffer.BufferPath
                          + "\r\n\r\n";
            foreach (string line in tail)
                body += line + "\r\n";
            using (TextForm f = new TextForm("Tampon", body))
                f.ShowDialog();
            FocusInput();
        }

        private void OnSettings(object sender, EventArgs e)
        {
            using (SettingsForm f = new SettingsForm(_settings))
            {
                if (f.ShowDialog() == DialogResult.OK)
                {
                    _lblDevice.Text = _settings.DeviceId;
                    SetStatus("Reglages enregistres.", Green);
                }
            }
            EnterScanStep();
        }

        private void OnProbePorts(object sender, EventArgs e)
        {
            SetStatus("Test des ports COM1 a COM9...", Grey);
            Application.DoEvents();
            string report;
            try
            {
                report = SerialLinkCE.Probe(_settings.Baud);
            }
            catch (Exception ex)
            {
                report = "echec du test : " + ex.Message;
            }
            string body = "Ports serie du terminal\r\n"
                        + "(un port OUVRABLE est un candidat\r\n"
                        + " pour le reglage Port COM)\r\n\r\n" + report;
            using (TextForm f = new TextForm("Diagnostic ports", body))
                f.ShowDialog();
            EnterScanStep();
        }

        private void OnAbout(object sender, EventArgs e)
        {
            MessageBox.Show("Inventaire du frigo " + Program.Version
                            + "\r\nprotocole " + Protocol.Version
                            + "\r\n\r\nDossier :\r\n" + Settings.BaseDir,
                            "A propos", MessageBoxButtons.OK, MessageBoxIcon.None,
                            MessageBoxDefaultButton.Button1);
            FocusInput();
        }

        private void OnQuit(object sender, EventArgs e)
        {
            if (_buffer.PendingCount > 0)
            {
                MessageBox.Show("Attention : " + _buffer.PendingCount
                                + " scan(s) ne sont pas encore envoyes au PC.\r\n"
                                + "Ils sont conserves sur le terminal.",
                                "Quitter", MessageBoxButtons.OK, MessageBoxIcon.None,
                                MessageBoxDefaultButton.Button1);
            }
            Close();
        }
    }
}
