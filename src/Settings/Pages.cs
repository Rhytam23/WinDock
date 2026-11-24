// WinDock - what is on each Settings page (SPEC 11.2). Pages for later milestones say so plainly.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Forms;

namespace WinDock
{
    public static class Pages
    {
        static Config Cfg { get { return App.Cfg; } }

        static ToggleRow Toggle(string label, string key, bool def, Action after)
        {
            return new ToggleRow(label, () => Cfg.GetBool(key, def), v => { Cfg.SetBool(key, v); if (after != null) after(); });
        }

        static Action DockChanged { get { return () => { if (App.Dock != null) App.Dock.ApplySettings(); }; } }

        public static List<SettingsPage> All()
        {
            var list = new List<SettingsPage>();
            Func<string, string, string, Color, string, Func<List<Section>>, SettingsPage> add = (id, title, glyph, color, group, build) =>
            {
                var p = new SettingsPage { Id = id, Title = title, Glyph = glyph, Color = color, Group = group, Build = build };
                list.Add(p);
                return p;
            };
            Color grey = Color.FromArgb(142, 142, 147), blue = Color.FromArgb(10, 132, 255), indigo = Color.FromArgb(94, 92, 230),
                green = Color.FromArgb(48, 209, 88), orange = Color.FromArgb(255, 159, 10), red = Color.FromArgb(255, 69, 58),
                purple = Color.FromArgb(191, 90, 242), teal = Color.FromArgb(48, 176, 199), pink = Color.FromArgb(255, 55, 95);

            add("general", "General", "gear", grey, "WINDOCK", General);
            add("appearance", "Appearance", "appearance", Color.FromArgb(60, 60, 66), "WINDOCK", Appearance);
            add("dock", "Dock", "dock", Color.FromArgb(28, 28, 30), "WINDOCK", Dock);
            add("menubar", "Menu Bar & Pill", "menubar", blue, "WINDOCK", () => Later("The menu bar (system menu, app menus, status readouts, tray icons) and the Pill", 3));
            add("launcher", "Launcher", "grid", indigo, "WINDOCK", () => Later("The Launcher (all apps), Search and the Windows-key behaviour", 4));
            add("icons", "Icons", "star", orange, "WINDOCK", () => Later("Icon styles and custom icon packs", 9));
            add("buttons", "Window Buttons", "buttons", red, "WINDOCK", () => Later("The red / yellow / green buttons and the title row on every window", 5));
            add("gestures", "Gestures", "swipe", teal, "WINDOCK", () => Later("Touchpad swipes for the Overview", 7));

            add("sys-display", "Display & Sound", "display", blue, "SYSTEM", null).OpensUri = "ms-settings:display";
            add("sys-network", "Network & Bluetooth", "wifi", blue, "SYSTEM", null).OpensUri = "ms-settings:network";
            add("sys-power", "Power & Battery", "battery", green, "SYSTEM", Power);
            add("sys-personal", "Personalization", "brush", pink, "SYSTEM", null).OpensUri = "ms-settings:personalization";
            add("sys-notify", "Notifications & Focus", "bell", red, "SYSTEM", null).OpensUri = "ms-settings:notifications";
            add("sys-privacy", "Privacy & Security", "lock", blue, "SYSTEM", null).OpensUri = "ms-settings:privacy";
            add("sys-apps", "Apps", "apps", indigo, "SYSTEM", null).OpensUri = "ms-settings:appsfeatures";
            add("sys-accounts", "Accounts", "person", orange, "SYSTEM", null).OpensUri = "ms-settings:yourinfo";
            add("sys-time", "Time & Language", "clock", blue, "SYSTEM", null).OpensUri = "ms-settings:dateandtime";
            add("sys-update", "Windows Update", "update", blue, "SYSTEM", null).OpensUri = "ms-settings:windowsupdate";
            add("sys-explorer", "File Explorer", "folder", blue, "SYSTEM", null).OpensUri = "shell:::{6DFD7C5C-2451-11d3-A299-00C04F8EF6AF}";
            add("sys-about", "About This PC", "info", grey, "SYSTEM", About);
            foreach (var p in list) if (p.Build == null) p.Build = () => new List<Section>();
            return list;
        }

        static List<Section> Later(string what, int milestone)
        {
            return new List<Section>
            {
                new Section(null).Add(new InfoRow("Status", () => "Not built yet"))
                    .Note(what + " will arrive in a later version of WinDock (milestone " + milestone + "). The settings for it will appear here then.")
            };
        }

        // ---------------------------------------------------------------- General

        static List<Section> General()
        {
            return new List<Section>
            {
                new Section(null)
                    .Add(new ToggleRow("Open WinDock at login", Installer.StartsAtLogin, Installer.SetStartAtLogin))
                    .Note("Starts WinDock when you sign in to Windows."),
                new Section("WinDock")
                    .Add(new LinkRow("Restart WinDock", App.Restart))
                    .Add(new LinkRow("Quit WinDock", App.Quit))
                    .Note("Quitting puts the Windows taskbar back. If WinDock ever stops unexpectedly, run WinDock.exe /restore to bring the taskbar back."),
                new Section("Configuration")
                    .Add(new LinkRow("Open configuration file", () => Process.Start(new ProcessStartInfo("notepad.exe", "\"" + Config.FilePath + "\"") { UseShellExecute = true })))
                    .Add(new LinkRow("Open data folder", () => Shellx.Open(Config.Dir)))
                    .Add(new LinkRow("Back up settings…", Backup))
                    .Add(new LinkRow("Restore settings…", RestoreBackup))
                    .Note("Settings live in windock.txt in the data folder. Dock items you change there apply right away; other lines apply after a restart."),
            };
        }

        static void Backup()
        {
            using (var d = new SaveFileDialog { Title = "Back up WinDock settings", Filter = "Zip archive (*.zip)|*.zip", FileName = "WinDock settings " + DateTime.Now.ToString("yyyy-MM-dd") + ".zip" })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                try
                {
                    if (File.Exists(d.FileName)) File.Delete(d.FileName);
                    using (var zip = ZipFile.Open(d.FileName, ZipArchiveMode.Create))
                        foreach (var f in Directory.GetFiles(Config.Dir, "*.txt"))
                        {
                            string n = Path.GetFileName(f);
                            if (n == "taskbar-hidden.txt" || n == "appbar-hwnd.txt") continue;
                            zip.CreateEntryFromFile(f, n);
                        }
                }
                catch (Exception ex) { MessageBox.Show("The backup could not be written:\n" + ex.Message, "WinDock"); }
            }
        }

        static void RestoreBackup()
        {
            using (var d = new OpenFileDialog { Title = "Restore WinDock settings", Filter = "Zip archive (*.zip)|*.zip" })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                if (MessageBox.Show("Replace your current WinDock settings with the ones in this backup? WinDock restarts afterwards.", "WinDock", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                try
                {
                    using (var zip = ZipFile.OpenRead(d.FileName))
                        foreach (var e in zip.Entries)
                        {
                            if (!e.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || e.Name.Contains("..") || e.Name != e.FullName) continue;
                            e.ExtractToFile(Path.Combine(Config.Dir, e.Name), true);
                        }
                    App.Restart();
                }
                catch (Exception ex) { MessageBox.Show("The backup could not be restored:\n" + ex.Message, "WinDock"); }
            }
        }

        // ---------------------------------------------------------------- Appearance

        static List<Section> Appearance()
        {
            var fonts = new List<string> { "" };
            var labels = new List<string> { "System (Segoe UI)" };
            using (var ifc = new System.Drawing.Text.InstalledFontCollection())
                foreach (var name in new[] { "Segoe UI Variable Text", "Segoe UI", "Inter", "Calibri", "Arial", "Verdana", "Tahoma" })
                    if (ifc.Families.Any(f => f.Name == name)) { fonts.Add(name); labels.Add(name); }
            return new List<Section>
            {
                new Section(null)
                    .Add(new PickerRow("Appearance", new[] { "auto", "dark", "light" }, new[] { "Match Windows", "Dark", "Light" },
                        () => Cfg.Get("appearance", "auto"), v => { Cfg.Set("appearance", v); App.ThemeChanged(); }))
                    .Add(Toggle("Transparency effects", "transparency", true, () => App.ThemeChanged()))
                    .Add(new PickerRow("Font", fonts.ToArray(), labels.ToArray(), () => Cfg.Get("font", ""), v => { Cfg.Set("font", v); Theme.ResetFont(); App.ThemeChanged(); }))
                    .Note("Match Windows follows the light or dark mode set in Windows. Transparency also follows the Windows transparency setting."),
            };
        }

        // ---------------------------------------------------------------- Dock

        static List<Section> Dock()
        {
            var preview = new CustomRow(120, (g, r, m) =>
            {
                if (App.Dock == null) return;
                float scale = r.Height / 120f;
                int size = Cfg.GetInt("dock-size", 48, 24, 128);
                bool magnify = Cfg.GetBool("dock-magnification", true);
                float max = magnify ? 1f + Cfg.GetInt("dock-magnify", 50, 0, 100) / 100f * 1.2f : 1f;
                float hover = m.HasValue ? (m.Value.X - r.X) / r.Width : 0.38f;
                // the preview is drawn smaller than the real dock, but grows and magnifies the same way
                float shown = Math.Max(18f, Math.Min(44f, size * 0.55f));
                float maxShown = Math.Min(max, 96f / shown);
                using (var bmp = App.Dock.RenderPreview((int)r.Width, (int)r.Height, (int)shown, maxShown, hover, scale))
                    g.DrawImage(bmp, r.X, r.Y);
            });
            return new List<Section>
            {
                new Section("Dock")
                    .Add(preview)
                    .Add(new SliderRow("Size", 24, 128, () => Cfg.GetInt("dock-size", 48, 24, 128), v => { Cfg.SetIntQuiet("dock-size", v); DockChanged(); }, "Small", "Large"))
                    .Add(Toggle("Magnification", "dock-magnification", true, DockChanged))
                    .Add(new SliderRow("Magnification amount", 0, 100, () => Cfg.GetInt("dock-magnify", 50, 0, 100), v => { Cfg.SetIntQuiet("dock-magnify", v); DockChanged(); }, "Less", "More"))
                    .Note("Icons near the pointer grow as you move across the Dock. Move the pointer over the preview to try it."),
                new Section(null)
                    .Add(Toggle("Show Dock", "dock-show", true, DockChanged))
                    .Add(new ToggleRow("Automatically hide and show the Dock", () => Cfg.GetBool("dock-autohide", false), v => { if (App.Dock != null) App.Dock.SetAutoHide(v); else Cfg.SetBool("dock-autohide", v); }))
                    .Add(Toggle("Show Launcher icon in Dock", "dock-launcher", true, () => { if (App.Dock != null) App.Dock.RebuildItems(); }))
                    .Add(Toggle("Genie effect when minimising", "minimize-animation", true, null))
                    .Add(new PickerRow("Minimise effect", new[] { "genie", "scale", "fade" }, new[] { "Genie", "Scale", "Fade" }, () => Cfg.Get("minimize-effect", "genie"), v => Cfg.Set("minimize-effect", v)))
                    .Add(Toggle("Minimise windows into the Dock (as thumbnails)", "dock-minimize-thumbnails", false, DockChanged))
                    .Add(Toggle("Show window previews on hover", "dock-previews", true, null))
                    .Add(Toggle("Open folder fans on click instead of hover", "stacks-click", true, null))
                    .Add(new PickerRow("Folder contents show as", new[] { "fan", "grid", "list" }, new[] { "Fan", "Grid", "List" }, () => Cfg.Get("stack-view", "fan"), v => Cfg.Set("stack-view", v)))
                    .Add(Toggle("Show website icons (fetched once over the network)", "website-icons", true, () => { if (App.Dock != null) App.Dock.ReloadIcons(); }))
                    .Note("With hiding on, the Dock slides away and comes back when the pointer touches the bottom edge of the screen. The minimise effect plays when you minimise a window from the Dock."),
                new Section("Windows taskbar")
                    .Add(new ToggleRow("Hide the Windows taskbar while WinDock runs", () => Cfg.GetBool("hide-taskbar", true), v => { Cfg.SetBool("hide-taskbar", v); App.ApplyTaskbar(); }))
                    .Note("The taskbar always comes back when WinDock quits."),
                new Section("Dock items")
                    .Add(new LinkRow("Add folder…", () => { if (App.Dock != null) App.Dock.AddFolderDialog(); }))
                    .Add(new LinkRow("Add app or file…", () => { if (App.Dock != null) App.Dock.AddFileDialog(); }))
                    .Add(new LinkRow("Add website…", () => { if (App.Dock != null) App.Dock.AddWebsiteDialog(); }))
                    .Add(new LinkRow("Edit dock items", () => Process.Start(new ProcessStartInfo("notepad.exe", "\"" + Config.FilePath + "\"") { UseShellExecute = true })))
                    .Note("You can also drag apps, files and folders onto the Dock, drag icons to reorder them, and drag an icon off the Dock to remove it."),
            };
        }

        // ---------------------------------------------------------------- Power & Battery (read-only for now)

        static List<Section> Power()
        {
            var ps = SystemInformation.PowerStatus;
            Func<string> level = () =>
            {
                var s = SystemInformation.PowerStatus;
                if (s.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery)) return "No battery";
                return (int)Math.Round(s.BatteryLifePercent * 100) + "%";
            };
            Func<string> state = () =>
            {
                var s = SystemInformation.PowerStatus;
                if (s.PowerLineStatus == PowerLineStatus.Online) return s.BatteryChargeStatus.HasFlag(BatteryChargeStatus.Charging) ? "Charging" : "Plugged in";
                if (s.BatteryLifeRemaining > 0) { var t = TimeSpan.FromSeconds(s.BatteryLifeRemaining); return "On battery, " + (int)t.TotalHours + " h " + t.Minutes + " min left"; }
                return "On battery";
            };
            return new List<Section>
            {
                new Section("Battery")
                    .Add(new InfoRow("Battery level", level))
                    .Add(new InfoRow("Power source", state)),
                new Section(null)
                    .Add(new LinkRow("Power mode and battery saver", () => Shellx.Open("ms-settings:batterysaver")))
                    .Add(new LinkRow("Screen and sleep", () => Shellx.Open("ms-settings:powersleep")))
                    .Note("These open Windows Settings. Low-battery warnings arrive with the Pill in a later version of WinDock."),
            };
        }

        // ---------------------------------------------------------------- About This PC

        static string cpuName, memory;

        static List<Section> About()
        {
            if (cpuName == null)
            {
                cpuName = "";
                try
                {
                    using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                        if (k != null) cpuName = ((k.GetValue("ProcessorNameString") as string) ?? "").Trim();
                }
                catch { }
                try
                {
                    var ci = new Microsoft.VisualBasic.Devices.ComputerInfo();
                    memory = Math.Round(ci.TotalPhysicalMemory / 1024.0 / 1024 / 1024) + " GB";
                }
                catch { memory = ""; }
            }
            string winVer = "Windows";
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    string name = k.GetValue("ProductName") as string ?? "Windows";
                    int build = 0; int.TryParse(k.GetValue("CurrentBuild") as string, out build);
                    if (build >= 22000) name = name.Replace("Windows 10", "Windows 11");
                    string disp = k.GetValue("DisplayVersion") as string;
                    winVer = name + (disp != null ? " " + disp : "") + " (build " + build + ")";
                }
            }
            catch { }
            return new List<Section>
            {
                new Section(null)
                    .Add(new InfoRow("Device name", () => Environment.MachineName))
                    .Add(new InfoRow("Windows", () => winVer))
                    .Add(new InfoRow("Processor", () => cpuName))
                    .Add(new InfoRow("Memory", () => memory))
                    .Add(new InfoRow("WinDock", () => "Version " + App.Version)),
                new Section(null)
                    .Add(new LinkRow("Task Manager", () => Shellx.Open("taskmgr.exe")))
                    .Add(new LinkRow("Sleep", () => Application.SetSuspendState(PowerState.Suspend, false, false)))
                    .Add(new LinkRow("Lock Screen", () => Process.Start(new ProcessStartInfo("rundll32.exe", "user32.dll,LockWorkStation") { UseShellExecute = false })))
                    .Add(new LinkRow("Restart…", () => Confirm("Restart your PC now?", "Restart", "/r /t 0")))
                    .Add(new LinkRow("Shut Down…", () => Confirm("Shut down your PC now?", "Shut Down", "/s /t 0")))
                    .Note("Restart and Shut Down ask first."),
            };
        }

        static void Confirm(string question, string action, string args)
        {
            if (MessageBox.Show(question + "\n\nUnsaved work in open apps may be lost.", "WinDock - " + action, MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.OK)
                Process.Start(new ProcessStartInfo("shutdown.exe", args) { UseShellExecute = false, CreateNoWindow = true });
        }
    }
}
