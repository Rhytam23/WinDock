// WinDock - per-user install / uninstall (SPEC section 1). No admin rights, nothing outside the user's profile.
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WinDock
{
    public static class Installer
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\WinDock";

        public static string InstallDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinDock"); } }
        public static string InstalledExe { get { return Path.Combine(InstallDir, "WinDock.exe"); } }
        static string ShortcutPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "WinDock.lnk"); } }

        public static bool StartsAtLogin()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("WinDock") != null;
        }

        public static void SetStartAtLogin(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on)
                {
                    string exe = File.Exists(InstalledExe) ? InstalledExe : Application.ExecutablePath;
                    k.SetValue("WinDock", "\"" + exe + "\"");
                }
                else if (k.GetValue("WinDock") != null) k.DeleteValue("WinDock");
            }
        }

        /// <summary>WinDock.exe /install [noautostart] : copy to %LOCALAPPDATA%\WinDock, Start Menu shortcut, autostart, start.</summary>
        public static int Install(string[] args)
        {
            bool autostart = true;
            foreach (var a in args) if (a.Equals("noautostart", StringComparison.OrdinalIgnoreCase)) autostart = false;
            try
            {
                Console.WriteLine("Installing WinDock for " + Environment.UserName + "...");
                if (!Program.QuitRunning(8000)) Console.WriteLine("  (a running WinDock did not quit in time; trying anyway)");
                Directory.CreateDirectory(InstallDir);
                string src = Application.ExecutablePath;
                if (!string.Equals(Path.GetFullPath(src), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase))
                {
                    for (int i = 0; ; i++)
                    {
                        try { File.Copy(src, InstalledExe, true); break; }
                        catch (IOException) { if (i > 20) throw; System.Threading.Thread.Sleep(250); }
                    }
                }
                Console.WriteLine("  copied to " + InstalledExe);
                Directory.CreateDirectory(Path.GetDirectoryName(ShortcutPath));
                Shell.CreateLink(ShortcutPath, InstalledExe, null, "WinDock - a dock and desktop shell for Windows");
                Console.WriteLine("  Start Menu shortcut created");
                SetStartAtLogin(autostart);
                Console.WriteLine(autostart ? "  WinDock will start when you sign in" : "  WinDock will not start at sign-in");
                RegisterUninstall();
                Console.WriteLine("  added to Settings > Apps > Installed apps");
                if (!HasArg(args, "nostart"))
                {
                    Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true, WorkingDirectory = InstallDir });
                    Console.WriteLine("  WinDock started.");
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Install failed: " + ex.Message);
                Log.Write("install: " + ex);
                return 1;
            }
        }

        static bool HasArg(string[] args, string name)
        {
            foreach (var a in args) if (a.TrimStart('/', '-').Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>The entry in Settings > Apps > Installed apps (per user, no admin). Its Uninstall button runs
        /// "WinDock.exe /uninstall /ui".</summary>
        static void RegisterUninstall()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "WinDock");
                k.SetValue("DisplayVersion", App.Version);
                k.SetValue("Publisher", "WinDock (open source, MIT)");
                k.SetValue("DisplayIcon", InstalledExe);
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("UninstallString", "\"" + InstalledExe + "\" /uninstall /ui");
                k.SetValue("QuietUninstallString", "\"" + InstalledExe + "\" /uninstall");
                k.SetValue("URLInfoAbout", "https://github.com/Rhytam23/WinDock");
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                try { k.SetValue("EstimatedSize", (int)Math.Max(1, new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord); } catch { }
            }
        }

        /// <summary>WinDock.exe /uninstall [/ui] : stop WinDock, give back the taskbar, remove autostart, the shortcut,
        /// the Installed-apps entry and the install folder (after this exe has exited). Settings in %APPDATA%\WinDock
        /// are kept. With /ui (used by Settings > Apps) it asks first and says when it is done.</summary>
        public static int Uninstall(string[] args)
        {
            bool ui = HasArg(args, "ui");
            if (ui && MessageBox.Show("Remove WinDock from this PC?\n\nThe Windows taskbar comes back. Your dock settings are kept in %APPDATA%\\WinDock.",
                    "Uninstall WinDock", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return 1;
            try
            {
                Program.QuitRunning(8000);
                Taskbar.Restore();
                DockWindow.RemoveStaleAppBar();
                SetStartAtLogin(false);
                if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
                Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
                // The install folder holds this running exe, so a hidden cmd removes it once we have exited.
                if (Directory.Exists(InstallDir))
                    Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"" + InstallDir + "\"")
                    { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
                Console.WriteLine("WinDock stopped; taskbar restored; autostart, Start Menu shortcut and program files removed.");
                if (ui) MessageBox.Show("WinDock has been removed.", "Uninstall WinDock", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Uninstall problem: " + ex.Message);
                if (ui) MessageBox.Show("WinDock could not be removed completely:\n" + ex.Message, "Uninstall WinDock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
        }
    }
}
