// WinDock - every icon WinDock shows of its own is drawn here in code (SPEC 0.2: own assets only).
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace WinDock
{
    public static class Glyphs
    {
        /// <summary>A white line glyph inside r. Names are WinDock's own.</summary>
        public static void Draw(Graphics g, string name, RectangleF r, Color c)
        {
            float s = Math.Min(r.Width, r.Height);
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            float w = Math.Max(1f, s * 0.09f);
            using (var pen = new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var brush = new SolidBrush(c))
            {
                switch (name)
                {
                    case "gear":
                        {
                            int teeth = 8;
                            for (int i = 0; i < teeth; i++)
                            {
                                double a = i * Math.PI * 2 / teeth;
                                g.DrawLine(pen, cx + (float)Math.Cos(a) * s * 0.22f, cy + (float)Math.Sin(a) * s * 0.22f,
                                                cx + (float)Math.Cos(a) * s * 0.38f, cy + (float)Math.Sin(a) * s * 0.38f);
                            }
                            g.DrawEllipse(pen, cx - s * 0.25f, cy - s * 0.25f, s * 0.5f, s * 0.5f);
                            g.FillEllipse(brush, cx - s * 0.08f, cy - s * 0.08f, s * 0.16f, s * 0.16f);
                            break;
                        }
                    case "appearance":
                        g.DrawEllipse(pen, cx - s * 0.32f, cy - s * 0.32f, s * 0.64f, s * 0.64f);
                        g.FillPie(brush, cx - s * 0.32f, cy - s * 0.32f, s * 0.64f, s * 0.64f, 90, 180);
                        break;
                    case "dock":
                        g.DrawRectangle(pen, cx - s * 0.34f, cy - s * 0.3f, s * 0.68f, s * 0.6f);
                        g.FillRectangle(brush, cx - s * 0.26f, cy + s * 0.08f, s * 0.52f, s * 0.12f);
                        break;
                    case "menubar":
                        g.DrawRectangle(pen, cx - s * 0.34f, cy - s * 0.3f, s * 0.68f, s * 0.6f);
                        g.FillRectangle(brush, cx - s * 0.34f, cy - s * 0.3f, s * 0.68f, s * 0.14f);
                        g.FillEllipse(brush, cx - s * 0.12f, cy - s * 0.27f, s * 0.24f, s * 0.08f);
                        break;
                    case "grid":
                        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++)
                                g.FillRectangle(brush, cx - s * 0.3f + i * s * 0.22f, cy - s * 0.3f + j * s * 0.22f, s * 0.15f, s * 0.15f);
                        break;
                    case "star":
                        {
                            var pts = new PointF[10];
                            for (int i = 0; i < 10; i++)
                            {
                                double a = -Math.PI / 2 + i * Math.PI / 5;
                                float rad = (i % 2 == 0) ? s * 0.38f : s * 0.16f;
                                pts[i] = new PointF(cx + (float)Math.Cos(a) * rad, cy + (float)Math.Sin(a) * rad + s * 0.03f);
                            }
                            g.FillPolygon(brush, pts);
                            break;
                        }
                    case "buttons":
                        for (int i = 0; i < 3; i++) g.FillEllipse(brush, cx - s * 0.36f + i * s * 0.26f, cy - s * 0.09f, s * 0.2f, s * 0.2f);
                        break;
                    case "display":
                        g.DrawRectangle(pen, cx - s * 0.34f, cy - s * 0.28f, s * 0.68f, s * 0.44f);
                        g.DrawLine(pen, cx, cy + s * 0.16f, cx, cy + s * 0.3f);
                        g.DrawLine(pen, cx - s * 0.16f, cy + s * 0.3f, cx + s * 0.16f, cy + s * 0.3f);
                        break;
                    case "wifi":
                        for (int i = 1; i <= 3; i++)
                        {
                            float rr = s * 0.13f * i;
                            g.DrawArc(pen, cx - rr, cy + s * 0.2f - rr, rr * 2, rr * 2, 225, 90);
                        }
                        g.FillEllipse(brush, cx - s * 0.05f, cy + s * 0.15f, s * 0.1f, s * 0.1f);
                        break;
                    case "battery":
                        g.DrawRectangle(pen, cx - s * 0.34f, cy - s * 0.17f, s * 0.6f, s * 0.34f);
                        g.FillRectangle(brush, cx - s * 0.28f, cy - s * 0.11f, s * 0.32f, s * 0.22f);
                        g.FillRectangle(brush, cx + s * 0.28f, cy - s * 0.07f, s * 0.06f, s * 0.14f);
                        break;
                    case "brush":
                        g.DrawLine(pen, cx + s * 0.28f, cy - s * 0.3f, cx - s * 0.05f, cy + s * 0.05f);
                        g.FillEllipse(brush, cx - s * 0.32f, cy + s * 0.0f, s * 0.3f, s * 0.3f);
                        break;
                    case "bell":
                        {
                            var p = new GraphicsPath();
                            p.AddArc(cx - s * 0.22f, cy - s * 0.32f, s * 0.44f, s * 0.44f, 180, 180);
                            p.AddLine(cx + s * 0.22f, cy - s * 0.1f, cx + s * 0.3f, cy + s * 0.18f);
                            p.AddLine(cx - s * 0.3f, cy + s * 0.18f, cx - s * 0.22f, cy - s * 0.1f);
                            p.CloseFigure();
                            g.FillPath(brush, p);
                            g.FillEllipse(brush, cx - s * 0.08f, cy + s * 0.2f, s * 0.16f, s * 0.12f);
                            p.Dispose();
                            break;
                        }
                    case "hand":
                    case "lock":
                        g.DrawArc(pen, cx - s * 0.16f, cy - s * 0.34f, s * 0.32f, s * 0.36f, 180, 180);
                        g.DrawLine(pen, cx - s * 0.16f, cy - s * 0.16f, cx - s * 0.16f, cy - s * 0.04f);
                        g.DrawLine(pen, cx + s * 0.16f, cy - s * 0.16f, cx + s * 0.16f, cy - s * 0.04f);
                        g.FillRectangle(brush, cx - s * 0.27f, cy - s * 0.04f, s * 0.54f, s * 0.38f);
                        break;
                    case "apps":
                        for (int i = 0; i < 2; i++) for (int j = 0; j < 2; j++)
                                g.FillRectangle(brush, cx - s * 0.3f + i * s * 0.33f, cy - s * 0.3f + j * s * 0.33f, s * 0.26f, s * 0.26f);
                        break;
                    case "person":
                        g.FillEllipse(brush, cx - s * 0.14f, cy - s * 0.34f, s * 0.28f, s * 0.28f);
                        g.FillPie(brush, cx - s * 0.3f, cy - s * 0.02f, s * 0.6f, s * 0.6f, 180, 180);
                        break;
                    case "clock":
                        g.DrawEllipse(pen, cx - s * 0.32f, cy - s * 0.32f, s * 0.64f, s * 0.64f);
                        g.DrawLine(pen, cx, cy, cx, cy - s * 0.2f);
                        g.DrawLine(pen, cx, cy, cx + s * 0.14f, cy + s * 0.08f);
                        break;
                    case "update":
                        g.DrawArc(pen, cx - s * 0.28f, cy - s * 0.28f, s * 0.56f, s * 0.56f, 200, 250);
                        g.FillPolygon(brush, new[] { new PointF(cx - s * 0.36f, cy - s * 0.14f), new PointF(cx - s * 0.12f, cy - s * 0.14f), new PointF(cx - s * 0.26f, cy + s * 0.06f) });
                        break;
                    case "folder":
                        {
                            var p = new GraphicsPath();
                            p.AddLines(new[] {
                                new PointF(cx - s*0.36f, cy - s*0.24f), new PointF(cx - s*0.1f, cy - s*0.24f),
                                new PointF(cx - s*0.02f, cy - s*0.16f), new PointF(cx + s*0.36f, cy - s*0.16f),
                                new PointF(cx + s*0.36f, cy + s*0.28f), new PointF(cx - s*0.36f, cy + s*0.28f) });
                            p.CloseFigure();
                            g.FillPath(brush, p);
                            p.Dispose();
                            break;
                        }
                    case "info":
                        g.FillEllipse(brush, cx - s * 0.06f, cy - s * 0.3f, s * 0.12f, s * 0.12f);
                        g.FillRectangle(brush, cx - s * 0.055f, cy - s * 0.1f, s * 0.11f, s * 0.4f);
                        break;
                    case "swipe":
                        for (int i = 0; i < 4; i++) g.DrawLine(pen, cx - s * 0.27f + i * s * 0.18f, cy + s * 0.25f, cx - s * 0.27f + i * s * 0.18f, cy - s * 0.15f);
                        g.DrawLine(pen, cx - s * 0.2f, cy - s * 0.3f, cx + s * 0.2f, cy - s * 0.3f);
                        break;
                    case "keyboard":
                        g.DrawRectangle(pen, cx - s * 0.36f, cy - s * 0.2f, s * 0.72f, s * 0.4f);
                        g.DrawLine(pen, cx - s * 0.2f, cy + s * 0.08f, cx + s * 0.2f, cy + s * 0.08f);
                        break;
                    case "search":
                        g.DrawEllipse(pen, cx - s * 0.3f, cy - s * 0.3f, s * 0.42f, s * 0.42f);
                        g.DrawLine(pen, cx + s * 0.06f, cy + s * 0.06f, cx + s * 0.3f, cy + s * 0.3f);
                        break;
                    case "chevron-left":
                        g.DrawLines(pen, new[] { new PointF(cx + s * 0.12f, cy - s * 0.25f), new PointF(cx - s * 0.12f, cy), new PointF(cx + s * 0.12f, cy + s * 0.25f) });
                        break;
                    case "chevron-right":
                        g.DrawLines(pen, new[] { new PointF(cx - s * 0.12f, cy - s * 0.25f), new PointF(cx + s * 0.12f, cy), new PointF(cx - s * 0.12f, cy + s * 0.25f) });
                        break;
                    case "updown":
                        g.DrawLines(pen, new[] { new PointF(cx - s * 0.18f, cy - s * 0.08f), new PointF(cx, cy - s * 0.28f), new PointF(cx + s * 0.18f, cy - s * 0.08f) });
                        g.DrawLines(pen, new[] { new PointF(cx - s * 0.18f, cy + s * 0.08f), new PointF(cx, cy + s * 0.28f), new PointF(cx + s * 0.18f, cy + s * 0.08f) });
                        break;
                    case "arrow-up-right":
                        g.DrawLine(pen, cx - s * 0.2f, cy + s * 0.2f, cx + s * 0.2f, cy - s * 0.2f);
                        g.DrawLines(pen, new[] { new PointF(cx - s * 0.05f, cy - s * 0.2f), new PointF(cx + s * 0.2f, cy - s * 0.2f), new PointF(cx + s * 0.2f, cy + s * 0.05f) });
                        break;
                    case "download":
                        g.DrawLine(pen, cx, cy - s * 0.32f, cx, cy + s * 0.12f);
                        g.DrawLines(pen, new[] { new PointF(cx - s * 0.18f, cy - s * 0.04f), new PointF(cx, cy + s * 0.14f), new PointF(cx + s * 0.18f, cy - s * 0.04f) });
                        g.DrawLine(pen, cx - s * 0.3f, cy + s * 0.32f, cx + s * 0.3f, cy + s * 0.32f);
                        break;
                    case "doc":
                        g.DrawRectangle(pen, cx - s * 0.22f, cy - s * 0.32f, s * 0.44f, s * 0.64f);
                        for (int i = 0; i < 3; i++) g.DrawLine(pen, cx - s * 0.12f, cy - s * 0.14f + i * s * 0.14f, cx + s * 0.12f, cy - s * 0.14f + i * s * 0.14f);
                        break;
                    case "picture":
                        g.DrawRectangle(pen, cx - s * 0.32f, cy - s * 0.24f, s * 0.64f, s * 0.48f);
                        g.FillPolygon(brush, new[] { new PointF(cx - s * 0.28f, cy + s * 0.2f), new PointF(cx - s * 0.08f, cy - s * 0.04f), new PointF(cx + s * 0.06f, cy + s * 0.1f), new PointF(cx + s * 0.14f, cy + s * 0.02f), new PointF(cx + s * 0.28f, cy + s * 0.2f) });
                        break;
                    case "music":
                        g.FillEllipse(brush, cx - s * 0.26f, cy + s * 0.08f, s * 0.2f, s * 0.16f);
                        g.FillEllipse(brush, cx + s * 0.06f, cy + s * 0.02f, s * 0.2f, s * 0.16f);
                        g.DrawLines(pen, new[] { new PointF(cx - s * 0.07f, cy + s * 0.14f), new PointF(cx - s * 0.07f, cy - s * 0.26f), new PointF(cx + s * 0.25f, cy - s * 0.32f), new PointF(cx + s * 0.25f, cy + s * 0.08f) });
                        break;
                    case "video":
                        g.DrawRectangle(pen, cx - s * 0.32f, cy - s * 0.22f, s * 0.64f, s * 0.44f);
                        g.FillPolygon(brush, new[] { new PointF(cx - s * 0.08f, cy - s * 0.12f), new PointF(cx + s * 0.14f, cy), new PointF(cx - s * 0.08f, cy + s * 0.12f) });
                        break;
                    case "home":
                        g.DrawLines(pen, new[] { new PointF(cx - s * 0.32f, cy - s * 0.02f), new PointF(cx, cy - s * 0.3f), new PointF(cx + s * 0.32f, cy - s * 0.02f) });
                        g.DrawLines(pen, new[] { new PointF(cx - s * 0.22f, cy - s * 0.1f), new PointF(cx - s * 0.22f, cy + s * 0.28f), new PointF(cx + s * 0.22f, cy + s * 0.28f), new PointF(cx + s * 0.22f, cy - s * 0.1f) });
                        break;
                    case "trash":
                        g.DrawLine(pen, cx - s * 0.3f, cy - s * 0.22f, cx + s * 0.3f, cy - s * 0.22f);
                        g.DrawLines(pen, new[] { new PointF(cx - s * 0.22f, cy - s * 0.22f), new PointF(cx - s * 0.18f, cy + s * 0.32f), new PointF(cx + s * 0.18f, cy + s * 0.32f), new PointF(cx + s * 0.22f, cy - s * 0.22f) });
                        g.DrawLine(pen, cx - s * 0.08f, cy - s * 0.32f, cx + s * 0.08f, cy - s * 0.32f);
                        break;
                    case "windows":
                        {
                            float q = s * 0.3f, gap = s * 0.04f;
                            g.FillRectangle(brush, cx - q - gap / 2, cy - q - gap / 2, q, q);
                            g.FillRectangle(brush, cx + gap / 2, cy - q - gap / 2, q, q);
                            g.FillRectangle(brush, cx - q - gap / 2, cy + gap / 2, q, q);
                            g.FillRectangle(brush, cx + gap / 2, cy + gap / 2, q, q);
                            break;
                        }
                    default:
                        g.FillEllipse(brush, cx - s * 0.2f, cy - s * 0.2f, s * 0.4f, s * 0.4f);
                        break;
                }
            }
        }

        /// <summary>Rounded-square coloured tile with a white glyph (sidebar entries).</summary>
        public static void Tile(Graphics g, string glyph, Color bg, RectangleF r)
        {
            using (var path = Theme.Rounded(r, r.Width * 0.24f))
            using (var lg = new LinearGradientBrush(r, Theme.Blend(bg, Color.White, 0.15f), bg, 90f))
                g.FillPath(lg, path);
            var inner = RectangleF.Inflate(r, -r.Width * 0.18f, -r.Height * 0.18f);
            Draw(g, glyph, inner, Color.White);
        }

        // ---------------- dock icons ----------------

        public static Bitmap Render(int size, Action<Graphics, RectangleF> draw)
        {
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                Theme.HighQuality(g);
                draw(g, new RectangleF(0, 0, size, size));
            }
            return bmp;
        }

        static RectangleF TileRect(RectangleF r) { return RectangleF.Inflate(r, -r.Width * 0.06f, -r.Height * 0.06f); }

        public static void AppTile(Graphics g, RectangleF r, Color top, Color bottom)
        {
            var t = TileRect(r);
            using (var path = Theme.Squircle(t))
            {
                using (var lg = new LinearGradientBrush(t, top, bottom, 90f)) g.FillPath(lg, path);
                using (var pen = new Pen(Color.FromArgb(40, 0, 0, 0), Math.Max(1, r.Width / 96f))) g.DrawPath(pen, path);
            }
        }

        public static void WinDockLogo(Graphics g, RectangleF r)
        {
            AppTile(g, r, Color.FromArgb(74, 144, 255), Color.FromArgb(28, 70, 200));
            var t = TileRect(r);
            float s = t.Width;
            var bar = new RectangleF(t.X + s * 0.14f, t.Y + s * 0.62f, s * 0.72f, s * 0.2f);
            Theme.FillRounded(g, Color.FromArgb(150, 255, 255, 255), bar, s * 0.07f);
            Color[] cols = { Color.FromArgb(255, 95, 87), Color.FromArgb(255, 189, 46), Color.FromArgb(40, 201, 64) };
            for (int i = 0; i < 3; i++)
            {
                var sq = new RectangleF(bar.X + s * 0.06f + i * s * 0.21f, bar.Y - s * 0.14f, s * 0.17f, s * 0.17f);
                Theme.FillRounded(g, cols[i], sq, s * 0.04f);
            }
            using (var b = new SolidBrush(Color.FromArgb(230, 255, 255, 255)))
            {
                g.FillEllipse(b, t.X + s * 0.3f, t.Y + s * 0.2f, s * 0.08f, s * 0.08f);
                g.FillEllipse(b, t.X + s * 0.46f, t.Y + s * 0.2f, s * 0.08f, s * 0.08f);
                g.FillEllipse(b, t.X + s * 0.62f, t.Y + s * 0.2f, s * 0.08f, s * 0.08f);
            }
        }

        public static void Launcher(Graphics g, RectangleF r)
        {
            AppTile(g, r, Color.FromArgb(242, 242, 247), Color.FromArgb(206, 206, 214));
            var t = TileRect(r);
            float s = t.Width;
            Color[] cols = {
                Color.FromArgb(255, 69, 58), Color.FromArgb(255, 159, 10), Color.FromArgb(255, 214, 10),
                Color.FromArgb(48, 209, 88), Color.FromArgb(10, 132, 255), Color.FromArgb(94, 92, 230),
                Color.FromArgb(191, 90, 242), Color.FromArgb(255, 55, 95), Color.FromArgb(100, 210, 255) };
            float cell = s * 0.2f, gap = s * 0.06f, start = t.X + (s - (cell * 3 + gap * 2)) / 2, top = t.Y + (s - (cell * 3 + gap * 2)) / 2;
            for (int i = 0; i < 9; i++)
            {
                var sq = new RectangleF(start + (i % 3) * (cell + gap), top + (i / 3) * (cell + gap), cell, cell);
                Theme.FillRounded(g, cols[i], sq, cell * 0.28f);
            }
        }

        public static readonly string[] TagNames = { "none", "red", "orange", "yellow", "green", "blue", "purple", "grey" };
        public static Color TagColor(string tag)
        {
            switch (tag)
            {
                case "red": return Color.FromArgb(255, 69, 58);
                case "orange": return Color.FromArgb(255, 159, 10);
                case "yellow": return Color.FromArgb(255, 214, 10);
                case "green": return Color.FromArgb(48, 209, 88);
                case "blue": return Color.FromArgb(10, 132, 255);
                case "purple": return Color.FromArgb(191, 90, 242);
                case "grey": return Color.FromArgb(142, 142, 147);
            }
            return Color.Empty;
        }

        /// <summary>A folder drawn in WinDock's own style, with an optional emblem and colour tag.</summary>
        public static void Folder(Graphics g, RectangleF r, string emblem, string tag)
        {
            float s = r.Width;
            var back = new RectangleF(r.X + s * 0.08f, r.Y + s * 0.18f, s * 0.84f, s * 0.66f);
            Color baseTop = Color.FromArgb(120, 190, 255), baseBot = Color.FromArgb(52, 140, 240);
            Color tagCol = TagColor(tag);
            using (var p = new GraphicsPath())
            {
                float tabW = s * 0.32f, tabH = s * 0.08f, rad = s * 0.06f;
                p.AddArc(back.X, back.Y - tabH, rad * 2, rad * 2, 180, 90);
                p.AddLine(back.X + rad, back.Y - tabH, back.X + tabW, back.Y - tabH);
                p.AddLine(back.X + tabW, back.Y - tabH, back.X + tabW + tabH, back.Y);
                p.AddArc(back.Right - rad * 2, back.Y, rad * 2, rad * 2, 270, 90);
                p.AddArc(back.Right - rad * 2, back.Bottom - rad * 2, rad * 2, rad * 2, 0, 90);
                p.AddArc(back.X, back.Bottom - rad * 2, rad * 2, rad * 2, 90, 90);
                p.CloseFigure();
                using (var b = new SolidBrush(Color.FromArgb(70, 150, 235))) g.FillPath(b, p);
            }
            var front = new RectangleF(r.X + s * 0.08f, r.Y + s * 0.3f, s * 0.84f, s * 0.54f);
            using (var p = Theme.Rounded(front, s * 0.06f))
            using (var lg = new LinearGradientBrush(front, baseTop, baseBot, 90f))
            {
                g.FillPath(lg, p);
                using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255), Math.Max(1, s / 64f))) g.DrawLine(pen, front.X + s * 0.04f, front.Y + 1, front.Right - s * 0.04f, front.Y + 1);
            }
            if (!string.IsNullOrEmpty(emblem))
            {
                var er = new RectangleF(front.X + front.Width * 0.32f, front.Y + front.Height * 0.2f, front.Width * 0.36f, front.Width * 0.36f);
                Draw(g, emblem, er, Color.FromArgb(150, 20, 70, 150));
            }
            if (tagCol != Color.Empty)
            {
                float d = s * 0.22f;
                var dot = new RectangleF(r.Right - d - s * 0.04f, r.Bottom - d - s * 0.08f, d, d);
                using (var b = new SolidBrush(tagCol)) g.FillEllipse(b, dot);
                using (var pen = new Pen(Color.White, Math.Max(1, s / 40f))) g.DrawEllipse(pen, dot);
            }
        }

        public static void Trash(Graphics g, RectangleF r, bool full, bool dark)
        {
            float s = r.Width;
            var body = new RectangleF(r.X + s * 0.22f, r.Y + s * 0.2f, s * 0.56f, s * 0.68f);
            Color glass = dark ? Color.FromArgb(150, 210, 214, 222) : Color.FromArgb(170, 236, 238, 242);
            Color rim = dark ? Color.FromArgb(230, 200, 204, 212) : Color.FromArgb(230, 160, 164, 172);
            using (var p = new GraphicsPath())
            {
                p.AddLine(body.X, body.Y, body.Right, body.Y);
                p.AddLine(body.Right, body.Y, body.Right - s * 0.05f, body.Bottom);
                p.AddLine(body.Right - s * 0.05f, body.Bottom, body.X + s * 0.05f, body.Bottom);
                p.CloseFigure();
                using (var lg = new LinearGradientBrush(body, Theme.WithAlpha(glass, 200), Theme.WithAlpha(glass, 120), 0f)) g.FillPath(lg, p);
                if (full)
                {
                    using (var b = new SolidBrush(Color.FromArgb(235, 250, 250, 250)))
                    using (var pen = new Pen(Color.FromArgb(120, 140, 140, 150), Math.Max(1, s / 80f)))
                    {
                        g.FillEllipse(b, body.X + s * 0.02f, body.Y - s * 0.1f, s * 0.3f, s * 0.24f);
                        g.DrawEllipse(pen, body.X + s * 0.02f, body.Y - s * 0.1f, s * 0.3f, s * 0.24f);
                        g.FillEllipse(b, body.X + s * 0.22f, body.Y - s * 0.14f, s * 0.32f, s * 0.26f);
                        g.DrawEllipse(pen, body.X + s * 0.22f, body.Y - s * 0.14f, s * 0.32f, s * 0.26f);
                    }
                }
                using (var pen = new Pen(rim, Math.Max(1, s / 48f))) g.DrawPath(pen, p);
                using (var pen = new Pen(Theme.WithAlpha(rim, 150), Math.Max(1, s / 70f)))
                {
                    for (int i = 1; i <= 4; i++)
                    {
                        float x = body.X + body.Width * i / 5f;
                        float inset = (x - body.X) / body.Width;
                        g.DrawLine(pen, x, body.Y + s * 0.06f, x + (0.5f - inset) * s * 0.06f, body.Bottom - s * 0.05f);
                    }
                }
                var lip = new RectangleF(body.X - s * 0.03f, body.Y - s * 0.03f, body.Width + s * 0.06f, s * 0.06f);
                Theme.FillRounded(g, rim, lip, s * 0.03f);
            }
        }

        public static void Globe(Graphics g, RectangleF r, string letter)
        {
            AppTile(g, r, Color.FromArgb(90, 200, 250), Color.FromArgb(0, 122, 255));
            var t = TileRect(r);
            float s = t.Width, cx = t.X + s / 2, cy = t.Y + s / 2;
            using (var pen = new Pen(Color.FromArgb(220, 255, 255, 255), Math.Max(1, s / 36f)))
            {
                g.DrawEllipse(pen, cx - s * 0.3f, cy - s * 0.3f, s * 0.6f, s * 0.6f);
                g.DrawEllipse(pen, cx - s * 0.13f, cy - s * 0.3f, s * 0.26f, s * 0.6f);
                g.DrawLine(pen, cx - s * 0.3f, cy, cx + s * 0.3f, cy);
                g.DrawLine(pen, cx - s * 0.26f, cy - s * 0.15f, cx + s * 0.26f, cy - s * 0.15f);
                g.DrawLine(pen, cx - s * 0.26f, cy + s * 0.15f, cx + s * 0.26f, cy + s * 0.15f);
            }
            if (!string.IsNullOrEmpty(letter))
            {
                var badge = new RectangleF(t.Right - s * 0.36f, t.Bottom - s * 0.36f, s * 0.32f, s * 0.32f);
                using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, badge);
                using (var f = new Font(Theme.FontFamily, s * 0.2f, FontStyle.Bold, GraphicsUnit.Pixel))
                    Theme.DrawText(g, letter.ToUpperInvariant(), f, Color.FromArgb(0, 100, 220), badge, StringAlignment.Center, StringAlignment.Center);
            }
        }

        /// <summary>White tile with a website's own icon in the middle.</summary>
        public static void WebTile(Graphics g, RectangleF r, Image favicon)
        {
            AppTile(g, r, Color.FromArgb(255, 255, 255), Color.FromArgb(236, 236, 240));
            var t = TileRect(r);
            float inner = t.Width * 0.56f;
            g.DrawImage(favicon, new RectangleF(t.X + (t.Width - inner) / 2, t.Y + (t.Height - inner) / 2, inner, inner));
        }

        public static void Letter(Graphics g, RectangleF r, string text)
        {
            int hash = 0;
            foreach (char ch in text ?? "") hash = hash * 31 + ch;
            Color[] cols = { Color.FromArgb(255, 95, 87), Color.FromArgb(255, 159, 10), Color.FromArgb(48, 209, 88), Color.FromArgb(10, 132, 255), Color.FromArgb(94, 92, 230), Color.FromArgb(191, 90, 242) };
            Color c = cols[Math.Abs(hash) % cols.Length];
            AppTile(g, r, Theme.Blend(c, Color.White, 0.2f), c);
            string letter = string.IsNullOrEmpty(text) ? "?" : text.Substring(0, 1).ToUpperInvariant();
            using (var f = new Font(Theme.FontFamily, r.Width * 0.42f, FontStyle.Bold, GraphicsUnit.Pixel))
                Theme.DrawText(g, letter, f, Color.White, r, StringAlignment.Center, StringAlignment.Center);
        }

        /// <summary>WinDock's own app icon as an .ico-ready bitmap.</summary>
        public static Bitmap AppIcon(int size)
        {
            return Render(size, (g, r) => WinDockLogo(g, r));
        }

        public static Icon MakeIcon(int size)
        {
            using (var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp)) { Theme.HighQuality(g); WinDockLogo(g, new RectangleF(0, 0, size, size)); }
                IntPtr h = bmp.GetHicon();
                return Icon.FromHandle(h);
            }
        }
    }
}
