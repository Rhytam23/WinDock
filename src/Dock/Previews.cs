// WinDock - live window previews above a running app's dock icon (SPEC 2.3), using DWM thumbnails.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace WinDock
{
    public class PreviewPopup : Form
    {
        readonly DockWindow dock;
        public DockItem Owner_;
        readonly List<IntPtr> thumbs = new List<IntPtr>();
        readonly List<Rectangle> cells = new List<Rectangle>();
        readonly List<AppWindow> wins = new List<AppWindow>();
        readonly Timer hideTimer = new Timer { Interval = 250 };
        int hover = -1;
        float scale = 1;
        Palette pal;

        public PreviewPopup(DockWindow dock)
        {
            this.dock = dock;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            Text = "WinDock Previews";
            hideTimer.Tick += (s, e) =>
            {
                if (ContainsCursor()) return;
                var c = Native.CursorPos();
                if (Owner_ != null && dock.IconRect(Owner_).Contains(c.X, c.Y)) return;
                hideTimer.Stop();
                Hide();
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= (int)(Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST);
                return cp;
            }
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = new IntPtr(Native.MA_NOACTIVATE); return; }
            base.WndProc(ref m);
        }

        public bool ContainsCursor() { return Visible && Bounds.Contains(Native.CursorPos()); }

        public void HideSoon() { if (!hideTimer.Enabled) hideTimer.Start(); }

        public void ShowFor(DockItem item, Point anchorBottomCenter, float scale)
        {
            this.scale = scale;
            pal = Theme.Current;
            Clear();
            Owner_ = item;
            wins.AddRange(item.Windows.Take(8));
            if (wins.Count == 0) { Hide(); return; }
            int pad = (int)(10 * scale), titleH = (int)(24 * scale);
            int maxW = (int)(220 * scale), maxH = (int)(140 * scale);
            if (!IsHandleCreated) CreateHandle();
            BackColor = pal.Dark ? Color.FromArgb(36, 36, 40) : Color.FromArgb(244, 244, 246);
            var sizes = new List<Size>();
            foreach (var w in wins)
            {
                IntPtr th;
                if (Native.DwmRegisterThumbnail(Handle, w.Hwnd, out th) != 0) { th = IntPtr.Zero; }
                thumbs.Add(th);
                SIZE src = new SIZE(16, 10);
                if (th != IntPtr.Zero) Native.DwmQueryThumbnailSourceSize(th, out src);
                float f = Math.Min(maxW / (float)Math.Max(1, src.cx), maxH / (float)Math.Max(1, src.cy));
                f = Math.Min(f, 1f);
                sizes.Add(new Size(Math.Max(40, (int)(src.cx * f)), Math.Max(30, (int)(src.cy * f))));
            }
            int cellH = sizes.Max(s => s.Height) + titleH;
            int x = pad;
            foreach (var s in sizes)
            {
                int cw = Math.Max(s.Width, (int)(120 * scale));
                cells.Add(new Rectangle(x, pad, cw, cellH));
                x += cw + pad;
            }
            int width = x, height = cellH + pad * 2;
            IntPtr hmon;
            var mi = Native.PrimaryMonitor(out hmon);
            int left = Math.Max(mi.rcMonitor.Left + 4, Math.Min(mi.rcMonitor.Right - width - 4, anchorBottomCenter.X - width / 2));
            Bounds = new Rectangle(left, anchorBottomCenter.Y - height, width, height);
            for (int i = 0; i < wins.Count; i++)
            {
                if (thumbs[i] == IntPtr.Zero) continue;
                var c = cells[i];
                var s = sizes[i];
                var dest = new RECT(c.X + (c.Width - s.Width) / 2, c.Y + titleH, c.X + (c.Width - s.Width) / 2 + s.Width, c.Y + titleH + s.Height);
                var props = new DWM_THUMBNAIL_PROPERTIES
                {
                    dwFlags = Native.DWM_TNP_RECTDESTINATION | Native.DWM_TNP_VISIBLE | Native.DWM_TNP_OPACITY | Native.DWM_TNP_SOURCECLIENTAREAONLY,
                    rcDestination = dest, opacity = 255, fVisible = true, fSourceClientAreaOnly = false
                };
                Native.DwmUpdateThumbnailProperties(thumbs[i], ref props);
            }
            Native.SetDwmInt(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, 2);
            Native.SetDwmInt(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, pal.Dark ? 1 : 0);
            if (!Visible) Native.ShowWindow(Handle, Native.SW_SHOWNA);
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            Invalidate();
            hideTimer.Stop();
        }

        void Clear()
        {
            foreach (var t in thumbs) if (t != IntPtr.Zero) Native.DwmUnregisterThumbnail(t);
            thumbs.Clear(); cells.Clear(); wins.Clear(); hover = -1;
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) { Clear(); Owner_ = null; hideTimer.Stop(); dock.Wake(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (pal == null) return;
            var g = e.Graphics;
            Theme.HighQuality(g);
            using (var f = Theme.MakeFont(12f * scale, FontStyle.Regular))
                for (int i = 0; i < cells.Count; i++)
                {
                    var c = cells[i];
                    if (i == hover) Theme.FillRounded(g, pal.Dark ? Color.FromArgb(60, 60, 66) : Color.FromArgb(222, 222, 228), Rectangle.Inflate(c, (int)(5 * scale), (int)(5 * scale)), 8 * scale);
                    string title = wins[i].Title;
                    if (string.IsNullOrEmpty(title)) title = Owner_ != null ? Owner_.Label : "";
                    Theme.DrawText(g, title, f, pal.Text, new RectangleF(c.X, c.Y, c.Width, 20 * scale), StringAlignment.Center, StringAlignment.Center);
                    if (thumbs[i] == IntPtr.Zero)
                        Theme.DrawText(g, "(no preview)", f, pal.SubText, new RectangleF(c.X, c.Y + 24 * scale, c.Width, c.Height - 24 * scale), StringAlignment.Center, StringAlignment.Center);
                }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = cells.FindIndex(c => c.Contains(e.Location));
            if (h != hover) { hover = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); HideSoon(); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            int i = cells.FindIndex(c => c.Contains(e.Location));
            if (i < 0) return;
            var w = wins[i];
            var owner = Owner_;
            if (e.Button == MouseButtons.Middle) { Apps.Close(w.Hwnd); Hide(); return; }
            Hide();
            if (Native.IsIconic(w.Hwnd) && owner != null) Effects.Restore(w.Hwnd, dock.IconRect(owner), App.Cfg);
            else Apps.Activate(w.Hwnd);
        }
    }
}
