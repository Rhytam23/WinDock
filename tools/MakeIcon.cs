// Build helper: writes WinDock's own app icon (drawn in code by Glyphs.WinDockLogo) as a multi-size .ico.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

static class MakeIcon
{
    static int Main(string[] args)
    {
        string outPath = args.Length > 0 ? args[0] : "windock.ico";
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var images = new List<byte[]>();
        foreach (int s in sizes)
        {
            using (var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp)) { WinDock.Theme.HighQuality(g); WinDock.Glyphs.WinDockLogo(g, new RectangleF(0, 0, s, s)); }
                var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                images.Add(ms.ToArray());
            }
        }
        using (var fs = File.Create(outPath))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                w.Write(images[i].Length); w.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) w.Write(img);
        }
        return 0;
    }
}
