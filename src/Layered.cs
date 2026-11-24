// WinDock - per-pixel-alpha windows (UpdateLayeredWindow) used by the dock, stacks, labels and effects.
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinDock
{
    /// <summary>A premultiplied 32-bit drawing surface whose pixels live in a DIB section, so presenting needs no copy.</summary>
    public class LayerSurface : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

        public int Width { get; private set; }
        public int Height { get; private set; }
        public Bitmap Bitmap { get; private set; }
        IntPtr hdc, hbmp, oldBmp, bits;

        public void Ensure(int w, int h)
        {
            w = Math.Max(1, w); h = Math.Max(1, h);
            if (Bitmap != null && w == Width && h == Height) return;
            Free();
            var bi = new BITMAPINFOHEADER { biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER)), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            hdc = Native.CreateCompatibleDC(screen);
            Native.ReleaseDC(IntPtr.Zero, screen);
            hbmp = CreateDIBSection(hdc, ref bi, 0, out bits, IntPtr.Zero, 0);
            oldBmp = Native.SelectObject(hdc, hbmp);
            Bitmap = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);
            Width = w; Height = h;
        }

        public Graphics BeginDraw()
        {
            var g = Graphics.FromImage(Bitmap);
            g.Clear(Color.Transparent);
            Theme.HighQuality(g);
            return g;
        }

        public void Present(IntPtr hwnd, int x, int y, byte opacity)
        {
            var dst = new POINT(x, y);
            var size = new SIZE(Width, Height);
            var src = new POINT(0, 0);
            var blend = new BLENDFUNCTION { BlendOp = Native.AC_SRC_OVER, SourceConstantAlpha = opacity, AlphaFormat = Native.AC_SRC_ALPHA };
            Native.UpdateLayeredWindow(hwnd, IntPtr.Zero, ref dst, ref size, hdc, ref src, 0, ref blend, Native.ULW_ALPHA);
        }

        void Free()
        {
            if (Bitmap != null) { Bitmap.Dispose(); Bitmap = null; }
            if (hdc != IntPtr.Zero)
            {
                Native.SelectObject(hdc, oldBmp);
                Native.DeleteObject(hbmp);
                Native.DeleteDC(hdc);
                hdc = IntPtr.Zero;
            }
        }

        public void Dispose() { Free(); }
    }

    /// <summary>Borderless topmost layered window. Subclasses draw into Surface and call Present().</summary>
    public class LayeredForm : Form
    {
        public readonly LayerSurface Surface = new LayerSurface();
        protected bool NoActivate = true;
        protected bool ClickThrough = false;
        public byte Opacity255 = 255;

        public LayeredForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= (int)(Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST);
                if (NoActivate) cp.ExStyle |= (int)Native.WS_EX_NOACTIVATE;
                if (ClickThrough) cp.ExStyle |= (int)Native.WS_EX_TRANSPARENT;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return NoActivate; } }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE && NoActivate) { m.Result = new IntPtr(Native.MA_NOACTIVATE); return; }
            base.WndProc(ref m);
        }

        /// <summary>Show the surface at screen position (x, y), resizing the window to the surface.</summary>
        public void Present(int x, int y)
        {
            if (!IsHandleCreated) CreateHandle();
            if (!Visible) Bounds = new Rectangle(x, y, Surface.Width, Surface.Height);
            Surface.Present(Handle, x, y, Opacity255);
            if (!Visible)
            {
                Show();
                Surface.Present(Handle, x, y, Opacity255);
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | (NoActivate ? Native.SWP_NOACTIVATE : 0));
            }
        }

        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Surface.Dispose();
            base.Dispose(disposing);
        }
    }
}
