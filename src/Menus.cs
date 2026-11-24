// WinDock - context menus in WinDock's own look (rounded, dark or light).
using System;
using System.Drawing;
using System.Windows.Forms;

namespace WinDock
{
    public static class Menus
    {
        class Renderer : ToolStripRenderer
        {
            readonly Palette p;
            public Renderer(Palette p) { this.p = p; }

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using (var b = new SolidBrush(p.MenuBg)) e.Graphics.FillRectangle(b, e.AffectedBounds);
            }
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                var r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
                using (var pen = new Pen(p.MenuBorder)) e.Graphics.DrawRectangle(pen, r);
            }
            protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected || !e.Item.Enabled) return;
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var r = new RectangleF(4, 1, e.Item.Width - 8, e.Item.Height - 2);
                Theme.FillRounded(g, p.MenuHover, r, 5);
            }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                Color c = !e.Item.Enabled ? p.MenuSub : (e.Item.Selected ? Color.White : p.MenuText);
                var r = e.TextRectangle;
                TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, r, c, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                int y = e.Item.Height / 2;
                using (var pen = new Pen(p.Hairline)) e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
            }
            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var r = e.ImageRectangle;
                Color c = e.Item.Selected ? Color.White : p.MenuText;
                using (var pen = new Pen(c, 1.8f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                    g.DrawLines(pen, new[] { new PointF(r.X + r.Width * 0.2f, r.Y + r.Height * 0.52f), new PointF(r.X + r.Width * 0.42f, r.Y + r.Height * 0.74f), new PointF(r.X + r.Width * 0.8f, r.Y + r.Height * 0.28f) });
            }
            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                e.ArrowColor = e.Item.Selected ? Color.White : p.MenuText;
                base.OnRenderArrow(e);
            }
        }

        public static ContextMenuStrip Create()
        {
            var p = Theme.Current;
            var m = new ContextMenuStrip();
            m.Renderer = new Renderer(p);
            m.ShowImageMargin = true;
            m.ShowCheckMargin = false;
            m.BackColor = p.MenuBg;
            m.ForeColor = p.MenuText;
            m.Font = new Font(Theme.FontFamily, 9.5f);
            m.Padding = new Padding(0, 4, 0, 4);
            m.HandleCreated += (s, e) =>
            {
                Native.SetDwmInt(m.Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, 2);
                Native.SetDwmInt(m.Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, p.Dark ? 1 : 0);
            };
            return m;
        }

        public static ToolStripMenuItem Item(ToolStrip menu, string text, Action action)
        {
            return Item(menu.Items, text, action, true, false);
        }

        public static ToolStripMenuItem Item(ToolStripItemCollection items, string text, Action action, bool enabled, bool check)
        {
            var it = new ToolStripMenuItem(text);
            it.Enabled = enabled;
            it.Checked = check;
            it.Padding = new Padding(0, 2, 0, 2);
            if (action != null) it.Click += (s, e) => { try { action(); } catch (Exception ex) { Log.Write("menu: " + ex); } };
            items.Add(it);
            return it;
        }

        public static void Sep(ToolStrip menu) { menu.Items.Add(new ToolStripSeparator()); }

        public static ToolStripMenuItem Sub(ToolStrip menu, string text)
        {
            var it = new ToolStripMenuItem(text);
            it.Padding = new Padding(0, 2, 0, 2);
            menu.Items.Add(it);
            var p = Theme.Current;
            it.DropDown.BackColor = p.MenuBg;
            it.DropDown.ForeColor = p.MenuText;
            it.DropDown.Renderer = menu.Renderer;
            it.DropDown.HandleCreated += (s, e) => Native.SetDwmInt(it.DropDown.Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, 2);
            return it;
        }

        /// <summary>Shows the menu so that its bottom edge sits at `anchor` (above the dock), centred horizontally.</summary>
        public static void ShowAbove(ContextMenuStrip m, Point anchor, Action closed)
        {
            m.Closed += (s, e) => { if (closed != null) closed(); };
            Size sz = m.GetPreferredSize(Size.Empty);
            var pt = new Point(anchor.X - sz.Width / 2, anchor.Y - sz.Height);
            m.Show(pt);
            // Make WinDock the foreground app so clicking anywhere else closes the menu.
            Native.SetForegroundWindow(m.Handle);
        }

        public static void ShowAt(ContextMenuStrip m, Point pt, Action closed)
        {
            m.Closed += (s, e) => { if (closed != null) closed(); };
            m.Show(pt);
            Native.SetForegroundWindow(m.Handle);
        }
    }
}
