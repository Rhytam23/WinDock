// WinDock - hiding and restoring the Windows taskbar (SPEC 2.6, 0.6).
// The original state is written to a file before anything changes, so `/restore` can undo it after a crash.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace WinDock
{
    public static class Taskbar
    {
        static string StateFile { get { return Path.Combine(Config.Dir, "taskbar-hidden.txt"); } }
        public static bool IsHiddenByUs { get; private set; }

        static IntPtr Primary() { return Native.FindWindow("Shell_TrayWnd", null); }

        static List<IntPtr> AllTaskbars()
        {
            var list = new List<IntPtr>();
            IntPtr p = Primary();
            if (p != IntPtr.Zero) list.Add(p);
            IntPtr h = IntPtr.Zero;
            while ((h = Native.FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero) list.Add(h);
            return list;
        }

        static int GetState()
        {
            var abd = new APPBARDATA(); abd.cbSize = Marshal.SizeOf(abd); abd.hWnd = Primary();
            return Native.SHAppBarMessage(Native.ABM_GETSTATE, ref abd).ToInt32();
        }

        static void SetState(int state)
        {
            var abd = new APPBARDATA(); abd.cbSize = Marshal.SizeOf(abd); abd.hWnd = Primary();
            abd.lParam = new IntPtr(state);
            Native.SHAppBarMessage(Native.ABM_SETSTATE, ref abd);
        }

        /// <summary>Hide the taskbar: switch it to auto-hide (so the work area is the whole screen) and hide its windows.</summary>
        public static void Hide()
        {
            try
            {
                if (Primary() == IntPtr.Zero) return;
                if (!IsHiddenByUs)
                {
                    int original;
                    if (File.Exists(StateFile) && int.TryParse(File.ReadAllText(StateFile).Trim(), out original))
                    {
                        // left over from a crash: keep the real original state
                    }
                    else
                    {
                        original = GetState();
                        Directory.CreateDirectory(Config.Dir);
                        File.WriteAllText(StateFile, original.ToString());
                    }
                    if ((GetState() & Native.ABS_AUTOHIDE) == 0) SetState(Native.ABS_AUTOHIDE | Native.ABS_ALWAYSONTOP);
                    IsHiddenByUs = true;
                }
                foreach (var h in AllTaskbars())
                    if (Native.IsWindowVisible(h)) Native.ShowWindow(h, Native.SW_HIDE);
            }
            catch (Exception ex) { Log.Write("taskbar hide: " + ex.Message); }
        }

        /// <summary>Called periodically: Explorer sometimes shows the taskbar again (Start menu, Explorer restart).</summary>
        public static void KeepHidden()
        {
            if (!IsHiddenByUs) return;
            foreach (var h in AllTaskbars())
                if (Native.IsWindowVisible(h)) Native.ShowWindow(h, Native.SW_HIDE);
        }

        /// <summary>Put the taskbar back exactly as it was. Safe to call any number of times, also from /restore.</summary>
        public static void Restore()
        {
            try
            {
                int original = -1;
                if (File.Exists(StateFile))
                {
                    int.TryParse(File.ReadAllText(StateFile).Trim(), out original);
                }
                if (original >= 0) SetState(original);
                foreach (var h in AllTaskbars()) Native.ShowWindow(h, Native.SW_SHOWNA);
                if (File.Exists(StateFile)) File.Delete(StateFile);
                IsHiddenByUs = false;
            }
            catch (Exception ex) { Log.Write("taskbar restore: " + ex.Message); }
        }
    }
}
