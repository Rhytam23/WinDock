// WinDock - finding running apps and their windows, grouping them like the dock shows them, focusing windows.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace WinDock
{
    public class AppWindow
    {
        public IntPtr Hwnd;
        public int Pid;
        public string ExePath;     // may be null (access denied)
        public string Aumid;       // window or package app id, may be null
        public bool Packaged;
        public string Title;
        public bool Minimized;
        public string GroupKey;
    }

    public static class Apps
    {
        class ProcInfo { public string Path; public string PackagedAumid; public long Created; public DateTime Checked; }
        static readonly Dictionary<int, ProcInfo> procs = new Dictionary<int, ProcInfo>();
        static readonly Dictionary<IntPtr, KeyValuePair<string, DateTime>> windowAumids = new Dictionary<IntPtr, KeyValuePair<string, DateTime>>();
        static readonly int ownPid = Process.GetCurrentProcess().Id;

        static ProcInfo Proc(int pid)
        {
            ProcInfo info;
            IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            try
            {
                long created = 0, a, b, c;
                if (h != IntPtr.Zero) Native.GetProcessTimes(h, out created, out a, out b, out c);
                if (procs.TryGetValue(pid, out info) && info.Created == created) return info;
                info = new ProcInfo { Created = created, Checked = DateTime.Now };
                if (h != IntPtr.Zero)
                {
                    var sb = new StringBuilder(1024); int size = sb.Capacity;
                    if (Native.QueryFullProcessImageName(h, 0, sb, ref size)) info.Path = sb.ToString();
                    try
                    {
                        int len = 512; var asb = new StringBuilder(len);
                        if (Native.GetApplicationUserModelId(h, ref len, asb) == 0) info.PackagedAumid = asb.ToString();
                    }
                    catch { }
                }
                procs[pid] = info;
                if (procs.Count > 600) procs.Clear();
                return info;
            }
            finally { if (h != IntPtr.Zero) Native.CloseHandle(h); }
        }

        static string CachedWindowAumid(IntPtr hwnd)
        {
            KeyValuePair<string, DateTime> kv;
            if (windowAumids.TryGetValue(hwnd, out kv) && (DateTime.Now - kv.Value).TotalSeconds < 15) return kv.Key;
            string a = Shell.WindowAumid(hwnd);
            windowAumids[hwnd] = new KeyValuePair<string, DateTime>(a, DateTime.Now);
            if (windowAumids.Count > 800) windowAumids.Clear();
            return a;
        }

        /// <summary>Same rules as the Windows task switcher: visible, unowned (or app window), not a tool window, not cloaked.</summary>
        public static bool IsAppWindow(IntPtr h)
        {
            if (!Native.IsWindowVisible(h)) return false;
            long ex = Native.GetExStyle(h);
            bool appWin = (ex & Native.WS_EX_APPWINDOW) != 0;
            if (!appWin)
            {
                if ((ex & Native.WS_EX_TOOLWINDOW) != 0) return false;
                if (Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero) return false;
            }
            if ((ex & Native.WS_EX_NOACTIVATE) != 0 && !appWin) return false;
            if (Native.IsCloaked(h)) return false;
            string cls = Native.ClassName(h);
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd" ||
                cls == "Windows.UI.Core.CoreWindow" || cls == "XamlExplorerHostIslandWindow" || cls == "TopLevelWindowForOverflowXamlIsland")
                return false;
            if (Native.GetWindowTextLength(h) == 0 && !appWin) return false;
            return true;
        }

        public static AppWindow Describe(IntPtr h)
        {
            int pid;
            Native.GetWindowThreadProcessId(h, out pid);
            if (pid == ownPid) return null;
            var w = new AppWindow { Hwnd = h, Pid = pid, Title = Native.WindowText(h), Minimized = Native.IsIconic(h) };
            string cls = Native.ClassName(h);
            if (cls == "ApplicationFrameWindow")
            {
                // A UWP app hosted by ApplicationFrameHost: the real app is the CoreWindow child's process.
                IntPtr core = Native.FindWindowEx(h, IntPtr.Zero, "Windows.UI.Core.CoreWindow", null);
                if (core != IntPtr.Zero)
                {
                    int cpid; Native.GetWindowThreadProcessId(core, out cpid);
                    var ci = Proc(cpid);
                    w.Pid = cpid; w.ExePath = ci.Path; w.Aumid = ci.PackagedAumid; w.Packaged = ci.PackagedAumid != null;
                }
                if (w.Aumid == null) { w.Aumid = CachedWindowAumid(h); w.Packaged = w.Aumid != null; }
            }
            else
            {
                var pi = Proc(pid);
                w.ExePath = pi.Path;
                if (pi.PackagedAumid != null) { w.Aumid = pi.PackagedAumid; w.Packaged = true; }
                else
                {
                    string wa = CachedWindowAumid(h);
                    // Browser web apps ("Install as app") carry their own app id and count as their own apps.
                    if (wa != null && wa.IndexOf("_crx_", StringComparison.OrdinalIgnoreCase) >= 0) w.Aumid = wa;
                }
            }
            if (w.Aumid != null && (w.Packaged || w.Aumid.IndexOf("_crx_", StringComparison.OrdinalIgnoreCase) >= 0))
                w.GroupKey = "aumid:" + w.Aumid.ToLowerInvariant();
            else if (w.ExePath != null) w.GroupKey = "exe:" + w.ExePath.ToLowerInvariant();
            else w.GroupKey = "pid:" + w.Pid;
            return w;
        }

        /// <summary>All app windows, front-most first.</summary>
        public static List<AppWindow> Enumerate()
        {
            var list = new List<AppWindow>();
            Native.EnumWindows((h, l) =>
            {
                try
                {
                    if (IsAppWindow(h))
                    {
                        var w = Describe(h);
                        if (w != null) list.Add(w);
                    }
                }
                catch { }
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static IntPtr ForegroundRoot()
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero) return fg;
            IntPtr root = Native.GetAncestor(fg, Native.GA_ROOTOWNER);
            return root == IntPtr.Zero ? fg : root;
        }

        /// <summary>Bring a window to the front without sending any keys (SPEC 9.3: no lone Alt).</summary>
        public static void Activate(IntPtr h)
        {
            if (Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
            if (Native.SetForegroundWindow(h) && ForegroundRoot() == h) return;
            IntPtr fg = Native.GetForegroundWindow();
            int dummy;
            uint fgThread = Native.GetWindowThreadProcessId(fg, out dummy);
            uint me = Native.GetCurrentThreadId();
            bool attached = fgThread != 0 && fgThread != me && Native.AttachThreadInput(me, fgThread, true);
            Native.BringWindowToTop(h);
            Native.SetForegroundWindow(h);
            if (attached) Native.AttachThreadInput(me, fgThread, false);
        }

        public static void Close(IntPtr h) { Native.PostMessage(h, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); }

        public static string FriendlyName(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return null;
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(exePath);
                string d = vi.FileDescription;
                if (!string.IsNullOrWhiteSpace(d) && d.Length < 40) return d.Trim();
                if (!string.IsNullOrWhiteSpace(vi.ProductName) && vi.ProductName.Length < 40) return vi.ProductName.Trim();
            }
            catch { }
            return Path.GetFileNameWithoutExtension(exePath);
        }

        /// <summary>True when the front window covers its whole monitor (video, game, F11 browser).</summary>
        public static bool ForegroundIsFullscreen()
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            string cls = Native.ClassName(fg);
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd") return false;
            int pid; Native.GetWindowThreadProcessId(fg, out pid);
            if (pid == ownPid) return false;
            RECT r; Native.GetWindowRect(fg, out r);
            IntPtr mon = Native.MonitorFromWindow(fg, Native.MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO(); mi.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(mi);
            Native.GetMonitorInfo(mon, ref mi);
            if ((mi.dwFlags & 1) == 0) return false; // only the primary monitor matters for the dock
            // a maximised frameless window (Electron apps, browsers) also covers the screen when there is no taskbar,
            // but it is not a fullscreen app
            if (Native.IsZoomed(fg)) return false;
            // click-through or tool overlays (screen recorders, remote-control layers) are not fullscreen apps
            long ex = Native.GetExStyle(fg);
            if ((ex & (Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW)) != 0) return false;
            long style = Native.GetStyle(fg);
            bool noFrame = (style & Native.WS_CAPTION) != Native.WS_CAPTION;
            return noFrame && r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
        }
    }
}
