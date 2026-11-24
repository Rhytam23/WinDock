// WinDock - the Settings window (SPEC 11.1): sidebar with search, account row and grouped pages;
// pages made of rounded cards with toggle / slider / picker / link / info rows. Everything is drawn here.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinDock
{
    public class SettingsWindow : Form
    {
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        readonly List<SettingsPage> pages;
        SettingsPage page;
        readonly List<string> history = new List<string>();
        int historyPos = -1;
        readonly Dictionary<string, float> scrollByPage = new Dictionary<string, float>();
        float scroll, contentHeight;
        float scale = 1f;
        Palette pal;
        readonly TextBox search = new TextBox();
        readonly Timer anim = new Timer { Interval = 15 };
        Point mouse = new Point(-1, -1);
        bool mouseDown;
        SliderRow dragSlider;
        float sidebarScroll, sidebarContentH;
        Account account;

        // hit areas (window coordinates)
        class Hit { public RectangleF Rect; public Action Click; public string Id; }
        readonly List<Hit> sidebarHits = new List<Hit>();
        RectangleF btnClose, btnMin, btnZoom, navBack, navFwd, searchRect;

        const float SidebarW = 190, TopBar = 52, LogicalW = 660, LogicalH = 640;

        public SettingsWindow(List<SettingsPage> pages)
        {
            this.pages = pages;
            Text = "WinDock Settings";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            KeyPreview = true;
            Icon = Glyphs.MakeIcon(64);
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            var c = Native.CursorPos();
            scale = Native.MonitorScale(Native.MonitorFromPoint(new POINT(c.X, c.Y), Native.MONITOR_DEFAULTTONEAREST));
            var wa = Screen.FromPoint(c).WorkingArea;
            int w = (int)(LogicalW * scale), h = (int)(LogicalH * scale);
            Bounds = new Rectangle(wa.Left + (wa.Width - w) / 2, wa.Top + (wa.Height - h) / 2, w, h);
            MinimumSize = new Size((int)(500 * scale), (int)(420 * scale));
            pal = Theme.Current;

            search.BorderStyle = BorderStyle.None;
            search.TextChanged += (s, e) => { sidebarScroll = 0; Invalidate(); };
            search.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { if (search.Text.Length > 0) search.Text = ""; else Close(); e.Handled = true; e.SuppressKeyPress = true; }
                if (e.KeyCode == Keys.Enter) { var first = SearchResults().FirstOrDefault(); if (first != null) first.Click(); e.Handled = true; e.SuppressKeyPress = true; }
            };
            Controls.Add(search);
            search.HandleCreated += (s, e) => SendMessage(search.Handle, 0x1501, new IntPtr(1), "Search");
            ApplyThemeToControls();
            anim.Tick += (s, e) => AnimTick();
            account = Account.Load(() => { if (!IsDisposed) BeginInvoke(new Action(Invalidate)); });
        }

        float U(float v) { return v * scale; }

        void ApplyThemeToControls()
        {
            pal = Theme.Current;
            search.BackColor = pal.Field;
            search.ForeColor = pal.FieldText;
            search.Font = new Font(Theme.FontFamily, 13f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            if (IsHandleCreated) Native.SetDwmInt(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, pal.Dark ? 1 : 0);
        }

        public void ThemeChanged() { ApplyThemeToControls(); Rebuild(); }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            var m = new MARGINS { Top = 1 };
            Native.DwmExtendFrameIntoClientArea(Handle, ref m);
            Native.SetDwmInt(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, 2);
            Native.SetDwmInt(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, pal.Dark ? 1 : 0);
            Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_FRAMECHANGED);
        }

        // ------------------------------------------------------------ window frame (no Windows title bar)

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
            {
                if (Native.IsZoomed(Handle))
                {
                    // when maximised the window overhangs the screen by the frame size; keep the content on screen
                    var r = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                    int fx = GetSystemMetrics(32) + GetSystemMetrics(92), fy = GetSystemMetrics(33) + GetSystemMetrics(92);
                    r.Left += fx; r.Right -= fx; r.Top += fy; r.Bottom -= fy;
                    Marshal.StructureToPtr(r, m.LParam, false);
                }
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == Native.WM_NCHITTEST)
            {
                var p = PointToClient(new Point((short)(m.LParam.ToInt64() & 0xffff), (short)((m.LParam.ToInt64() >> 16) & 0xffff)));
                m.Result = new IntPtr(HitTestFrame(p));
                return;
            }
            if (m.Msg == Native.WM_DPICHANGED)
            {
                scale = (m.WParam.ToInt32() & 0xffff) / 96f;
                var r = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                ApplyThemeToControls();
                Bounds = r.ToRectangle();
                MinimumSize = new Size((int)(500 * scale), (int)(420 * scale));
                Invalidate();
                return;
            }
            base.WndProc(ref m);
        }

        int HitTestFrame(Point p)
        {
            int edge = (int)U(6);
            if (!Native.IsZoomed(Handle))
            {
                bool l = p.X < edge, r = p.X >= Width - edge, t = p.Y < edge, b = p.Y >= Height - edge;
                if (t && l) return Native.HTTOPLEFT; if (t && r) return Native.HTTOPRIGHT;
                if (b && l) return Native.HTBOTTOMLEFT; if (b && r) return Native.HTBOTTOMRIGHT;
                if (l) return Native.HTLEFT; if (r) return Native.HTRIGHT; if (t) return Native.HTTOP; if (b) return Native.HTBOTTOM;
            }
            var pf = new PointF(p.X, p.Y);
            if (btnClose.Contains(pf) || btnMin.Contains(pf) || btnZoom.Contains(pf) || navBack.Contains(pf) || navFwd.Contains(pf) || searchRect.Contains(pf))
                return Native.HTCLIENT;
            if (p.X < U(SidebarW) && p.Y < U(40)) return Native.HTCAPTION;
            if (p.X >= U(SidebarW) && p.Y < U(TopBar)) return Native.HTCAPTION;
            return Native.HTCLIENT;
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && !search.Focused) { Close(); e.Handled = true; return; }
            if (e.KeyCode == Keys.F && e.Control) { search.Focus(); e.Handled = true; return; }
            if (e.KeyCode == Keys.Left && e.Alt) { Back(); e.Handled = true; return; }
            if (e.KeyCode == Keys.Right && e.Alt) { Forward(); e.Handled = true; return; }
            base.OnKeyDown(e);
        }

        // ------------------------------------------------------------ navigation

        public void Navigate(string id) { Navigate(id, true); }

        void Navigate(string id, bool record)
        {
            var p = pages.FirstOrDefault(x => x.Id == id) ?? pages[0];
            if (p.OpensUri != null) { Shellx.Open(p.OpensUri); return; }
            if (page != null) scrollByPage[page.Id] = scroll;
            page = p;
            p.Sections = p.Build();
            float s;
            scroll = scrollByPage.TryGetValue(p.Id, out s) ? s : 0;
            if (record)
            {
                if (historyPos < history.Count - 1) history.RemoveRange(historyPos + 1, history.Count - historyPos - 1);
                if (history.Count == 0 || history[history.Count - 1] != id) { history.Add(p.Id); }
                historyPos = history.Count - 1;
            }
            foreach (var sec in p.Sections) foreach (var r in sec.Rows) { var t = r as ToggleRow; if (t != null) t.Anim = -1; }
            Invalidate();
        }

        void Back() { if (historyPos > 0) { historyPos--; Navigate(history[historyPos], false); } }
        void Forward() { if (historyPos < history.Count - 1) { historyPos++; Navigate(history[historyPos], false); } }

        /// <summary>Rebuild the current page (values changed elsewhere, e.g. from the dock menu or the config file).</summary>
        public void Rebuild()
        {
            if (page == null) return;
            page.Sections = page.Build();
            Invalidate();
        }

        // ------------------------------------------------------------ painting

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.HighQuality(g);
            pal = Theme.Current;
            float sw = U(SidebarW);
            using (var b = new SolidBrush(pal.WindowBg)) g.FillRectangle(b, 0, 0, Width, Height);
            using (var b = new SolidBrush(pal.SidebarBg)) g.FillRectangle(b, 0, 0, sw, Height);
            using (var pen = new Pen(pal.Hairline)) g.DrawLine(pen, sw - 0.5f, 0, sw - 0.5f, Height);
            DrawContent(g, sw);
            DrawSidebar(g, sw);
        }

        void DrawTrafficLights(Graphics g)
        {
            float d = U(12), y = U(16), x = U(16), step = U(20);
            btnClose = new RectangleF(x, y, d, d); btnMin = new RectangleF(x + step, y, d, d); btnZoom = new RectangleF(x + step * 2, y, d, d);
            bool active = Form.ActiveForm == this;
            var group = RectangleF.Union(btnClose, btnZoom); group.Inflate(U(4), U(4));
            bool hot = group.Contains(mouse);
            Color[] cols = { Color.FromArgb(255, 95, 87), Color.FromArgb(254, 188, 46), Color.FromArgb(40, 200, 64) };
            RectangleF[] rs = { btnClose, btnMin, btnZoom };
            for (int i = 0; i < 3; i++)
            {
                Color c = active || hot ? cols[i] : (pal.Dark ? Color.FromArgb(80, 80, 84) : Color.FromArgb(205, 205, 208));
                using (var b = new SolidBrush(c)) g.FillEllipse(b, rs[i]);
                using (var pen = new Pen(Color.FromArgb(40, 0, 0, 0), 1)) g.DrawEllipse(pen, rs[i]);
                if (hot)
                {
                    var r = rs[i]; float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, k = r.Width * 0.24f;
                    using (var pen = new Pen(Color.FromArgb(150, 0, 0, 0), Math.Max(1.2f, U(1.3f))))
                    {
                        if (i == 0) { g.DrawLine(pen, cx - k, cy - k, cx + k, cy + k); g.DrawLine(pen, cx - k, cy + k, cx + k, cy - k); }
                        else if (i == 1) g.DrawLine(pen, cx - k * 1.2f, cy, cx + k * 1.2f, cy);
                        else
                        {
                            using (var b = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                            {
                                g.FillPolygon(b, new[] { new PointF(cx - k * 1.1f, cy - k * 1.1f), new PointF(cx + k * 0.5f, cy - k * 1.1f), new PointF(cx - k * 1.1f, cy + k * 0.5f) });
                                g.FillPolygon(b, new[] { new PointF(cx + k * 1.1f, cy + k * 1.1f), new PointF(cx - k * 0.5f, cy + k * 1.1f), new PointF(cx + k * 1.1f, cy - k * 0.5f) });
                            }
                        }
                    }
                }
            }
        }

        void DrawSidebar(Graphics g, float sw)
        {
            DrawTrafficLights(g);
            // search field
            searchRect = new RectangleF(U(12), U(42), sw - U(24), U(28));
            Theme.FillRounded(g, pal.Field, searchRect, U(7));
            Glyphs.Draw(g, "search", new RectangleF(searchRect.X + U(6), searchRect.Y + U(6), U(16), U(16)), pal.SubText);
            int th = search.PreferredHeight;
            var sb = new Rectangle((int)(searchRect.X + U(26)), (int)(searchRect.Y + (searchRect.Height - th) / 2), (int)(searchRect.Width - U(32)), th);
            if (search.Bounds != sb) search.Bounds = sb;

            float top = U(80);
            var region = new RectangleF(0, top, sw, Height - top);
            var oldClip = g.Clip;
            g.SetClip(region);
            sidebarHits.Clear();
            float y = top - sidebarScroll;
            using (var name = Theme.MakeFont(U(13), FontStyle.Bold))
            using (var sub = Theme.MakeFont(U(11), FontStyle.Regular))
            using (var item = Theme.MakeFont(U(13), FontStyle.Regular))
            using (var header = Theme.MakeFont(U(10.5f), FontStyle.Bold))
            {
                if (search.Text.Trim().Length > 0)
                {
                    var results = SearchResults();
                    Theme.DrawText(g, results.Count == 0 ? "No results" : "RESULTS", header, pal.SubText, new RectangleF(U(18), y, sw - U(30), U(22)), StringAlignment.Near, StringAlignment.Center);
                    y += U(24);
                    foreach (var r in results)
                    {
                        var rr = new RectangleF(U(8), y, sw - U(16), U(40));
                        if (rr.Contains(mouse)) Theme.FillRounded(g, pal.Hover, rr, U(7));
                        Theme.DrawText(g, r.Id, item, pal.Text, new RectangleF(rr.X + U(10), rr.Y + U(2), rr.Width - U(14), U(20)), StringAlignment.Near, StringAlignment.Center);
                        Theme.DrawText(g, r.Sub, sub, pal.SubText, new RectangleF(rr.X + U(10), rr.Y + U(20), rr.Width - U(14), U(16)), StringAlignment.Near, StringAlignment.Center);
                        sidebarHits.Add(new Hit { Rect = rr, Click = r.Click });
                        y += U(42);
                    }
                }
                else
                {
                    // account row
                    var ar = new RectangleF(U(8), y, sw - U(16), U(50));
                    float av = U(36);
                    var avr = new RectangleF(ar.X + U(6), ar.Y + (ar.Height - av) / 2, av, av);
                    if (account.Picture != null)
                    {
                        using (var path = new GraphicsPath()) { path.AddEllipse(avr); var st = g.Save(); g.SetClip(path, CombineMode.Intersect); g.DrawImage(account.Picture, avr); g.Restore(st); }
                    }
                    else
                    {
                        using (var lg = new LinearGradientBrush(avr, Color.FromArgb(160, 160, 170), Color.FromArgb(110, 110, 120), 90f)) g.FillEllipse(lg, avr);
                        using (var f = Theme.MakeFont(U(16), FontStyle.Bold)) Theme.DrawText(g, account.Initial, f, Color.White, avr, StringAlignment.Center, StringAlignment.Center);
                    }
                    Theme.DrawText(g, account.Name, name, pal.Text, new RectangleF(avr.Right + U(10), ar.Y + U(7), ar.Width - av - U(20), U(18)), StringAlignment.Near, StringAlignment.Center);
                    Theme.DrawText(g, "WinDock settings", sub, pal.SubText, new RectangleF(avr.Right + U(10), ar.Y + U(25), ar.Width - av - U(20), U(16)), StringAlignment.Near, StringAlignment.Center);
                    y += U(58);
                    string lastGroup = null;
                    foreach (var p in pages)
                    {
                        if (p.Group != lastGroup)
                        {
                            if (lastGroup != null)
                            {
                                using (var pen = new Pen(pal.Hairline)) g.DrawLine(pen, U(16), y + U(6), sw - U(16), y + U(6));
                                y += U(12);
                            }
                            Theme.DrawText(g, p.Group, header, pal.SubText, new RectangleF(U(18), y, sw - U(30), U(22)), StringAlignment.Near, StringAlignment.Center);
                            y += U(24);
                            lastGroup = p.Group;
                        }
                        var rr = new RectangleF(U(8), y, sw - U(16), U(30));
                        bool sel = page == p;
                        if (sel) Theme.FillRounded(g, pal.Accent, rr, U(7));
                        else if (rr.Contains(mouse)) Theme.FillRounded(g, pal.Hover, rr, U(7));
                        Glyphs.Tile(g, p.Glyph, p.Color, new RectangleF(rr.X + U(6), rr.Y + U(5), U(20), U(20)));
                        Theme.DrawText(g, p.Title, item, sel ? pal.AccentText : pal.Text, new RectangleF(rr.X + U(34), rr.Y, rr.Width - U(38), rr.Height), StringAlignment.Near, StringAlignment.Center);
                        if (p.OpensUri != null && !sel && rr.Contains(mouse))
                            Glyphs.Draw(g, "arrow-up-right", new RectangleF(rr.Right - U(18), rr.Y + U(9), U(12), U(12)), pal.SubText);
                        string id = p.Id;
                        sidebarHits.Add(new Hit { Rect = rr, Click = () => Navigate(id) });
                        y += U(32);
                    }
                }
            }
            sidebarContentH = y + sidebarScroll - top + U(10);
            g.Clip = oldClip;
        }

        class Result { public string Id, Sub; public Action Click; public RectangleF Rect; }

        List<Result> SearchResults()
        {
            string q = search.Text.Trim();
            var list = new List<Result>();
            if (q.Length == 0) return list;
            foreach (var p in pages)
            {
                if (p.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string id = p.Id;
                    list.Add(new Result { Id = p.Title, Sub = p.OpensUri != null ? "Windows Settings" : "Page", Click = () => { Navigate(id); } });
                }
                if (p.OpensUri != null) continue;
                foreach (var sec in p.Build())
                    foreach (var r in sec.Rows)
                    {
                        string text = (r.Label ?? "") + " " + (sec.Heading ?? "");
                        if (!string.IsNullOrEmpty(r.Label) && text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            string id = p.Id, label = r.Label;
                            list.Add(new Result { Id = r.Label, Sub = p.Title, Click = () => JumpTo(id, label) });
                        }
                    }
            }
            return list.Take(30).ToList();
        }

        void JumpTo(string pageId, string label)
        {
            Navigate(pageId);
            LayoutContent();
            foreach (var sec in page.Sections)
                foreach (var r in sec.Rows)
                    if (r.Label == label)
                    {
                        float viewH = Height - U(TopBar);
                        scroll = Math.Max(0, Math.Min(Math.Max(0, contentHeight - viewH), r.Bounds.Y - viewH / 3));
                        r.Flash = 1f;
                        anim.Start();
                        Invalidate();
                        return;
                    }
        }

        // ------------------------------------------------------------ content

        float ContentLeft { get { return U(SidebarW) + U(20); } }
        float ContentWidth { get { return Width - U(SidebarW) - U(40); } }

        /// <summary>Positions every row (content coordinates: y = 0 at the top of the scrolling area).</summary>
        void LayoutContent()
        {
            if (page == null) return;
            float y = U(6);
            float x = ContentLeft, w = ContentWidth;
            using (var g = CreateGraphics())
            using (var note = Theme.MakeFont(U(11.5f), FontStyle.Regular))
                foreach (var sec in page.Sections)
                {
                    if (!string.IsNullOrEmpty(sec.Heading)) y += U(28);
                    foreach (var r in sec.Rows) { r.Bounds = new RectangleF(x, y, w, U(r.Height)); y += U(r.Height); }
                    if (!string.IsNullOrEmpty(sec.Footnote))
                    {
                        var sz = g.MeasureString(sec.Footnote, note, (int)(w - U(16)));
                        y += U(6) + sz.Height + U(4);
                    }
                    y += U(18);
                }
            contentHeight = y + U(10);
        }

        void DrawContent(Graphics g, float sw)
        {
            if (page == null) return;
            LayoutContent();
            float viewTop = U(TopBar);
            float viewH = Height - viewTop;
            scroll = Math.Max(0, Math.Min(scroll, Math.Max(0, contentHeight - viewH)));
            // top bar
            navBack = new RectangleF(sw + U(14), U(14), U(24), U(24));
            navFwd = new RectangleF(sw + U(40), U(14), U(24), U(24));
            bool canBack = historyPos > 0, canFwd = historyPos < history.Count - 1;
            if (navBack.Contains(mouse) && canBack) Theme.FillRounded(g, pal.Hover, navBack, U(6));
            if (navFwd.Contains(mouse) && canFwd) Theme.FillRounded(g, pal.Hover, navFwd, U(6));
            Glyphs.Draw(g, "chevron-left", RectangleF.Inflate(navBack, -U(5), -U(5)), canBack ? pal.Text : Theme.WithAlpha(pal.SubText, 110));
            Glyphs.Draw(g, "chevron-right", RectangleF.Inflate(navFwd, -U(5), -U(5)), canFwd ? pal.Text : Theme.WithAlpha(pal.SubText, 110));
            using (var title = Theme.MakeFont(U(19), FontStyle.Bold))
                Theme.DrawText(g, page.Title, title, pal.Text, new RectangleF(sw + U(74), U(10), Width - sw - U(90), U(32)), StringAlignment.Near, StringAlignment.Center);
            if (scroll > 1) using (var pen = new Pen(pal.Hairline)) g.DrawLine(pen, sw, viewTop - 0.5f, Width, viewTop - 0.5f);

            var state = g.Save();
            g.SetClip(new RectangleF(sw + 1, viewTop, Width - sw - 1, viewH));
            g.TranslateTransform(0, viewTop - scroll);
            var m = new PointF(mouse.X, mouse.Y - viewTop + scroll);
            bool mouseInContent = mouse.Y >= viewTop;
            using (var heading = Theme.MakeFont(U(13), FontStyle.Bold))
            using (var label = Theme.MakeFont(U(13), FontStyle.Regular))
            using (var small = Theme.MakeFont(U(11), FontStyle.Regular))
            using (var note = Theme.MakeFont(U(11.5f), FontStyle.Regular))
            {
                foreach (var sec in page.Sections)
                {
                    if (sec.Rows.Count == 0) continue;
                    var first = sec.Rows[0].Bounds;
                    if (!string.IsNullOrEmpty(sec.Heading))
                        Theme.DrawText(g, sec.Heading, heading, pal.Text, new RectangleF(first.X + U(2), first.Y - U(26), first.Width, U(22)), StringAlignment.Near, StringAlignment.Center);
                    var last = sec.Rows[sec.Rows.Count - 1].Bounds;
                    var card = RectangleF.FromLTRB(first.Left, first.Top, first.Right, last.Bottom);
                    Theme.FillRounded(g, pal.CardBg, card, U(10));
                    if (!pal.Dark) Theme.StrokeRounded(g, Color.FromArgb(18, 0, 0, 0), 1, card, U(10));
                    for (int i = 0; i < sec.Rows.Count; i++)
                    {
                        var r = sec.Rows[i];
                        if (i > 0) using (var pen = new Pen(pal.Hairline)) g.DrawLine(pen, r.Bounds.X + U(14), r.Bounds.Y, r.Bounds.Right - U(14), r.Bounds.Y);
                        bool hot = mouseInContent && r.Bounds.Contains(m) && r.Interactive;
                        DrawRow(g, r, hot, label, small, m, i == 0, i == sec.Rows.Count - 1);
                    }
                    if (!string.IsNullOrEmpty(sec.Footnote))
                    {
                        using (var b = new SolidBrush(pal.SubText))
                            g.DrawString(sec.Footnote, note, b, new RectangleF(card.X + U(8), card.Bottom + U(6), card.Width - U(16), U(200)));
                    }
                }
            }
            g.Restore(state);
            // thin scroll indicator
            if (contentHeight > viewH + 1)
            {
                float trackH = viewH - U(8);
                float barH = Math.Max(U(30), trackH * viewH / contentHeight);
                float by = viewTop + U(4) + (trackH - barH) * (scroll / (contentHeight - viewH));
                Theme.FillRounded(g, Theme.WithAlpha(pal.SubText, 90), new RectangleF(Width - U(8), by, U(4), barH), U(2));
            }
        }

        void DrawRow(Graphics g, Row r, bool hot, Font label, Font small, PointF m, bool first, bool last)
        {
            var b = r.Bounds;
            if (r.Flash > 0)
                Theme.FillRounded(g, Theme.WithAlpha(pal.Accent, (int)(90 * r.Flash)), RectangleF.Inflate(b, -U(2), -U(2)), U(8));
            if (hot && (r is LinkRow || r is PickerRow))
                Theme.FillRounded(g, Theme.WithAlpha(pal.Hover, 255), RectangleF.Inflate(b, -U(3), -U(3)), U(7));
            float ctl = r is ToggleRow ? U(64) : (r is SliderRow ? Math.Min(U(200), b.Width * 0.45f) + U(24) : b.Width * 0.38f);
            var labelRect = new RectangleF(b.X + U(14), b.Y, b.Width - ctl - U(14), U(40));
            var custom = r as CustomRow;
            if (custom != null)
            {
                custom.Draw(g, b, b.Contains(m) ? (Point?)Point.Round(m) : null);
                return;
            }
            Theme.DrawText(g, r.Label, label, pal.Text, labelRect, StringAlignment.Near, StringAlignment.Center);
            float right = b.Right - U(14);
            float cy = b.Y + U(20);
            var tog = r as ToggleRow;
            if (tog != null)
            {
                bool on = tog.Get();
                float target = on ? 1 : 0;
                if (tog.Anim < 0) tog.Anim = target;
                float t = tog.Anim;
                var track = new RectangleF(right - U(38), cy - U(11), U(38), U(22));
                Theme.FillRounded(g, Theme.Blend(pal.ToggleOff, pal.Accent, t), track, U(11));
                float kx = track.X + U(2) + t * U(16);
                using (var sh = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillEllipse(sh, kx, track.Y + U(2.5f), U(18), U(18));
                using (var kb = new SolidBrush(pal.Knob)) g.FillEllipse(kb, kx, track.Y + U(2), U(18), U(18));
                return;
            }
            var sl = r as SliderRow;
            if (sl != null)
            {
                float w = Math.Min(U(200), b.Width * 0.45f);
                var track = new RectangleF(right - w, cy - U(2), w, U(4));
                sl.Track = track;
                float v = (sl.Get() - sl.Min) / (float)Math.Max(1, sl.Max - sl.Min);
                Theme.FillRounded(g, pal.ToggleOff, track, U(2));
                Theme.FillRounded(g, pal.Accent, new RectangleF(track.X, track.Y, track.Width * v, track.Height), U(2));
                float kx = track.X + track.Width * v;
                using (var sh = new SolidBrush(Color.FromArgb(50, 0, 0, 0))) g.FillEllipse(sh, kx - U(9), cy - U(8.5f), U(18), U(18));
                using (var kb = new SolidBrush(pal.Knob)) g.FillEllipse(kb, kx - U(9), cy - U(9), U(18), U(18));
                if (sl.MinLabel != null)
                {
                    Theme.DrawText(g, sl.MinLabel, small, pal.SubText, new RectangleF(track.X - U(4), cy + U(9), U(80), U(16)), StringAlignment.Near, StringAlignment.Center);
                    Theme.DrawText(g, sl.MaxLabel, small, pal.SubText, new RectangleF(track.Right - U(76), cy + U(9), U(80), U(16)), StringAlignment.Far, StringAlignment.Center);
                }
                return;
            }
            var pk = r as PickerRow;
            if (pk != null)
            {
                Glyphs.Draw(g, "updown", new RectangleF(right - U(12), cy - U(7), U(12), U(14)), pal.SubText);
                Theme.DrawText(g, pk.CurrentLabel, label, pal.SubText, new RectangleF(b.X + b.Width * 0.4f, b.Y, right - U(18) - (b.X + b.Width * 0.4f), U(40)), StringAlignment.Far, StringAlignment.Center);
                return;
            }
            var ln = r as LinkRow;
            if (ln != null)
            {
                Glyphs.Draw(g, "chevron-right", new RectangleF(right - U(10), cy - U(6), U(10), U(12)), pal.SubText);
                if (ln.Value != null)
                    Theme.DrawText(g, ln.Value(), label, pal.SubText, new RectangleF(b.X + b.Width * 0.45f, b.Y, right - U(18) - (b.X + b.Width * 0.45f), U(40)), StringAlignment.Far, StringAlignment.Center);
                return;
            }
            var info = r as InfoRow;
            if (info != null)
            {
                string v = info.Value();
                Theme.DrawText(g, v, label, pal.SubText, new RectangleF(b.X + b.Width * 0.38f, b.Y, right - (b.X + b.Width * 0.38f), U(40)), StringAlignment.Far, StringAlignment.Center);
            }
        }

        void AnimTick()
        {
            bool busy = false;
            if (page != null)
                foreach (var sec in page.Sections)
                    foreach (var r in sec.Rows)
                    {
                        var t = r as ToggleRow;
                        if (t != null && t.Anim >= 0)
                        {
                            float target = t.Get() ? 1 : 0;
                            if (Math.Abs(t.Anim - target) > 0.01f) { t.Anim += (target - t.Anim) * 0.35f; busy = true; }
                            else t.Anim = target;
                        }
                        if (r.Flash > 0) { r.Flash = Math.Max(0, r.Flash - 0.02f); busy = true; }
                    }
            Invalidate();
            if (!busy) anim.Stop();
        }

        // ------------------------------------------------------------ mouse

        protected override void OnMouseMove(MouseEventArgs e)
        {
            mouse = e.Location;
            if (dragSlider != null) { SetSliderFromMouse(dragSlider, e.X); }
            Invalidate();
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { mouse = new Point(-1, -1); Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (e.X < U(SidebarW))
            {
                float maxS = Math.Max(0, sidebarContentH - (Height - U(80)));
                sidebarScroll = Math.Max(0, Math.Min(maxS, sidebarScroll - e.Delta * scale * 0.5f));
            }
            else
            {
                float viewH = Height - U(TopBar);
                scroll = Math.Max(0, Math.Min(Math.Max(0, contentHeight - viewH), scroll - e.Delta * scale * 0.5f));
            }
            Invalidate();
        }

        Row RowAt(Point p)
        {
            if (page == null || p.Y < U(TopBar) || p.X < U(SidebarW)) return null;
            var m = new PointF(p.X, p.Y - U(TopBar) + scroll);
            foreach (var sec in page.Sections) foreach (var r in sec.Rows) if (r.Bounds.Contains(m)) return r;
            return null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            mouseDown = true;
            if (e.Button != MouseButtons.Left) return;
            if (!searchRect.Contains(e.Location) && search.Focused) ActiveControl = null;
            var r = RowAt(e.Location) as SliderRow;
            if (r != null)
            {
                var t = r.Track; t.Inflate(U(10), U(12));
                var m = new PointF(e.X, e.Y - U(TopBar) + scroll);
                if (t.Contains(m)) { dragSlider = r; Capture = true; SetSliderFromMouse(r, e.X); }
            }
        }

        void SetSliderFromMouse(SliderRow r, int x)
        {
            float v = (x - r.Track.X) / Math.Max(1f, r.Track.Width);
            v = Math.Max(0, Math.Min(1, v));
            int val = r.Min + (int)Math.Round(v * (r.Max - r.Min));
            if (val != r.Get()) { r.Set(val); Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            bool wasDown = mouseDown; mouseDown = false;
            if (dragSlider != null) { dragSlider = null; Capture = false; App.Cfg.Save(); return; }
            if (!wasDown || e.Button != MouseButtons.Left) return;
            var p = new PointF(e.X, e.Y);
            if (btnClose.Contains(p)) { Close(); return; }
            if (btnMin.Contains(p)) { WindowState = FormWindowState.Minimized; return; }
            if (btnZoom.Contains(p)) { WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; return; }
            if (navBack.Contains(p)) { Back(); return; }
            if (navFwd.Contains(p)) { Forward(); return; }
            if (e.X < U(SidebarW))
            {
                foreach (var h in sidebarHits) if (h.Rect.Contains(p) && h.Rect.Top >= U(78)) { h.Click(); Invalidate(); return; }
                return;
            }
            var row = RowAt(e.Location);
            if (row == null) return;
            var tog = row as ToggleRow;
            if (tog != null) { tog.Set(!tog.Get()); if (tog.Anim < 0) tog.Anim = tog.Get() ? 0 : 1; anim.Start(); Invalidate(); return; }
            var ln = row as LinkRow;
            if (ln != null) { ln.Do(); Rebuild(); return; }
            var pk = row as PickerRow;
            if (pk != null)
            {
                var menu = Menus.Create();
                string cur = pk.Get();
                for (int i = 0; i < pk.Values.Length; i++)
                {
                    string v = pk.Values[i];
                    Menus.Item(menu.Items, pk.Labels[i], () => { pk.Set(v); Rebuild(); }, true, v == cur);
                }
                var screenPt = PointToScreen(new Point((int)(row.Bounds.Right - U(150)), (int)(row.Bounds.Bottom - scroll + U(TopBar))));
                menu.Show(screenPt);
            }
        }
    }

    /// <summary>The Windows account shown in the Settings sidebar: name and picture (or an initial).</summary>
    public class Account
    {
        public string Name, Initial;
        public Image Picture;

        public static Account Load(Action updated)
        {
            var a = new Account { Name = Environment.UserName };
            a.Initial = a.Name.Length > 0 ? a.Name.Substring(0, 1).ToUpperInvariant() : "?";
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var sb = new System.Text.StringBuilder(256); int n = sb.Capacity;
                    string full = null;
                    if (Native.GetUserNameEx(3, sb, ref n) && sb.Length > 0) full = sb.ToString();
                    if (full == null)
                    {
                        using (var s = new System.Management.ManagementObjectSearcher("SELECT FullName FROM Win32_UserAccount WHERE Name='" + Environment.UserName.Replace("'", "''") + "' AND Domain='" + Environment.MachineName.Replace("'", "''") + "'"))
                            foreach (System.Management.ManagementObject o in s.Get())
                            {
                                var fn = o["FullName"] as string;
                                if (!string.IsNullOrWhiteSpace(fn)) full = fn;
                            }
                    }
                    if (!string.IsNullOrWhiteSpace(full)) { a.Name = full.Trim(); a.Initial = a.Name.Substring(0, 1).ToUpperInvariant(); }
                }
                catch { }
                try
                {
                    string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
                    using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AccountPicture\Users\" + sid))
                    {
                        if (k != null)
                            foreach (var name in new[] { "Image192", "Image240", "Image208", "Image96", "Image448" })
                            {
                                var path = k.GetValue(name) as string;
                                if (path != null && System.IO.File.Exists(path))
                                {
                                    using (var fs = System.IO.File.OpenRead(path)) using (var img = Image.FromStream(fs)) a.Picture = new Bitmap(img);
                                    break;
                                }
                            }
                    }
                }
                catch { }
                if (updated != null) updated();
            });
            return a;
        }
    }
}
