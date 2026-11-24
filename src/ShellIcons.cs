// WinDock - getting icons, names and shortcut targets from the Windows shell, plus website icons.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace WinDock
{
    public static class Shell
    {
        public static readonly Guid IID_IShellItem = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
        public static PROPERTYKEY PKEY_AppUserModel_ID = new PROPERTYKEY(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
        public const string SettingsAumid = "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel";

        public static string ParsingNameFor(string target)
        {
            if (string.IsNullOrEmpty(target)) return target;
            if (target.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase)) return @"shell:AppsFolder\" + SettingsAumid;
            return Environment.ExpandEnvironmentVariables(target);
        }

        static IShellItem Item(string parsingName)
        {
            object o;
            Native.SHCreateItemFromParsingName(parsingName, IntPtr.Zero, IID_IShellItem, out o);
            return (IShellItem)o;
        }

        /// <summary>Icon (or thumbnail) of a file, folder, exe or shell:AppsFolder item at the given pixel size.</summary>
        public static Bitmap GetImage(string target, int size, bool iconOnly)
        {
            IShellItem item = null;
            try
            {
                item = Item(ParsingNameFor(target));
                var f = (IShellItemImageFactory)item;
                IntPtr hbmp;
                int flags = 0x1 | (iconOnly ? 0x4 : 0);  // BIGGERSIZEOK | ICONONLY
                int hr = f.GetImage(new SIZE(size, size), flags, out hbmp);
                if (hr != 0 || hbmp == IntPtr.Zero) { Log.Write("icon: GetImage " + target + " hr=0x" + hr.ToString("X8")); return null; }
                try { return FromHBitmap(hbmp); }
                finally { Native.DeleteObject(hbmp); }
            }
            catch (Exception ex) { Log.Write("icon: " + target + ": " + ex.Message); return null; }
            finally { if (item != null) Marshal.ReleaseComObject(item); }
        }

        public static string DisplayName(string target)
        {
            IShellItem item = null;
            try
            {
                item = Item(ParsingNameFor(target));
                string name;
                item.GetDisplayName(0, out name);
                return name;
            }
            catch { return null; }
            finally { if (item != null) Marshal.ReleaseComObject(item); }
        }

        /// <summary>Converts a 32-bit HBITMAP from the shell into a Bitmap that keeps its alpha channel.</summary>
        public static Bitmap FromHBitmap(IntPtr hbmp)
        {
            using (var src = Image.FromHbitmap(hbmp))
            {
                if (Image.GetPixelFormatSize(src.PixelFormat) < 32) return new Bitmap(src);
                var rect = new Rectangle(0, 0, src.Width, src.Height);
                var data = src.LockBits(rect, ImageLockMode.ReadOnly, src.PixelFormat);
                try
                {
                    // the stride can be negative (bottom-up DIB): copy row by row into a top-down buffer
                    int row = src.Width * 4, bytes = row * src.Height;
                    var buf = new byte[bytes];
                    for (int y = 0; y < src.Height; y++)
                        Marshal.Copy(data.Scan0 + y * data.Stride, buf, y * row, row);
                    bool anyAlpha = false, premultiplied = true;
                    for (int i = 0; i < bytes; i += 4)
                    {
                        byte a = buf[i + 3];
                        if (a != 0) anyAlpha = true;
                        if (buf[i] > a || buf[i + 1] > a || buf[i + 2] > a) premultiplied = false;
                    }
                    if (!anyAlpha) { for (int i = 3; i < bytes; i += 4) buf[i] = 255; premultiplied = false; }
                    if (premultiplied)
                    {
                        for (int i = 0; i < bytes; i += 4)
                        {
                            int a = buf[i + 3];
                            if (a > 0 && a < 255)
                            {
                                buf[i] = (byte)Math.Min(255, buf[i] * 255 / a);
                                buf[i + 1] = (byte)Math.Min(255, buf[i + 1] * 255 / a);
                                buf[i + 2] = (byte)Math.Min(255, buf[i + 2] * 255 / a);
                            }
                        }
                    }
                    var result = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
                    var rd = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    for (int y = 0; y < src.Height; y++)
                        Marshal.Copy(buf, y * row, rd.Scan0 + y * rd.Stride, row);
                    result.UnlockBits(rd);
                    return result;
                }
                finally { src.UnlockBits(data); }
            }
        }

        public class LinkInfo { public string Path, Arguments, Aumid; }

        public static LinkInfo ResolveLink(string lnk)
        {
            object o = null;
            try
            {
                o = new ShellLinkCoClass();
                ((IPersistFile)o).Load(lnk, 0);
                var link = (IShellLinkW)o;
                var sb = new StringBuilder(1024);
                link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
                var args = new StringBuilder(2048);
                link.GetArguments(args, args.Capacity);
                var info = new LinkInfo { Path = sb.ToString(), Arguments = args.ToString() };
                try
                {
                    var store = (IPropertyStore)o;
                    PROPVARIANT pv;
                    if (store.GetValue(ref PKEY_AppUserModel_ID, out pv) == 0)
                    {
                        if (pv.vt == 31 && pv.p != IntPtr.Zero) info.Aumid = Marshal.PtrToStringUni(pv.p);
                        Native.PropVariantClear(ref pv);
                    }
                }
                catch { }
                return info;
            }
            catch { return null; }
            finally { if (o != null) Marshal.ReleaseComObject(o); }
        }

        public static void CreateLink(string lnkPath, string target, string args, string description)
        {
            object o = new ShellLinkCoClass();
            try
            {
                var link = (IShellLinkW)o;
                link.SetPath(target);
                if (!string.IsNullOrEmpty(args)) link.SetArguments(args);
                link.SetWorkingDirectory(Path.GetDirectoryName(target));
                link.SetDescription(description ?? "");
                link.SetIconLocation(target, 0);
                ((IPersistFile)o).Save(lnkPath, true);
            }
            finally { Marshal.ReleaseComObject(o); }
        }

        public static string WindowAumid(IntPtr hwnd)
        {
            IPropertyStore store = null;
            try
            {
                Native.SHGetPropertyStoreForWindow(hwnd, typeof(IPropertyStore).GUID, out store);
                PROPVARIANT pv;
                if (store.GetValue(ref PKEY_AppUserModel_ID, out pv) == 0)
                {
                    string s = null;
                    if (pv.vt == 31 && pv.p != IntPtr.Zero) s = Marshal.PtrToStringUni(pv.p);
                    Native.PropVariantClear(ref pv);
                    return s;
                }
            }
            catch { }
            finally { if (store != null) Marshal.ReleaseComObject(store); }
            return null;
        }
    }

    /// <summary>Loads shell icons on a background STA thread so the dock never waits for the disk.</summary>
    public static class IconLoader
    {
        static readonly BlockingCollection<Action> queue = new BlockingCollection<Action>();
        static SynchronizationContext ui;
        static Thread thread;

        public static void Start()
        {
            ui = SynchronizationContext.Current;
            thread = new Thread(() =>
            {
                foreach (var work in queue.GetConsumingEnumerable())
                {
                    try { work(); } catch (Exception ex) { Log.Write("icon loader: " + ex.Message); }
                }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Name = "WinDock icons";
            thread.Start();
        }

        /// <summary>Runs `load` on the icon thread and hands the result to `done` on the UI thread.</summary>
        public static void Request<T>(Func<T> load, Action<T> done)
        {
            queue.Add(() =>
            {
                T result = load();
                if (ui != null) ui.Post(_ => done(result), null); else done(result);
            });
        }
    }

    /// <summary>Website icons: fetched once over HTTPS (only when the user pins a website) and cached on disk.</summary>
    public static class SiteIcons
    {
        static readonly HashSet<string> inFlight = new HashSet<string>();

        static string CachePath(string host) { return Path.Combine(Config.CacheDir, "sites", host + ".png"); }

        public static string Host(string url)
        {
            try { return new Uri(url).Host.ToLowerInvariant(); } catch { return null; }
        }

        public static Bitmap Cached(string url)
        {
            string host = Host(url);
            if (host == null) return null;
            string p = CachePath(host);
            if (!File.Exists(p)) return null;
            try { using (var fs = File.OpenRead(p)) using (var img = Image.FromStream(fs)) return new Bitmap(img); }
            catch { return null; }
        }

        public static void Fetch(string url, Action<Bitmap> done)
        {
            string host = Host(url);
            if (host == null || inFlight.Contains(host)) return;
            if (File.Exists(CachePath(host) + ".none")) return; // tried before, site has no icon
            inFlight.Add(host);
            var ui = SynchronizationContext.Current;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Bitmap result = null;
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    string scheme = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "http" : "https";
                    foreach (var path in new[] { "/apple-touch-icon.png", "/favicon.ico" })
                    {
                        try
                        {
                            var req = (HttpWebRequest)WebRequest.Create(scheme + "://" + host + path);
                            req.Timeout = 6000; req.UserAgent = "WinDock";
                            using (var resp = (HttpWebResponse)req.GetResponse())
                            using (var s = resp.GetResponseStream())
                            {
                                var ms = new MemoryStream();
                                s.CopyTo(ms);
                                if (ms.Length < 64) continue;
                                ms.Position = 0;
                                Bitmap bmp = null;
                                if (path.EndsWith(".ico"))
                                {
                                    try { using (var ico = new Icon(ms, 256, 256)) bmp = ico.ToBitmap(); } catch { ms.Position = 0; }
                                }
                                if (bmp == null) using (var img = Image.FromStream(ms)) bmp = new Bitmap(img);
                                if (bmp.Width < 8) { bmp.Dispose(); continue; }
                                result = bmp;
                                break;
                            }
                        }
                        catch { }
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(CachePath(host)));
                    if (result != null) lock (result) result.Save(CachePath(host), ImageFormat.Png);
                    else File.WriteAllText(CachePath(host) + ".none", "");
                }
                catch (Exception ex) { Log.Write("site icon: " + ex.Message); }
                ui.Post(__ => { inFlight.Remove(host); if (result != null) done(result); }, null);
            });
        }
    }
}
