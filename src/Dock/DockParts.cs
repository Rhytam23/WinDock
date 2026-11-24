// WinDock - shell actions, folder helpers, the minimise effect, the drag ghost and the "poof".
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinDock
{
    public static class Shellx
    {
        public static void Open(string target)
        {
            try
            {
                Native.AllowSetForegroundWindow(-1);
                if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                    Process.Start(new ProcessStartInfo("explorer.exe", "\"" + target + "\"") { UseShellExecute = true });
                else
                    Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Write("open " + target + ": " + ex.Message); }
        }

        public static void ShowInExplorer(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                Native.AllowSetForegroundWindow(-1);
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Write("show in explorer: " + ex.Message); }
        }

        /// <summary>Moves files to the Recycle Bin (never deletes for good unless Windows says the bin can't take them, and then it asks).</summary>
        public static void Recycle(string[] files, IntPtr owner)
        {
            var op = new SHFILEOPSTRUCT
            {
                hwnd = owner, wFunc = Native.FO_DELETE,
                pFrom = string.Join("\0", files) + "\0\0",
                fFlags = (ushort)(Native.FOF_ALLOWUNDO | Native.FOF_WANTNUKEWARNING)
            };
            Native.SHFileOperation(ref op);
        }

        public static void CopyOrMove(string[] files, string folder, IntPtr owner)
        {
            bool sameDrive = files.Length > 0 && string.Equals(Path.GetPathRoot(files[0]), Path.GetPathRoot(folder), StringComparison.OrdinalIgnoreCase);
            var op = new SHFILEOPSTRUCT
            {
                hwnd = owner, wFunc = sameDrive ? 1u : 2u, // FO_MOVE : FO_COPY
                pFrom = string.Join("\0", files) + "\0\0",
                pTo = folder + "\0\0",
                fFlags = (ushort)Native.FOF_ALLOWUNDO
            };
            Native.SHFileOperation(ref op);
        }

        /// <summary>Empty the Recycle Bin. Windows shows its own confirmation first.</summary>
        public static void EmptyTrash(IntPtr owner)
        {
            Native.SHEmptyRecycleBin(owner, null, 0);
        }
    }

    public static class Folders
    {
        public static string Emblem(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string p = path.TrimEnd('\\');
            Func<Environment.SpecialFolder, bool> isF = f => string.Equals(p, Environment.GetFolderPath(f).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.Equals(p, Path.Combine(home, "Downloads"), StringComparison.OrdinalIgnoreCase)) return "download";
            if (isF(Environment.SpecialFolder.MyDocuments)) return "doc";
            if (isF(Environment.SpecialFolder.MyPictures)) return "picture";
            if (isF(Environment.SpecialFolder.MyMusic)) return "music";
            if (isF(Environment.SpecialFolder.MyVideos)) return "video";
            if (isF(Environment.SpecialFolder.DesktopDirectory)) return "display";
            if (string.Equals(p, home, StringComparison.OrdinalIgnoreCase)) return "home";
            return null;
        }

        public static string View(string path) { return DockState.Get("view|" + path, "auto"); }
        public static string Sort(string path)
        {
            string def = Emblem(path) == "download" ? "date" : "name";
            return DockState.Get("sort|" + path, def);
        }
    }

    /// <summary>Minimise / restore animations into and out of a dock icon (SPEC 2.5). Only for windows WinDock minimises itself.</summary>
    public static class Effects
    {
        [DllImport("user32.dll")] static extern bool IsHungAppWindow(IntPtr h);

        class Capture { public Bitmap Image; public Rectangle Rect; }
        static readonly Dictionary<IntPtr, Capture> captures = new Dictionary<IntPtr, Capture>();
        static readonly Dictionary<IntPtr, Bitmap> thumbs = new Dictionary<IntPtr, Bitmap>();

        static string Kind(Config cfg)
        {
            if (!cfg.GetBool("minimize-animation", true)) return "none";
            string k = cfg.Get("minimize-effect", "genie").ToLowerInvariant();
            return k == "scale" || k == "fade" ? k : "genie";
        }

        public static Bitmap Grab(IntPtr hwnd, out Rectangle frame)
        {
            frame = Native.FrameBounds(hwnd).ToRectangle();
            if (frame.Width < 8 || frame.Height < 8 || frame.Width > 10000) return null;
            try
            {
                RECT wr; Native.GetWindowRect(hwnd, out wr);
                Bitmap full = null;
                if (!IsHungAppWindow(hwnd))
                {
                    full = new Bitmap(wr.Width, wr.Height, PixelFormat.Format24bppRgb);
                    using (var g = Graphics.FromImage(full))
                    {
                        IntPtr hdc = g.GetHdc();
                        bool ok = Native.PrintWindow(hwnd, hdc, 2); // PW_RENDERFULLCONTENT
                        g.ReleaseHdc(hdc);
                        if (!ok) { full.Dispose(); full = null; }
                    }
                }
                Bitmap result;
                if (full != null && !MostlyBlack(full))
                {
                    var crop = new Rectangle(frame.X - wr.Left, frame.Y - wr.Top, frame.Width, frame.Height);
                    crop.Intersect(new Rectangle(0, 0, full.Width, full.Height));
                    result = full.Clone(crop, PixelFormat.Format32bppPArgb);
                    full.Dispose();
                }
                else
                {
                    if (full != null) full.Dispose();
                    result = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppPArgb);
                    using (var g = Graphics.FromImage(result)) g.CopyFromScreen(frame.Location, Point.Empty, frame.Size);
                }
                return result;
            }
            catch (Exception ex) { Log.Write("capture: " + ex.Message); return null; }
        }

        static bool MostlyBlack(Bitmap b)
        {
            int dark = 0, n = 0;
            for (int y = b.Height / 8; y < b.Height; y += Math.Max(1, b.Height / 8))
                for (int x = b.Width / 8; x < b.Width; x += Math.Max(1, b.Width / 8))
                {
                    var c = b.GetPixel(x, y); n++;
                    if (c.R < 4 && c.G < 4 && c.B < 4) dark++;
                }
            return n == 0 || dark > n * 0.9;
        }

        static Bitmap Shrink(Bitmap src, int maxW)
        {
            if (src.Width <= maxW) return src;
            int h = Math.Max(1, src.Height * maxW / src.Width);
            var b = new Bitmap(maxW, h, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(b)) { g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.DrawImage(src, 0, 0, maxW, h); }
            src.Dispose();
            return b;
        }

        public static void Minimize(IntPtr hwnd, RectangleF icon, Config cfg)
        {
            string kind = Kind(cfg);
            if (kind == "none") { Native.ShowWindowAsync(hwnd, Native.SW_MINIMIZE); return; }
            Rectangle frame;
            var img = Grab(hwnd, out frame);
            if (img == null) { Native.ShowWindowAsync(hwnd, Native.SW_MINIMIZE); return; }
            img = Shrink(img, 900);
            StoreThumb(hwnd, img);
            Capture old;
            if (captures.TryGetValue(hwnd, out old) && old.Image != null) old.Image.Dispose();
            captures[hwnd] = new Capture { Image = img, Rect = frame };
            SetTransitions(hwnd, false);
            Native.ShowWindowAsync(hwnd, Native.SW_MINIMIZE);
            new EffectWindow(img, frame, icon, kind, false, () => SetTransitions(hwnd, true)).Run();
        }

        public static void Restore(IntPtr hwnd, RectangleF icon, Config cfg)
        {
            string kind = Kind(cfg);
            Capture cap;
            if (kind == "none" || !captures.TryGetValue(hwnd, out cap) || !Native.IsIconic(hwnd))
            {
                Apps.Activate(hwnd);
                return;
            }
            captures.Remove(hwnd);
            SetTransitions(hwnd, false);
            new EffectWindow(cap.Image, cap.Rect, icon, kind, true, () =>
            {
                Apps.Activate(hwnd);
                var t = new Timer { Interval = 400 };
                t.Tick += (s, e) => { t.Stop(); t.Dispose(); SetTransitions(hwnd, true); cap.Image.Dispose(); };
                t.Start();
            }).Run();
        }

        static void SetTransitions(IntPtr hwnd, bool on)
        {
            Native.SetDwmInt(hwnd, Native.DWMWA_TRANSITIONS_FORCEDISABLED, on ? 0 : 1);
        }

        static void StoreThumb(IntPtr hwnd, Bitmap img)
        {
            Bitmap old;
            if (thumbs.TryGetValue(hwnd, out old)) old.Dispose();
            int w = 160, h = Math.Max(1, img.Height * 160 / Math.Max(1, img.Width));
            var b = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(b)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(img, 0, 0, w, h); }
            thumbs[hwnd] = b;
            if (thumbs.Count > 60) { foreach (var v in thumbs.Values) v.Dispose(); thumbs.Clear(); }
        }

        /// <summary>For "minimise windows into the dock": remember what a window looked like as it minimised.</summary>
        public static void CaptureForThumbnail(IntPtr hwnd)
        {
            if (thumbs.ContainsKey(hwnd) && captures.ContainsKey(hwnd)) return;
            Rectangle frame;
            var img = Grab(hwnd, out frame);
            if (img != null) { StoreThumb(hwnd, img); img.Dispose(); }
        }

        public static Bitmap ThumbnailFor(IntPtr hwnd)
        {
            Bitmap b;
            return thumbs.TryGetValue(hwnd, out b) ? b : null;
        }

        static float Clamp(float v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
        static float Smooth(float t) { return t * t * (3 - 2 * t); }
        static float Lerp(float a, float b, float t) { return a + (b - a) * t; }

        /// <summary>Full-path overlay that plays one minimise/restore animation and closes itself.</summary>
        class EffectWindow : LayeredForm
        {
            readonly Bitmap img; readonly RectangleF from, to; readonly string kind; readonly bool reverse; readonly Action done;
            readonly Rectangle area;
            readonly Stopwatch sw = new Stopwatch();
            readonly Timer timer = new Timer { Interval = 10 };
            const double Duration = 0.42;

            public EffectWindow(Bitmap img, Rectangle from, RectangleF to, string kind, bool reverse, Action done)
            {
                ClickThrough = true;
                this.img = img; this.from = from; this.to = to; this.kind = kind; this.reverse = reverse; this.done = done;
                var a = Rectangle.Union(from, Rectangle.Round(to));
                a.Inflate(4, 4);
                a.Intersect(SystemInformation.VirtualScreen);
                area = a;
                timer.Tick += (s, e) => Step();
            }

            public void Run()
            {
                Surface.Ensure(area.Width, area.Height);
                Draw(reverse ? 1f : 0f);
                Present(area.X, area.Y);
                sw.Start(); timer.Start();
            }

            void Step()
            {
                double t = sw.Elapsed.TotalSeconds / Duration;
                if (t >= 1)
                {
                    timer.Stop();
                    if (reverse) { if (done != null) done(); }
                    Close();
                    if (!reverse && done != null) { var d = new Timer { Interval = 300 }; d.Tick += (s, e) => { d.Stop(); d.Dispose(); done(); }; d.Start(); }
                    return;
                }
                float p = (float)t;
                Draw(reverse ? 1 - p : p);
                Surface.Present(Handle, area.X, area.Y, 255);
            }

            void Draw(float p)
            {
                using (var g = Surface.BeginDraw())
                {
                    g.InterpolationMode = InterpolationMode.Bilinear;
                    g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                    g.SmoothingMode = SmoothingMode.None;
                    g.TranslateTransform(-area.X, -area.Y);
                    if (kind == "genie") Genie(g, p);
                    else if (kind == "scale")
                    {
                        float e = Smooth(p);
                        var r = new RectangleF(Lerp(from.X, to.X, e), Lerp(from.Y, to.Y, e), Lerp(from.Width, to.Width, e), Lerp(from.Height, to.Height, e));
                        DrawAlpha(g, r, 1f - 0.6f * Clamp((p - 0.6f) / 0.4f));
                    }
                    else
                    {
                        float k = 1f - 0.08f * p;
                        var r = new RectangleF(from.X + from.Width * (1 - k) / 2, from.Y + from.Height * (1 - k) / 2, from.Width * k, from.Height * k);
                        DrawAlpha(g, r, 1f - p);
                    }
                }
            }

            void DrawAlpha(Graphics g, RectangleF r, float alpha)
            {
                if (alpha >= 0.99f) { g.DrawImage(img, r); return; }
                using (var ia = new ImageAttributes())
                {
                    var cm = new ColorMatrix { Matrix33 = Math.Max(0, alpha) };
                    ia.SetColorMatrix(cm);
                    g.DrawImage(img, Rectangle.Round(r), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
                }
            }

            void Genie(Graphics g, float p)
            {
                float a = Smooth(Clamp(p / 0.45f));
                float b = Smooth(Clamp((p - 0.28f) / 0.72f));
                int strips = (int)Math.Max(24, Math.Min(150, from.Height / 5));
                float drop = Math.Max(1, to.Bottom - from.Top);
                for (int i = 0; i < strips; i++)
                {
                    float v0 = i / (float)strips, v1 = (i + 1) / (float)strips;
                    float y0 = Lerp(from.Top + v0 * from.Height, to.Top + v0 * to.Height, b);
                    float y1 = Lerp(from.Top + v1 * from.Height, to.Top + v1 * to.Height, b);
                    float t = Clamp(((y0 + y1) / 2 - from.Top) / drop);
                    float sh = Math.Max(a * Smooth(t), b);
                    float half = Lerp(from.Width / 2, to.Width / 2, sh);
                    float cx = Lerp(from.X + from.Width / 2, to.X + to.Width / 2, sh);
                    var src = new RectangleF(0, v0 * img.Height, img.Width, (v1 - v0) * img.Height);
                    var dst = new RectangleF(cx - half, y0, half * 2, y1 - y0 + 1f);
                    g.DrawImage(img, dst, src, GraphicsUnit.Pixel);
                }
            }
        }
    }

    /// <summary>The icon that follows the cursor while dragging a dock item.</summary>
    public class DragGhost : LayeredForm
    {
        Bitmap image;
        public DragGhost() { ClickThrough = true; }
        public void SetImage(Bitmap b) { image = b; }

        public void ShowAt(Point c, bool removing)
        {
            if (image == null) return;
            int w = image.Width, h = image.Height;
            int labelH = removing ? (int)(h * 0.42f) : 0;
            Surface.Ensure(w + 40, h + labelH + 8);
            using (var g = Surface.BeginDraw())
            {
                using (var ia = new ImageAttributes())
                {
                    ia.SetColorMatrix(new ColorMatrix { Matrix33 = removing ? 0.6f : 0.9f });
                    g.DrawImage(image, new Rectangle(20, 0, w, h), 0, 0, w, h, GraphicsUnit.Pixel, ia);
                }
                if (removing)
                {
                    using (var f = Theme.MakeFont(h * 0.22f, FontStyle.Regular))
                    {
                        var r = new RectangleF(4, h + 2, w + 32, labelH);
                        Theme.FillRounded(g, Color.FromArgb(220, 40, 40, 44), r, labelH / 2f);
                        Theme.DrawText(g, "Remove", f, Color.White, r, StringAlignment.Center, StringAlignment.Center);
                    }
                }
            }
            Present(c.X - w / 2 - 20, c.Y - h / 2);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && image != null) image.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>The little cloud shown when an item is dragged off the dock.</summary>
    public class Poof : LayeredForm
    {
        readonly Stopwatch sw = Stopwatch.StartNew();
        readonly Timer timer = new Timer { Interval = 15 };
        readonly Point center; readonly float scale;

        Poof(Point c, float scale)
        {
            ClickThrough = true; center = c; this.scale = scale;
            timer.Tick += (s, e) => Step();
        }

        public static void At(Point c, float scale)
        {
            var p = new Poof(c, scale);
            p.Step();
            p.timer.Start();
        }

        void Step()
        {
            double t = sw.Elapsed.TotalSeconds / 0.45;
            if (t >= 1) { timer.Stop(); Close(); Dispose(); return; }
            int size = (int)(120 * scale);
            Surface.Ensure(size, size);
            using (var g = Surface.BeginDraw())
            {
                float c = size / 2f;
                int alpha = (int)(230 * (1 - t));
                for (int i = 0; i < 7; i++)
                {
                    double a = i * Math.PI * 2 / 7 + 0.3;
                    float dist = (float)(size * 0.12 + size * 0.22 * t);
                    float r = (float)(size * (0.13 + 0.06 * Math.Sin(i * 1.7)) * (1 - 0.3 * t));
                    float x = c + (float)Math.Cos(a) * dist, y = c + (float)Math.Sin(a) * dist;
                    using (var b = new SolidBrush(Color.FromArgb(Math.Max(0, alpha), 236, 236, 240))) g.FillEllipse(b, x - r, y - r, r * 2, r * 2);
                }
                float cr = (float)(size * 0.18 * (1 - t));
                using (var b = new SolidBrush(Color.FromArgb(Math.Max(0, alpha), 250, 250, 252))) g.FillEllipse(b, c - cr, c - cr, cr * 2, cr * 2);
            }
            Present(center.X - size / 2, center.Y - size / 2);
        }
    }
}
