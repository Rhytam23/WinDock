// WinDock Setup - a small per-user installer. WinDock.exe is embedded as a resource; Setup unpacks it to a temp
// folder and runs "WinDock.exe /install", which copies it to %LOCALAPPDATA%\WinDock, adds the Start Menu shortcut,
// the start-at-sign-in entry and the Settings > Apps entry. No admin rights, nothing outside the user's profile.
//
//   WinDock-Setup.exe                  -> the setup window
//   WinDock-Setup.exe /S [/noautostart] [/nostart]   -> silent install (exit code 0 = success)
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

static class Setup
{
    [STAThread]
    static int Main(string[] args)
    {
        bool silent = Has(args, "s") || Has(args, "silent") || Has(args, "quiet");
        if (silent)
        {
            string err;
            return Install(!Has(args, "noautostart"), !Has(args, "nostart"), out err);
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SetupForm());
        return 0;
    }

    static bool Has(string[] args, string name)
    {
        foreach (var a in args) if (a.TrimStart('/', '-').Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static string Version
    {
        get { var v = Assembly.GetExecutingAssembly().GetName().Version; return v.Major + "." + v.Minor + "." + v.Build; }
    }

    /// <summary>Unpacks WinDock.exe and runs its own installer. Returns WinDock's exit code (0 = installed).</summary>
    public static int Install(bool autostart, bool startNow, out string error)
    {
        error = null;
        string dir = Path.Combine(Path.GetTempPath(), "WinDock-Setup-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        try
        {
            Directory.CreateDirectory(dir);
            string exe = Path.Combine(dir, "WinDock.exe");
            using (var src = Assembly.GetExecutingAssembly().GetManifestResourceStream("WinDock.exe"))
            {
                if (src == null) { error = "This setup file is damaged (WinDock.exe is missing). Please download it again."; return 2; }
                using (var dst = File.Create(exe)) src.CopyTo(dst);
            }
            string a = "/install" + (autostart ? "" : " noautostart") + (startNow ? "" : " nostart");
            var p = Process.Start(new ProcessStartInfo(exe, a) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir });
            if (!p.WaitForExit(120000)) { error = "WinDock's installer did not finish in time."; return 3; }
            if (p.ExitCode != 0) error = "WinDock's installer reported a problem (code " + p.ExitCode + ").";
            return p.ExitCode;
        }
        catch (Win32Exception ex)
        {
            error = "Windows did not allow WinDock to run (" + ex.Message + ").\n\n" +
                    "If Smart App Control is on (Windows Security > App & browser control), it blocks apps that are not " +
                    "signed by a paid certificate, including WinDock. Your antivirus may also have blocked it.";
            return 4;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return 5;
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}

class SetupForm : Form
{
    readonly CheckBox autostart, startNow;
    readonly Button go, cancel;
    readonly Label status;
    bool done;

    public SetupForm()
    {
        Text = "WinDock " + Setup.Version + " Setup";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 330);
        BackColor = Color.White;
        Font = new Font("Segoe UI", 9.75f);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        var logo = new PictureBox { Location = new Point(28, 28), Size = new Size(64, 64), SizeMode = PictureBoxSizeMode.Zoom };
        try { logo.Image = new Icon(Icon, 64, 64).ToBitmap(); } catch { }
        var title = new Label { Text = "WinDock " + Setup.Version, Font = new Font("Segoe UI Semibold", 18f), AutoSize = true, Location = new Point(108, 28) };
        var sub = new Label
        {
            Text = "A Mac-style dock for Windows 11.\nInstalls for your account only. No admin rights needed.",
            AutoSize = true, Location = new Point(110, 68), ForeColor = Color.FromArgb(90, 90, 100)
        };
        var note = new Label
        {
            Text = "While WinDock runs it hides the Windows taskbar. Quit WinDock (right-click the dock) and the " +
                   "taskbar comes back. Remove it any time from Settings > Apps > Installed apps.",
            Location = new Point(30, 120), Size = new Size(460, 60), ForeColor = Color.FromArgb(90, 90, 100)
        };
        autostart = new CheckBox { Text = "Start WinDock when I sign in", Checked = true, AutoSize = true, Location = new Point(30, 186) };
        startNow = new CheckBox { Text = "Open WinDock when setup finishes", Checked = true, AutoSize = true, Location = new Point(30, 214) };
        var link = new LinkLabel { Text = "Free and open source (MIT licence)", AutoSize = true, Location = new Point(30, 290) };
        link.LinkClicked += (s, e) => { try { Process.Start("https://github.com/Rhytam23/WinDock"); } catch { } };
        status = new Label { AutoSize = false, Location = new Point(30, 246), Size = new Size(460, 40), ForeColor = Color.FromArgb(40, 40, 50) };
        go = new Button { Text = "Install", Size = new Size(96, 32), Location = new Point(298, 284) };
        cancel = new Button { Text = "Cancel", Size = new Size(96, 32), Location = new Point(404, 284), DialogResult = DialogResult.Cancel };
        go.Click += (s, e) => OnGo();
        AcceptButton = go; CancelButton = cancel;
        Controls.AddRange(new Control[] { logo, title, sub, note, autostart, startNow, status, link, go, cancel });
    }

    void OnGo()
    {
        if (done) { Close(); return; }
        go.Enabled = cancel.Enabled = autostart.Enabled = startNow.Enabled = false;
        status.Text = "Installing...";
        Cursor = Cursors.WaitCursor;
        Refresh();
        string error;
        int code = Setup.Install(autostart.Checked, startNow.Checked, out error);
        Cursor = Cursors.Default;
        if (code == 0)
        {
            done = true;
            status.Text = "WinDock is installed. You'll find it in the Start menu.";
            go.Text = "Finish"; go.Enabled = true;
            cancel.Visible = false;
        }
        else
        {
            status.Text = "Setup could not finish.";
            MessageBox.Show(this, error ?? "Unknown problem.", "WinDock Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            go.Text = "Try again";
            go.Enabled = cancel.Enabled = autostart.Enabled = startNow.Enabled = true;
        }
    }
}
