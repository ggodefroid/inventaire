using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SkorpioFrigo
{
    /// <summary>Ecran de reglages, saisissable entierement au pave numerique.</summary>
    internal sealed class SettingsForm : Form
    {
        private readonly Settings _s;
        private readonly Panel _rows = new Panel();
        private readonly Button _ok = new Button();
        private readonly Button _cancel = new Button();

        private readonly TextBox _device = new TextBox();
        private readonly ComboBox _transport = new ComboBox();
        private readonly TextBox _com = new TextBox();
        private readonly TextBox _baud = new TextBox();
        private readonly TextBox _host = new TextBox();
        private readonly TextBox _port = new TextBox();
        private readonly TextBox _location = new TextBox();
        private readonly CheckBox _askExpiry = new CheckBox();

        private readonly List<Label> _labels = new List<Label>();
        private readonly List<Control> _fields = new List<Control>();

        public SettingsForm(Settings s)
        {
            _s = s;
            Text = "Reglages";
            WindowState = FormWindowState.Maximized;

            // AutoScroll : en paysage (320x240) les huit lignes ne tiennent pas.
            // Presence confirmee dans les metadonnees du CF 2.0 et dans la
            // surface Windows CE generique (voir tools/verifier_api.py).
            _rows.AutoScroll = true;
            Controls.Add(_rows);

            _transport.Items.Add("serie");
            _transport.Items.Add("tcp");
            _transport.SelectedIndex = (s.Transport == "tcp") ? 1 : 0;

            _device.Text = s.DeviceId;
            _com.Text = s.ComPort;
            _baud.Text = s.Baud.ToString();
            _host.Text = s.Host;
            _port.Text = s.Port.ToString();
            _location.Text = s.Location;

            Add("Terminal", _device);
            Add("Transport", _transport);
            Add("Port COM", _com);
            Add("Bauds", _baud);
            Add("Hote PC", _host);
            Add("Port TCP", _port);
            Add("Lieu", _location);

            _askExpiry.Text = "Demander la peremption";
            _askExpiry.Checked = s.AskExpiry;
            _rows.Controls.Add(_askExpiry);

            _ok.Text = "Enregistrer";
            _ok.Click += new EventHandler(OnSave);
            Controls.Add(_ok);

            _cancel.Text = "Annuler";
            _cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(_cancel);
        }

        private void Add(string caption, Control field)
        {
            Label l = new Label();
            l.Text = caption;
            l.Font = new Font("Tahoma", 8f, FontStyle.Regular);
            _rows.Controls.Add(l);
            _labels.Add(l);

            field.Font = new Font("Tahoma", 8f, FontStyle.Regular);
            _rows.Controls.Add(field);
            _fields.Add(field);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            if (w <= 0 || h <= 0)
                return;

            _rows.Bounds = new Rectangle(0, 0, w, Math.Max(40, h - 30));
            int labelW = 72;
            int fieldW = Math.Max(60, w - labelW - 24);
            int y = 4;
            for (int i = 0; i < _fields.Count; i++)
            {
                _labels[i].Bounds = new Rectangle(4, y + 3, labelW, 18);
                _fields[i].Bounds = new Rectangle(labelW + 4, y, fieldW, 21);
                y += 25;
            }
            _askExpiry.Bounds = new Rectangle(4, y, Math.Max(80, w - 24), 20);

            int btnW = (w - 18) / 2;
            _ok.Bounds = new Rectangle(6, h - 28, btnW, 26);
            _cancel.Bounds = new Rectangle(12 + btnW, h - 28, btnW, 26);
        }

        private void OnSave(object sender, EventArgs e)
        {
            _s.DeviceId = _device.Text.Trim();
            _s.Transport = (_transport.SelectedIndex == 1) ? "tcp" : "serie";
            _s.ComPort = _com.Text.Trim();
            _s.Host = _host.Text.Trim();
            _s.Location = _location.Text.Trim();
            _s.AskExpiry = _askExpiry.Checked;
            try { _s.Baud = int.Parse(_baud.Text.Trim()); }
            catch (Exception) { }
            try { _s.Port = int.Parse(_port.Text.Trim()); }
            catch (Exception) { }

            if (_s.DeviceId.Length == 0)
                _s.DeviceId = "SKORPIO1";
            if (_s.Location.Length == 0)
                _s.Location = "frigo";
            if (_s.ComPort.Length == 0)
                _s.ComPort = "COM1:";

            try
            {
                _s.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Enregistrement impossible : " + ex.Message, "Reglages",
                                MessageBoxButtons.OK, MessageBoxIcon.None,
                                MessageBoxDefaultButton.Button1);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
