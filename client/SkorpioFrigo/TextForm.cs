using System;
using System.Drawing;
using System.Windows.Forms;

namespace SkorpioFrigo
{
    /// <summary>
    /// Afficheur de texte defilant, reutilise pour le contenu du tampon et pour
    /// le diagnostic des ports. Une MessageBox ne convient pas : sur un ecran de
    /// 240x320 elle tronque tout et ne defile pas.
    /// </summary>
    internal sealed class TextForm : Form
    {
        private readonly TextBox _box = new TextBox();
        private readonly Button _close = new Button();

        public TextForm(string title, string body)
        {
            Text = title;
            WindowState = FormWindowState.Maximized;

            _box.Multiline = true;
            _box.ScrollBars = ScrollBars.Vertical;
            _box.ReadOnly = true;
            // Pas de retour a la ligne : les lignes de diagnostic sont larges et
            // plus lisibles avec un defilement horizontal qu'enroulees.
            _box.WordWrap = false;
            _box.Font = new Font("Courier New", 7f, FontStyle.Regular);
            _box.Text = body;
            Controls.Add(_box);

            _close.Text = "Fermer";
            _close.Click += delegate { Close(); };
            Controls.Add(_close);
        }

        // La taille reelle n'est connue qu'apres maximisation par le systeme :
        // positionner depuis le constructeur donnerait la taille de conception.
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            if (w <= 0 || h <= 0)
                return;
            _box.Bounds = new Rectangle(0, 0, w, Math.Max(20, h - 28));
            _close.Bounds = new Rectangle(Math.Max(0, w - 76), h - 26, 74, 24);
        }
    }
}
