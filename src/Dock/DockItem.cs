// WinDock - one item in the dock and how it maps to pins and running apps.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;

namespace WinDock
{
    public enum DockKind { App, Folder, File, Website, Launcher, Trash, Divider, MinWindow }

    public class DockItem
    {
        public DockKind Kind;
        public PinEntry Pin;              // null for running apps that aren't pinned, Trash, Launcher, minimised windows
        public string Label;
        public string Target;             // what a click starts / opens
        public string MatchAumid, MatchExe, MatchName;
        public string GroupKey;           // for running apps that aren't pinned
        public List<AppWindow> Windows = new List<AppWindow>();
        public IntPtr MinHwnd;            // for MinWindow items
        public Bitmap MinThumb;

        // icon cache: full-resolution source and two pre-scaled copies
        public Bitmap IconSource;
        public Bitmap IconBase, IconHi;
        public int IconBaseSize, IconHiSize;
        public bool IconRequested;
        public string IconKey;

        // animation state
        public float Scale = 1f;
        public double BounceStart = -1;
        public double LaunchStart = -1;
        public float CurX, CurW;          // last laid-out screen position (centre x) and size

        public bool Running { get { return Windows.Count > 0; } }
        public bool IsPinned { get { return Pin != null; } }
        public bool RightSide { get { return Kind == DockKind.Folder || Kind == DockKind.File || Kind == DockKind.Trash || Kind == DockKind.MinWindow || (Pin != null && Pin.RightSide); } }

        public static DockItem FromPin(PinEntry pin)
        {
            var it = new DockItem { Pin = pin, Target = pin.ExpandedTarget, Label = pin.Label };
            string t = it.Target;
            if (pin.IsWebsite) it.Kind = DockKind.Website;
            else if (Directory.Exists(t) && !t.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) it.Kind = DockKind.Folder;
            else if (t.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
            {
                it.Kind = DockKind.App;
                it.MatchAumid = t.Substring("shell:AppsFolder\\".Length);
            }
            else if (t.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
            {
                it.Kind = DockKind.App;
                it.MatchAumid = Shell.SettingsAumid;
            }
            else if (t.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                it.Kind = DockKind.App;
                var li = Shell.ResolveLink(t);
                if (li != null)
                {
                    if (!string.IsNullOrEmpty(li.Aumid)) it.MatchAumid = li.Aumid;
                    if (!string.IsNullOrEmpty(li.Path) && li.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        // Shortcuts that start a browser web app (--app-id) only match that web app's windows.
                        if (li.Arguments == null || li.Arguments.IndexOf("--app-id", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            it.MatchExe = li.Path; it.MatchName = Path.GetFileName(li.Path);
                        }
                    }
                }
                if (it.Label == null) it.Label = Path.GetFileNameWithoutExtension(t);
            }
            else if (t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                it.Kind = DockKind.App;
                it.MatchExe = t; it.MatchName = Path.GetFileName(t);
            }
            else if (File.Exists(t)) it.Kind = DockKind.File;
            else it.Kind = DockKind.App; // other URI schemes / missing targets behave like an app launcher

            if (it.Label == null) it.Label = DeriveLabel(it);
            return it;
        }

        static string DeriveLabel(DockItem it)
        {
            string t = it.Target;
            switch (it.Kind)
            {
                case DockKind.Website: return SiteIcons.Host(t) ?? t;
                case DockKind.Folder: return Shell.DisplayName(t) ?? Path.GetFileName(t.TrimEnd('\\')) ?? t;
                case DockKind.File: return Path.GetFileName(t);
            }
            if (it.MatchAumid != null) return Shell.DisplayName(t) ?? it.MatchAumid;
            if (t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return Apps.FriendlyName(t);
            return t;
        }

        public static DockItem FromWindow(AppWindow w)
        {
            var it = new DockItem { Kind = DockKind.App, GroupKey = w.GroupKey };
            if (w.GroupKey.StartsWith("aumid:"))
            {
                it.Target = "shell:AppsFolder\\" + w.Aumid;
                it.MatchAumid = w.Aumid;
                it.Label = Shell.DisplayName(it.Target);
            }
            else
            {
                it.Target = w.ExePath;
                it.MatchExe = w.ExePath;
                it.Label = Apps.FriendlyName(w.ExePath);
            }
            if (string.IsNullOrEmpty(it.Label)) it.Label = w.Title;
            return it;
        }

        public bool Matches(AppWindow w)
        {
            if (Kind != DockKind.App) return false;
            if (GroupKey != null) return GroupKey == w.GroupKey;
            if (MatchAumid != null && w.Aumid != null && string.Equals(MatchAumid, w.Aumid, StringComparison.OrdinalIgnoreCase)) return true;
            bool webApp = w.Aumid != null && w.Aumid.IndexOf("_crx_", StringComparison.OrdinalIgnoreCase) >= 0;
            if (webApp) return false;
            if (MatchExe != null && w.ExePath != null && string.Equals(MatchExe, w.ExePath, StringComparison.OrdinalIgnoreCase)) return true;
            if (MatchName != null && w.ExePath != null && string.Equals(MatchName, Path.GetFileName(w.ExePath), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>The path the shell should give an icon for.</summary>
        public string IconTarget
        {
            get
            {
                if (Kind == DockKind.App && Pin == null && Windows.Count > 0 && GroupKey != null && GroupKey.StartsWith("exe:")) return Windows[0].ExePath;
                return Target;
            }
        }

        /// <summary>Path to show in Explorer / the program to open dropped files with.</summary>
        public string ProgramPath
        {
            get
            {
                if (MatchExe != null) return MatchExe;
                if (Kind == DockKind.App && Target != null && Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return Target;
                foreach (var w in Windows) if (w.ExePath != null) return w.ExePath;
                return null;
            }
        }

        public void SetIcon(Bitmap src)
        {
            if (IconSource != null && IconSource != src) IconSource.Dispose();
            IconSource = src;
            if (IconBase != null) { IconBase.Dispose(); IconBase = null; }
            if (IconHi != null) { IconHi.Dispose(); IconHi = null; }
        }

        /// <summary>Returns a copy of the icon pre-scaled to about `px` pixels (cached), so per-frame drawing is cheap.</summary>
        public Bitmap IconFor(int basePx, int hiPx, float wantPx)
        {
            if (IconSource == null) return null;
            if (IconBase == null || IconBaseSize != basePx) { if (IconBase != null) IconBase.Dispose(); IconBase = Resize(IconSource, basePx); IconBaseSize = basePx; }
            if (wantPx <= basePx + 0.5f) return IconBase;
            if (IconHi == null || IconHiSize != hiPx) { if (IconHi != null) IconHi.Dispose(); IconHi = Resize(IconSource, hiPx); IconHiSize = hiPx; }
            return IconHi;
        }

        static Bitmap Resize(Bitmap src, int px)
        {
            px = Math.Max(4, px);
            var b = new Bitmap(px, px, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(b))
            {
                Theme.HighQuality(g);
                g.DrawImage(src, new Rectangle(0, 0, px, px));
            }
            return b;
        }

        public void Launch()
        {
            try
            {
                Native.AllowSetForegroundWindow(-1);
                if (Target == null) return;
                if (Target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                    Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Target + "\"") { UseShellExecute = true });
                else
                    Process.Start(new ProcessStartInfo(Target) { UseShellExecute = true, WorkingDirectory = SafeDir(Target) });
            }
            catch (Exception ex) { Log.Write("launch " + Target + ": " + ex.Message); }
        }

        static string SafeDir(string target)
        {
            try { if (File.Exists(target)) return Path.GetDirectoryName(target); } catch { }
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        /// <summary>Open files with this app (SPEC 2.2: drop files on an app icon).</summary>
        public void OpenFiles(string[] files)
        {
            try
            {
                string args = string.Join(" ", Array.ConvertAll(files, f => "\"" + f + "\""));
                string exe = ProgramPath;
                if (exe != null) { Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true }); return; }
                if (Target != null && Target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    var li = Shell.ResolveLink(Target);
                    if (li != null && !string.IsNullOrEmpty(li.Path))
                    {
                        Process.Start(new ProcessStartInfo(li.Path, (li.Arguments + " " + args).Trim()) { UseShellExecute = true });
                        return;
                    }
                }
                foreach (var f in files) Process.Start(new ProcessStartInfo(f) { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Write("open files: " + ex.Message); }
        }
    }
}
