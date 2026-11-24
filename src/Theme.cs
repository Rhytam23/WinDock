// WinDock - colours, fonts and drawing helpers shared by every surface.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Microsoft.Win32;

namespace WinDock
{
    public class Palette
    {
        public bool Dark;
        public Color WindowBg, SidebarBg, CardBg, Hairline, Text, SubText, Accent, AccentText, ToggleOff, Knob, Field, FieldText, Hover, Selection;
        public Color MenuBg, MenuHover, MenuText, MenuSub, MenuBorder;

        public static Palette Make(bool dark)
        {
            var p = new Palette { Dark = dark };
            if (dark)
            {
                p.WindowBg = Color.FromArgb(30, 30, 32);
                p.SidebarBg = Color.FromArgb(40, 40, 43);
                p.CardBg = Color.FromArgb(44, 44, 47);
                p.Hairline = Color.FromArgb(58, 58, 62);
                p.Text = Color.FromArgb(236, 236, 238);
                p.SubText = Color.FromArgb(152, 152, 158);
                p.Accent = Color.FromArgb(10, 132, 255);
                p.ToggleOff = Color.FromArgb(72, 72, 76);
                p.Knob = Color.White;
                p.Field = Color.FromArgb(56, 56, 60);
                p.FieldText = Color.FromArgb(236, 236, 238);
                p.Hover = Color.FromArgb(52, 52, 56);
                p.MenuBg = Color.FromArgb(40, 40, 43);
                p.MenuHover = Color.FromArgb(10, 100, 220);
                p.MenuText = Color.FromArgb(236, 236, 238);
                p.MenuSub = Color.FromArgb(150, 150, 156);
                p.MenuBorder = Color.FromArgb(70, 70, 74);
            }
            else
            {
                p.WindowBg = Color.FromArgb(242, 242, 244);
                p.SidebarBg = Color.FromArgb(232, 232, 236);
                p.CardBg = Color.FromArgb(255, 255, 255);
                p.Hairline = Color.FromArgb(222, 222, 226);
                p.Text = Color.FromArgb(28, 28, 30);
                p.SubText = Color.FromArgb(120, 120, 126);
                p.Accent = Color.FromArgb(0, 122, 255);
                p.ToggleOff = Color.FromArgb(214, 214, 218);
                p.Knob = Color.White;
                p.Field = Color.FromArgb(222, 222, 226);
                p.FieldText = Color.FromArgb(28, 28, 30);
                p.Hover = Color.FromArgb(224, 224, 228);
                p.MenuBg = Color.FromArgb(246, 246, 248);
                p.MenuHover = Color.FromArgb(0, 110, 240);
                p.MenuText = Color.FromArgb(28, 28, 30);
                p.MenuSub = Color.FromArgb(130, 130, 136);
                p.MenuBorder = Color.FromArgb(210, 210, 214);
            }
            p.AccentText = Color.White;
            p.Selection = p.Accent;
            return p;
        }
    }

    public static class Theme
    {
        public static Config Cfg; // set at startup

        static bool ReadLight(string name)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (k == null) return false;
                    object v = k.GetValue(name);
                    return v is int && (int)v == 1;
                }
            }
            catch { return false; }
        }

        // registry reads are cached; App.ThemeChanged() calls Reset() when Windows or the user changes the theme
        static bool? appsDark, shellDark, transparency;
        public static void Reset() { appsDark = null; shellDark = null; transparency = null; }

        static string Mode { get { return Cfg == null ? "auto" : Cfg.Get("appearance", "auto").ToLowerInvariant(); } }

        /// <summary>Dark for WinDock's own windows (Settings, menus): follows the Windows app theme unless forced.</summary>
        public static bool AppsDark
        {
            get { if (appsDark == null) appsDark = ReadAppsDark(); return appsDark.Value; }
        }
        static bool ReadAppsDark()
        {
            {
                if (Mode == "dark") return true;
                if (Mode == "light") return false;
                return !ReadLight("AppsUseLightTheme");
            }
        }

        /// <summary>Dark for the dock and other shell surfaces: follows the Windows (taskbar) theme unless forced.</summary>
        public static bool ShellDark
        {
            get { if (shellDark == null) shellDark = ReadShellDark(); return shellDark.Value; }
        }
        static bool ReadShellDark()
        {
            {
                if (Mode == "dark") return true;
                if (Mode == "light") return false;
                return !ReadLight("SystemUsesLightTheme");
            }
        }

        public static bool Transparency
        {
            get { if (transparency == null) transparency = ReadTransparency(); return transparency.Value; }
        }
        static bool ReadTransparency()
        {
            {
                if (Cfg != null && !Cfg.GetBool("transparency", true)) return false;
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    {
                        if (k == null) return true;
                        object v = k.GetValue("EnableTransparency");
                        return !(v is int) || (int)v != 0;
                    }
                }
                catch { return true; }
            }
        }

        public static Palette Current { get { return Palette.Make(AppsDark); } }

        static string fontFamily;
        public static string FontFamily
        {
            get
            {
                if (fontFamily != null) return fontFamily;
                string want = Cfg == null ? "" : Cfg.Get("font", "");
                using (var fonts = new InstalledFontCollection())
                {
                    foreach (var name in new[] { want, "Segoe UI Variable Text", "Segoe UI" })
                    {
                        if (string.IsNullOrEmpty(name)) continue;
                        foreach (var f in fonts.Families) if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) { fontFamily = f.Name; return fontFamily; }
                    }
                }
                fontFamily = "Segoe UI";
                return fontFamily;
            }
        }
        public static void ResetFont() { fontFamily = null; }

        public static Font MakeFont(float px, FontStyle style)
        {
            return new Font(FontFamily, px, style, GraphicsUnit.Pixel);
        }

        // ---- drawing helpers ----
        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>Superellipse-like "squircle" used for app tiles.</summary>
        public static GraphicsPath Squircle(RectangleF r)
        {
            return Rounded(r, r.Width * 0.225f);
        }

        public static void FillRounded(Graphics g, Color c, RectangleF r, float radius)
        {
            using (var p = Rounded(r, radius)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void StrokeRounded(Graphics g, Color c, float width, RectangleF r, float radius)
        {
            using (var p = Rounded(r, radius)) using (var pen = new Pen(c, width)) g.DrawPath(pen, p);
        }

        public static void HighQuality(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.CompositingQuality = CompositingQuality.HighQuality;
        }

        public static Color Blend(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static Color WithAlpha(Color c, int a) { return Color.FromArgb(Math.Max(0, Math.Min(255, a)), c.R, c.G, c.B); }

        public static void DrawText(Graphics g, string s, Font f, Color c, RectangleF r, StringAlignment h, StringAlignment v)
        {
            using (var sf = new StringFormat())
            using (var b = new SolidBrush(c))
            {
                sf.Alignment = h; sf.LineAlignment = v;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(s, f, b, r, sf);
            }
        }

        public static SizeF Measure(Graphics g, string s, Font f)
        {
            using (var sf = new StringFormat(StringFormat.GenericTypographic))
            {
                sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                return g.MeasureString(s, f, 10000, sf);
            }
        }
    }
}
