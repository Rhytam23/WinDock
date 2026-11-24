// WinDock - entry point, single instance, command-line switches, safe quit (SPEC section 1).
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("WinDock")]
[assembly: System.Reflection.AssemblyProduct("WinDock")]
[assembly: System.Reflection.AssemblyDescription("A dock and desktop shell for Windows")]
[assembly: System.Reflection.AssemblyCopyright("MIT licence")]
[assembly: System.Reflection.AssemblyVersion("0.2.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.2.0.0")]

namespace WinDock
{
    public static class Program
    {
        public const string MutexName = "Local\\WinDock.SingleInstance";
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

        [STAThread]
        public static int Main(string[] args)
        {
            string sw = args.Length > 0 ? args[0].ToLowerInvariant().TrimStart('/', '-') : "";
            if (sw == "install" || sw == "uninstall" || sw == "restore" || sw == "quit") AttachConsole(-1);
            switch (sw)
            {
                case "restore":
                    Taskbar.Restore();
                    DockWindow.RemoveStaleAppBar();
                    return 0;
                case "quit":
                    return QuitRunning(8000) ? 0 : 1;
                case "install":
                    Application.EnableVisualStyles();
                    return Installer.Install(args);
                case "uninstall":
                    Application.EnableVisualStyles();
                    return Installer.Uninstall(args);
            }

            bool created;
            var mutex = new Mutex(true, MutexName, out created);
            if (!created)
            {
                // already running: show its Settings window instead of starting twice
                PostToRunning("WinDock.ShowSettings");
                return 0;
            }
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => Log.Write("error: " + e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    Log.Write("fatal: " + e.ExceptionObject);
                    Taskbar.Restore();
                    DockWindow.RemoveStaleAppBar();
                };
                App.Run(sw == "settings");
            }
            finally
            {
                Taskbar.Restore();
                mutex.ReleaseMutex();
                GC.KeepAlive(mutex);
            }
            return 0;
        }

        /// <summary>Posts a WinDock message to the running copy's hidden message window (title "WinDock", process WinDock.exe).</summary>
        static void PostToRunning(string message)
        {
            int msg = Native.RegisterWindowMessage(message);
            int me = Process.GetCurrentProcess().Id;
            bool sent = false;
            Native.EnumWindows((h, l) =>
            {
                if (Native.WindowText(h) != "WinDock" || !Native.ClassName(h).StartsWith("WindowsForms10")) return true;
                int pid; Native.GetWindowThreadProcessId(h, out pid);
                if (pid == me) return true;
                try { if (!Process.GetProcessById(pid).ProcessName.Equals("WinDock", StringComparison.OrdinalIgnoreCase)) return true; } catch { return true; }
                Native.AllowSetForegroundWindow(pid); // so the running copy may bring its Settings window to the front
                Native.PostMessage(h, msg, IntPtr.Zero, IntPtr.Zero);
                sent = true;
                return true;
            }, IntPtr.Zero);
            if (!sent) Native.PostMessage(Native.HWND_BROADCAST, msg, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>Asks a running WinDock to quit and waits until it has (used by Install/Uninstall).</summary>
        public static bool QuitRunning(int timeoutMs)
        {
            Mutex m;
            if (!Mutex.TryOpenExisting(MutexName, out m)) return true;
            m.Dispose();
            PostToRunning("WinDock.Quit");
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                Thread.Sleep(150);
                if (!Mutex.TryOpenExisting(MutexName, out m)) return true;
                m.Dispose();
            }
            return false;
        }
    }

    /// <summary>The running app: config, dock, Settings, tray icon and the hidden message window.</summary>
    public static class App
    {
        public static Config Cfg;
        public static DockWindow Dock;
        static SettingsWindow settings;
        static NotifyIcon tray;
        static MessageWindow messages;
        static FileSystemWatcher watcher;
        static System.Windows.Forms.Timer reloadTimer;
        static bool quitting;

        public static string Version { get { return typeof(App).Assembly.GetName().Version.ToString(3); } }

        public static void Run(bool openSettings)
        {
            Cfg = Config.Load();
            Theme.Cfg = Cfg;
            messages = new MessageWindow();   // also installs the UI synchronisation context
            IconLoader.Start();
            ApplyTaskbar();

            Dock = new DockWindow(Cfg);
            Dock.Start();

            tray = new NotifyIcon { Icon = Glyphs.MakeIcon(32), Text = "WinDock", Visible = true };
            var menu = Menus.Create();
            Menus.Item(menu, "WinDock Settings…", () => ShowSettings(null));
            Menus.Sep(menu);
            Menus.Item(menu, "Quit WinDock", Quit);
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowSettings(null); };

            WatchConfig();
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += (s, e) => { if (e.Category == Microsoft.Win32.UserPreferenceCategory.General) ThemeChanged(); };
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) => { if (Dock != null) Dock.ApplySettings(); };
            Microsoft.Win32.SystemEvents.SessionEnding += (s, e) => { Taskbar.Restore(); };
            Application.ApplicationExit += (s, e) => Cleanup();
            if (openSettings) ShowSettings(null);
            Application.Run();
        }

        public static void ApplyTaskbar()
        {
            if (Cfg.GetBool("hide-taskbar", true)) Taskbar.Hide(); else Taskbar.Restore();
            if (Dock != null) Dock.ApplySettings();
        }

        public static void ThemeChanged()
        {
            Theme.Reset();
            if (Dock != null) { Dock.ReloadIcons(); Dock.Wake(); }
            if (settings != null && !settings.IsDisposed) settings.ThemeChanged();
        }

        public static void SettingsChanged()
        {
            if (settings != null && !settings.IsDisposed) settings.Rebuild();
        }

        public static void ShowSettings(string page)
        {
            if (settings == null || settings.IsDisposed)
            {
                settings = new SettingsWindow(Pages.All());
                settings.Navigate(page ?? "dock");
                settings.Show();
            }
            else if (page != null) settings.Navigate(page);
            if (settings.WindowState == FormWindowState.Minimized) settings.WindowState = FormWindowState.Normal;
            settings.Activate();
            Apps.Activate(settings.Handle);
        }

        /// <summary>Until the Launcher exists (milestone 4) this opens the Windows Start menu.</summary>
        public static void OpenLauncher()
        {
            Native.KeyCombo(0x11, 0x1B); // Ctrl+Esc
        }

        static void WatchConfig()
        {
            try
            {
                reloadTimer = new System.Windows.Forms.Timer { Interval = 400 };
                reloadTimer.Tick += (s, e) =>
                {
                    reloadTimer.Stop();
                    try
                    {
                        string text = File.ReadAllText(Config.FilePath, System.Text.Encoding.UTF8);
                        if (text == Cfg.LastSavedText) return;
                        Cfg.Parse(text);
                        Cfg.LastSavedText = text;
                        Log.Write("config file changed: reloaded dock items");
                        Dock.ApplySettings();
                        Dock.ReloadPins();
                        SettingsChanged();
                    }
                    catch (Exception ex) { Log.Write("config reload: " + ex.Message); }
                };
                watcher = new FileSystemWatcher(Config.Dir, "windock.txt") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
                FileSystemEventHandler h = (s, e) => messages.BeginInvoke(new Action(() => { reloadTimer.Stop(); reloadTimer.Start(); }));
                watcher.Changed += h; watcher.Created += h;
                watcher.Renamed += (s, e) => h(s, e);
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) { Log.Write("watch config: " + ex.Message); }
        }

        public static void Quit()
        {
            if (quitting) return;
            quitting = true;
            Cleanup();
            Application.Exit();
        }

        public static void Restart()
        {
            string exe = Application.ExecutablePath;
            Quit();
            // the new copy waits for this one to let go of the single-instance lock
            Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & start \"\" \"" + exe + "\"") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        }

        static bool cleaned;
        static void Cleanup()
        {
            if (cleaned) return;
            cleaned = true;
            try { if (watcher != null) watcher.EnableRaisingEvents = false; } catch { }
            try { if (Dock != null) Dock.Shutdown(); } catch (Exception ex) { Log.Write("shutdown: " + ex.Message); }
            Taskbar.Restore();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            if (settings != null && !settings.IsDisposed) settings.Close();
        }

        /// <summary>Hidden top-level window: receives "show settings" / "quit" from other copies and Explorer restarts.</summary>
        class MessageWindow : Form
        {
            readonly int msgShow = Native.RegisterWindowMessage("WinDock.ShowSettings");
            readonly int msgQuit = Native.RegisterWindowMessage("WinDock.Quit");
            readonly int msgTaskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");

            public MessageWindow()
            {
                Text = "WinDock";
                ShowInTaskbar = false;
                FormBorderStyle = FormBorderStyle.None;
                Size = new System.Drawing.Size(1, 1);
                CreateHandle();
            }

            protected override CreateParams CreateParams
            {
                get { var cp = base.CreateParams; cp.ExStyle |= (int)Native.WS_EX_TOOLWINDOW; return cp; }
            }

            protected override void SetVisibleCore(bool value) { base.SetVisibleCore(false); }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == msgShow && msgShow != 0) { ShowSettings(null); return; }
                if (m.Msg == msgQuit && msgQuit != 0) { Quit(); return; }
                if (m.Msg == msgTaskbarCreated && msgTaskbarCreated != 0)
                {
                    // Explorer restarted: hide its new taskbar again and re-reserve the dock's space
                    Log.Write("Explorer restarted");
                    if (Cfg.GetBool("hide-taskbar", true)) { Taskbar.Hide(); Taskbar.KeepHidden(); }
                    if (App.Dock != null) App.Dock.ApplySettings();
                }
                if (m.Msg == Native.WM_ENDSESSION && m.WParam != IntPtr.Zero) Taskbar.Restore();
                base.WndProc(ref m);
            }
        }
    }
}
