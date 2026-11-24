// WinDock - a small themed dialog that asks for one line of text.
using System;
using System.Drawing;
using System.Windows.Forms;

namespace WinDock
{
    public class InputDialog : Form
    {
        readonly TextBox box = new TextBox();
        readonly string prompt;
        readonly float scale;
        Palette pal;

        InputDialog(string title, string prompt, string initial)
        {
            this.prompt = prompt;
            Text = title;
            pal = Theme.Current;
            var c = Native.CursorPos();
            scale = Native.MonitorScale(Native.MonitorFromPoint(new POINT(c.X, c.Y), Native.MONITOR_DEFAULTTONEAREST));
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = true;
            TopMost = true;
            Icon = Glyphs.MakeIcon(32);
            ClientSize = new Size((int)(420 * scale), (int)(150 * scale));
            BackColor = pal.WindowBg;
            box.Text = initial ?? "";
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = pal.Field; box.ForeColor = pal.FieldText;
            box.Font = new Font(Theme.FontFamily, 13f * scale, GraphicsUnit.Pixel);
            box.Bounds = new Rectangle((int)(18 * scale), (int)(56 * scale), (int)(384 * scale), box.PreferredHeight);
            Controls.Add(box);
            var ok = MakeButton("Add", true); ok.DialogResult = DialogResult.OK;
            var cancel = MakeButton("Cancel", false); cancel.DialogResult = DialogResult.Cancel;
            ok.Bounds = new Rectangle((int)(312 * scale), (int)(104 * scale), (int)(90 * scale), (int)(30 * scale));
            cancel.Bounds = new Rectangle((int)(214 * scale), (int)(104 * scale), (int)(90 * scale), (int)(30 * scale));
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
            Shown += (s, e) => { box.Focus(); box.SelectionStart = box.Text.Length; };
            HandleCreated += (s, e) => Native.SetDwmInt(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, pal.Dark ? 1 : 0);
        }

        Button MakeButton(string text, bool primary)
        {
            var b = new Button { Text = text, FlatStyle = FlatStyle.Flat };
            b.Font = new Font(Theme.FontFamily, 12.5f * scale, GraphicsUnit.Pixel);
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = pal.Hairline;
            b.BackColor = primary ? pal.Accent : pal.CardBg;
            b.ForeColor = primary ? Color.White : pal.Text;
            return b;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Theme.HighQuality(e.Graphics);
            using (var f = Theme.MakeFont(13f * scale, FontStyle.Regular))
                Theme.DrawText(e.Graphics, prompt, f, pal.Text, new RectangleF(18 * scale, 14 * scale, 390 * scale, 34 * scale), StringAlignment.Near, StringAlignment.Center);
        }

        public static string Ask(string title, string prompt, string initial)
        {
            using (var d = new InputDialog(title, prompt, initial))
                return d.ShowDialog() == DialogResult.OK ? d.box.Text : null;
        }
    }
}
