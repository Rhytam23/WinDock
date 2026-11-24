// WinDock - folder stacks (SPEC 2.4): a folder's contents as a Fan, a Grid or a List above its dock icon.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace WinDock
{
    public class StackPopup : LayeredForm
    {
        class Entry { public string Path, Name; public bool IsDir; public Bitmap Icon; public RectangleF Hit; }

        readonly DockWindow dock;
        public readonly DockItem Item;
        readonly Config cfg;
        readonly float scale;
        readonly List<Entry> entries = new List<Entry>();
        int total;
        int fanCount;
        string view;
        Point anchor; int iconPx;
        Rectangle bounds;
        int hover = -1;            // index into entries, -2 = "Open in Explorer"
        RectangleF openRect;
        float scroll;
        float contentH, viewH;
        readonly Timer leaveTimer = new Timer { Interval = 300 };
        bool closing;

        public StackPopup(DockWindow dock, DockItem item, Config cfg, float scale)
        {
            NoActivate = false;
            this.dock = dock; Item = item; this.cfg = cfg; this.scale = scale;
            KeyPreview = true;
            leaveTimer.Tick += (s, e) =>
            {
                // hover-opened stacks close when the cursor leaves both the stack and the icon
                if (cfg.GetBool("stacks-click", true)) return;
                var c = Native.CursorPos();
                if (bounds.Contains(c) || dock.IconRect(Item).Contains(c.X, c.Y)) return;
                Close();
            };
        }

        public void Open(Point anchorBottomCenter, int iconSize)
        {
            anchor = anchorBottomCenter; iconPx = iconSize;
            LoadEntries();
            view = Folders.View(Item.Target);
            if (view == "auto")
            {
                view = cfg.Get("stack-view", "fan");
                if (view == "fan" && total > 14) view = "grid"; // big folders read better as a grid unless chosen
            }
            if (view != "fan" && view != "grid" && view != "list") view = "fan";
            LayoutStack();
            Render();
            Activate();
            Native.SetForegroundWindow(Handle);
            leaveTimer.Start();
            int px = (int)(64 * scale);
            foreach (var e in entries)
            {
                var en = e;
                IconLoader.Request(() => Shell.GetImage(en.Path, px <= 48 ? 48 : (px <= 96 ? 96 : 128), false), bmp =>
                {
                    if (closing || IsDisposed) { if (bmp != null) bmp.Dispose(); return; }
                    en.Icon = bmp; Render();
                });
            }
        }

        void LoadEntries()
        {
            entries.Clear();
            try
            {
                var di = new DirectoryInfo(Item.Target);
                var all = di.EnumerateFileSystemInfos().Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 && !f.Name.StartsWith(".") && !f.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase));
                string sort = Folders.Sort(Item.Target);
                IEnumerable<FileSystemInfo> sorted;
                if (sort == "date") sorted = all.OrderByDescending(f => f.LastWriteTimeUtc);
                else if (sort == "kind") sorted = all.OrderBy(f => f is DirectoryInfo ? "" : f.Extension.ToLowerInvariant()).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase);
                else sorted = all.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase);
                var list = sorted.Take(400).ToList();
                total = list.Count;
                foreach (var f in list)
                    entries.Add(new Entry { Path = f.FullName, IsDir = f is DirectoryInfo, Name = f is DirectoryInfo ? f.Name : (Path.GetExtension(f.Name).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(f.Name) : f.Name) });
            }
            catch (Exception ex) { Log.Write("stack: " + ex.Message); }
        }

        float U(float v) { return v * scale; }

        void LayoutStack()
        {
            IntPtr hmon;
            var mi = Native.PrimaryMonitor(out hmon);
            var mon = mi.rcMonitor.ToRectangle();
            if (view == "fan")
            {
                float row = U(52);
                int fit = (int)((anchor.Y - mon.Top - U(60)) / row) - 1; // leave room for "Open in Explorer"
                int n = fanCount = Math.Max(1, Math.Min(entries.Count, Math.Min(14, fit)));
                int w = (int)U(420), h = (int)(row * (n + 1) + U(40));
                bounds = new Rectangle(anchor.X - (int)(w - U(80)), anchor.Y - h, w, h);
            }
            else if (view == "grid")
            {
                int cols = Math.Max(3, Math.Min(6, (int)Math.Ceiling(Math.Sqrt(Math.Max(1, entries.Count)))));
                float cell = U(92);
                int rows = (int)Math.Ceiling(entries.Count / (float)cols);
                contentH = rows * U(100);
                viewH = Math.Min(contentH, mon.Height * 0.62f);
                int w = (int)(cols * cell + U(36)), h = (int)(viewH + U(76) + U(12));
                bounds = new Rectangle(anchor.X - w / 2, anchor.Y - h, w, h);
            }
            else
            {
                float row = U(30);
                contentH = entries.Count * row;
                viewH = Math.Min(contentH, mon.Height * 0.62f);
                int w = (int)U(300), h = (int)(viewH + U(50) + U(12));
                bounds = new Rectangle(anchor.X - w / 2, anchor.Y - h, w, h);
            }
            if (bounds.Left < mon.Left + 4) bounds.X = mon.Left + 4;
            if (bounds.Right > mon.Right - 4) bounds.X = mon.Right - 4 - bounds.Width;
            if (bounds.Top < mon.Top + 4) { bounds.Height -= (mon.Top + 4 - bounds.Top); bounds.Y = mon.Top + 4; }
        }

        void Render()
        {
            if (closing || IsDisposed) return;
            Surface.Ensure(bounds.Width, bounds.Height);
            var pal = Theme.Current;
            using (var g = Surface.BeginDraw())
            {
                if (view == "fan") DrawFan(g, pal);
                else DrawPanel(g, pal);
            }
            Present(bounds.X, bounds.Y);
        }

        void DrawFan(Graphics g, Palette pal)
        {
            int n = Math.Min(entries.Count, fanCount);
            float row = U(52), icon = U(44);
            // the column rises from the dock icon and curves gently to the right
            float baseX = anchor.X - bounds.X, baseY = bounds.Height - U(6);
            using (var f = Theme.MakeFont(U(13), FontStyle.Regular))
            {
                for (int i = 0; i < entries.Count; i++) entries[i].Hit = RectangleF.Empty;
                for (int i = 0; i < n; i++)
                {
                    var e = entries[i];
                    float t = (i + 1) / (float)Math.Max(n, 8);
                    float cx = baseX + t * t * U(90);
                    float cy = baseY - row * (i + 0.5f) - U(4);
                    var ir = new RectangleF(cx - icon / 2, cy - icon / 2, icon, icon);
                    DrawEntryIcon(g, e, ir);
                    var sz = Theme.Measure(g, e.Name, f);
                    float lw = Math.Min(sz.Width + U(30), U(260));
                    var lr = new RectangleF(ir.Left - lw - U(10), cy - (sz.Height + U(8)) / 2, lw, sz.Height + U(8));
                    bool hot = hover == i;
                    Theme.FillRounded(g, hot ? pal.Accent : (pal.Dark ? Color.FromArgb(235, 44, 44, 48) : Color.FromArgb(240, 250, 250, 252)), lr, lr.Height / 2);
                    Theme.DrawText(g, e.Name, f, hot ? Color.White : pal.Text, RectangleF.Inflate(lr, -U(8), 0), StringAlignment.Center, StringAlignment.Center);
                    if (hot) using (var b = new SolidBrush(Color.FromArgb(50, 255, 255, 255))) g.FillEllipse(b, RectangleF.Inflate(ir, U(4), U(4)));
                    e.Hit = RectangleF.Union(ir, lr);
                }
                // "Open in Explorer" at the top
                float ty = baseY - row * (n + 0.5f) - U(4);
                float tn = (n + 1) / (float)Math.Max(n, 8); float tx = baseX + tn * tn * U(90);
                openRect = new RectangleF(tx - U(18), ty - U(18), U(36), U(36));
                Theme.FillRounded(g, hover == -2 ? pal.Accent : (pal.Dark ? Color.FromArgb(235, 60, 60, 64) : Color.FromArgb(240, 250, 250, 252)), openRect, U(18));
                Glyphs.Draw(g, "arrow-up-right", RectangleF.Inflate(openRect, -U(8), -U(8)), hover == -2 ? Color.White : pal.Text);
                string more = total > n ? (total - n) + " more in Explorer" : "Open in Explorer";
                var sz2 = Theme.Measure(g, more, f);
                var mr = new RectangleF(openRect.Left - sz2.Width - U(30), openRect.Top + U(4), sz2.Width + U(20), openRect.Height - U(8));
                Theme.FillRounded(g, pal.Dark ? Color.FromArgb(220, 44, 44, 48) : Color.FromArgb(230, 250, 250, 252), mr, mr.Height / 2);
                Theme.DrawText(g, more, f, pal.Text, mr, StringAlignment.Center, StringAlignment.Center);
                openRect = RectangleF.Union(openRect, mr);
                if (entries.Count == 0)
                {
                    var er = new RectangleF(baseX - U(60), baseY - row, U(120), U(30));
                    Theme.FillRounded(g, pal.Dark ? Color.FromArgb(235, 44, 44, 48) : Color.FromArgb(240, 250, 250, 252), er, U(15));
                    Theme.DrawText(g, "Empty folder", f, pal.SubText, er, StringAlignment.Center, StringAlignment.Center);
                }
            }
        }

        void DrawPanel(Graphics g, Palette pal)
        {
            float arrow = U(10);
            var panel = new RectangleF(0.5f, 0.5f, bounds.Width - 1, bounds.Height - arrow - 1);
            Color bg = pal.Dark ? Color.FromArgb(246, 38, 38, 42) : Color.FromArgb(248, 246, 246, 248);
            using (var path = Theme.Rounded(panel, U(14)))
            {
                // pointer towards the dock icon
                float ax = Math.Max(U(24), Math.Min(bounds.Width - U(24), anchor.X - bounds.X));
                path.AddPolygon(new[] { new PointF(ax - arrow, panel.Bottom - 1), new PointF(ax, panel.Bottom + arrow), new PointF(ax + arrow, panel.Bottom - 1) });
                using (var b = new SolidBrush(bg)) g.FillPath(b, path);
            }
            Theme.StrokeRounded(g, pal.Dark ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0), 1, panel, U(14));
            using (var title = Theme.MakeFont(U(15), FontStyle.Bold))
            using (var f = Theme.MakeFont(U(12.5f), FontStyle.Regular))
            {
                Theme.DrawText(g, Item.Label ?? "", title, pal.Text, new RectangleF(U(16), U(10), bounds.Width - U(170), U(26)), StringAlignment.Near, StringAlignment.Center);
                string open = "Open in Explorer";
                var osz = Theme.Measure(g, open, f);
                openRect = new RectangleF(bounds.Width - osz.Width - U(40), U(10), osz.Width + U(24), U(26));
                if (hover == -2) Theme.FillRounded(g, pal.Accent, openRect, U(13));
                Theme.DrawText(g, open + "  ›", f, hover == -2 ? Color.White : pal.Accent, openRect, StringAlignment.Center, StringAlignment.Center);
                float top = U(44);
                var clip = new RectangleF(0, top, bounds.Width, viewH + U(4));
                g.SetClip(clip);
                for (int i = 0; i < entries.Count; i++) entries[i].Hit = RectangleF.Empty;
                if (view == "grid")
                {
                    int cols = Math.Max(3, Math.Min(6, (int)Math.Ceiling(Math.Sqrt(Math.Max(1, entries.Count)))));
                    float cell = U(92), rowH = U(100), icon = U(54);
                    for (int i = 0; i < entries.Count; i++)
                    {
                        var e = entries[i];
                        float x = U(18) + (i % cols) * cell, y = top + (i / cols) * rowH - scroll;
                        if (y + rowH < top || y > top + viewH) continue;
                        var cellR = new RectangleF(x, y, cell, rowH);
                        if (hover == i) Theme.FillRounded(g, pal.Dark ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0), RectangleF.Inflate(cellR, -U(3), -U(3)), U(8));
                        DrawEntryIcon(g, e, new RectangleF(x + (cell - icon) / 2, y + U(6), icon, icon));
                        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter })
                        using (var b = new SolidBrush(pal.Text))
                            g.DrawString(e.Name, f, b, new RectangleF(x + U(4), y + icon + U(10), cell - U(8), U(32)), sf);
                        e.Hit = cellR;
                    }
                }
                else
                {
                    float rowH = U(30), icon = U(20);
                    for (int i = 0; i < entries.Count; i++)
                    {
                        var e = entries[i];
                        float y = top + i * rowH - scroll;
                        if (y + rowH < top || y > top + viewH) continue;
                        var rr = new RectangleF(U(8), y, bounds.Width - U(16), rowH);
                        if (hover == i) Theme.FillRounded(g, pal.Accent, rr, U(6));
                        DrawEntryIcon(g, e, new RectangleF(U(16), y + (rowH - icon) / 2, icon, icon));
                        Theme.DrawText(g, e.Name, f, hover == i ? Color.White : pal.Text, new RectangleF(U(44), y, bounds.Width - U(80), rowH), StringAlignment.Near, StringAlignment.Center);
                        if (e.IsDir) Glyphs.Draw(g, "chevron-right", new RectangleF(bounds.Width - U(34), y + U(8), U(14), U(14)), hover == i ? Color.White : pal.SubText);
                        e.Hit = rr;
                    }
                }
                g.ResetClip();
                if (entries.Count == 0) Theme.DrawText(g, "Empty folder", f, pal.SubText, new RectangleF(0, top, bounds.Width, U(40)), StringAlignment.Center, StringAlignment.Center);
            }
        }

        void DrawEntryIcon(Graphics g, Entry e, RectangleF r)
        {
            if (e.Icon != null)
            {
                float ar = e.Icon.Width / (float)Math.Max(1, e.Icon.Height);
                var dr = ar >= 1 ? new RectangleF(r.X, r.Y + (r.Height - r.Width / ar) / 2, r.Width, r.Width / ar) : new RectangleF(r.X + (r.Width - r.Height * ar) / 2, r.Y, r.Height * ar, r.Height);
                g.DrawImage(e.Icon, dr);
            }
            else if (e.IsDir) Glyphs.Folder(g, r, null, null);
            else Theme.FillRounded(g, Color.FromArgb(200, 230, 230, 235), RectangleF.Inflate(r, -r.Width * 0.15f, -r.Height * 0.05f), r.Width * 0.08f);
        }

        int HitTest(Point p)
        {
            if (openRect.Contains(p)) return -2;
            for (int i = 0; i < entries.Count; i++) if (entries[i].Hit.Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h != hover) { hover = h; Render(); }
        }

        protected override void OnMouseLeave(EventArgs e) { if (hover != -1) { hover = -1; Render(); } }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (view == "fan") return;
            float max = Math.Max(0, contentH - viewH);
            scroll = Math.Max(0, Math.Min(max, scroll - e.Delta * scale * 0.6f));
            hover = HitTest(e.Location);
            Render();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h == -2) { Shellx.Open(Item.Target); Close(); return; }
            if (h < 0) return;
            var en = entries[h];
            if (e.Button == MouseButtons.Right) { Shellx.ShowInExplorer(en.Path); Close(); return; }
            Shellx.Open(en.Path);
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Close();
            base.OnKeyDown(e);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            // clicking the same folder icon again closes the stack (the dock sees the click after we close)
            Close();
        }

        public void CloseNow() { Close(); }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            closing = true;
            leaveTimer.Stop();
            foreach (var en in entries) if (en.Icon != null) en.Icon.Dispose();
            base.OnFormClosed(e);
            dock.Wake();
        }
    }
}
