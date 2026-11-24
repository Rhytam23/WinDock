// WinDock - the dock's right-click menus (SPEC 2.2).
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace WinDock
{
    public partial class DockWindow
    {
        void ShowMenu(ContextMenuStrip m, DockItem it)
        {
            if (Previews != null) Previews.Hide();
            MenuOpen = true;
            Point anchor;
            if (it != null) { var r = IconRect(it); anchor = new Point((int)it.CurX, (int)(r.Top - 6 * dpiScale)); }
            else { var c = Native.CursorPos(); anchor = new Point(c.X, (int)(barRect.Top - 6 * dpiScale)); }
            Menus.ShowAbove(m, anchor, () => { MenuOpen = false; Wake(); });
            Wake();
        }

        public void ShowItemMenu(DockItem it)
        {
            var m = Menus.Create();
            switch (it.Kind)
            {
                case DockKind.Launcher:
                    Menus.Item(m, "Show All Apps", App.OpenLauncher);
                    Menus.Sep(m);
                    Menus.Item(m, "Remove Launcher from Dock", () => { cfg.SetBool("dock-launcher", false); RebuildItems(); App.SettingsChanged(); });
                    break;

                case DockKind.Trash:
                    Menus.Item(m, "Open", () => Shellx.Open("shell:RecycleBinFolder"));
                    Menus.Sep(m);
                    Menus.Item(m.Items, "Empty Trash", () => { Shellx.EmptyTrash(Handle); trashFull = false; Wake(); }, trashFull, false);
                    break;

                case DockKind.MinWindow:
                    Menus.Item(m, "Restore", () => ClickItem(it));
                    Menus.Item(m, "Close Window", () => Apps.Close(it.MinHwnd));
                    break;

                case DockKind.Folder:
                    {
                        string path = it.Target;
                        Menus.Item(m, "Open", () => Shellx.Open(path));
                        Menus.Sep(m);
                        var view = Menus.Sub(m, "View Content As");
                        string cur = Folders.View(path);
                        foreach (var v in new[] { "auto", "fan", "grid", "list" })
                        {
                            string vv = v;
                            Menus.Item(view.DropDownItems, v == "auto" ? "Automatic" : char.ToUpper(v[0]) + v.Substring(1), () => DockState.Set("view|" + path, vv == "auto" ? null : vv), true, cur == v);
                        }
                        var sort = Menus.Sub(m, "Sort By");
                        string cs = Folders.Sort(path);
                        foreach (var kv in new[] { new[] { "name", "Name" }, new[] { "date", "Date Modified" }, new[] { "kind", "Kind" } })
                        {
                            string key = kv[0];
                            Menus.Item(sort.DropDownItems, kv[1], () => DockState.Set("sort|" + path, key), true, cs == key);
                        }
                        var tag = Menus.Sub(m, "Colour Tag");
                        string ct = DockState.Get("tag|" + path, "none");
                        foreach (var t in Glyphs.TagNames)
                        {
                            string tt = t;
                            var mi = Menus.Item(tag.DropDownItems, char.ToUpper(t[0]) + t.Substring(1), () => { DockState.Set("tag|" + path, tt == "none" ? null : tt); Wake(); }, true, ct == t);
                            if (t != "none")
                            {
                                var bmp = new Bitmap(12, 12);
                                using (var g = Graphics.FromImage(bmp)) { g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; using (var b = new SolidBrush(Glyphs.TagColor(t))) g.FillEllipse(b, 1, 1, 10, 10); }
                                mi.Image = bmp;
                            }
                        }
                        Menus.Sep(m);
                        Menus.Item(m, "Show in Explorer", () => Shellx.ShowInExplorer(path));
                        Menus.Item(m, "Remove from Dock", () => TogglePin(it));
                        break;
                    }

                case DockKind.File:
                    Menus.Item(m, "Open", () => Shellx.Open(it.Target));
                    Menus.Item(m, "Show in Explorer", () => Shellx.ShowInExplorer(it.Target));
                    Menus.Sep(m);
                    Menus.Item(m, "Remove from Dock", () => TogglePin(it));
                    break;

                case DockKind.Website:
                    Menus.Item(m, "Open", () => Shellx.Open(it.Target));
                    Menus.Item(m, "Copy Address", () => { try { Clipboard.SetText(it.Target); } catch { } });
                    Menus.Sep(m);
                    Menus.Item(m, "Remove from Dock", () => TogglePin(it));
                    break;

                default: // apps
                    {
                        if (it.Running)
                        {
                            foreach (var w in it.Windows.Take(10))
                            {
                                var ww = w;
                                string title = string.IsNullOrEmpty(w.Title) ? it.Label : w.Title;
                                if (title.Length > 60) title = title.Substring(0, 57) + "...";
                                Menus.Item(m.Items, title, () => ActivateWindow(ww.Hwnd, it), true, ww.Hwnd == Apps.ForegroundRoot());
                            }
                            Menus.Sep(m);
                            if (it.ProgramPath != null || it.Target != null)
                                Menus.Item(m, "New Window", () => { it.Launch(); });
                        }
                        else Menus.Item(m, "Open", () => { it.LaunchStart = clock.Elapsed.TotalSeconds; it.Launch(); Wake(); });
                        string show = it.ProgramPath ?? (it.Target != null && File.Exists(it.Target) ? it.Target : null);
                        if (show != null) Menus.Item(m, "Show in Explorer", () => Shellx.ShowInExplorer(show));
                        Menus.Sep(m);
                        if (it.Pin != null) Menus.Item(m, "Remove from Dock", () => TogglePin(it));
                        else if (it.Target != null) Menus.Item(m, "Keep in Dock", () => TogglePin(it));
                        if (it.Running)
                        {
                            Menus.Sep(m);
                            Menus.Item(m, it.Windows.Count > 1 ? "Quit (close " + it.Windows.Count + " windows)" : "Quit", () => { foreach (var w in it.Windows) Apps.Close(w.Hwnd); });
                        }
                        break;
                    }
            }
            ShowMenu(m, it);
        }

        public void ShowDockMenu(Point at)
        {
            var m = Menus.Create();
            Menus.Item(m, "Add Folder to Dock…", AddFolderDialog);
            Menus.Item(m, "Add App or File to Dock…", AddFileDialog);
            Menus.Item(m, "Add Website to Dock…", AddWebsiteDialog);
            Menus.Sep(m);
            bool hiding = cfg.GetBool("dock-autohide", false);
            Menus.Item(m, hiding ? "Turn Hiding Off" : "Turn Hiding On", () => { SetAutoHide(!hiding); App.SettingsChanged(); });
            Menus.Item(m, "Show All Apps", App.OpenLauncher);
            Menus.Sep(m);
            Menus.Item(m, "Dock Settings…", () => App.ShowSettings("dock"));
            Menus.Item(m, "Quit WinDock", App.Quit);
            ShowMenu(m, null);
        }

        public void AddFolderDialog()
        {
            using (var d = new FolderBrowserDialog { Description = "Choose a folder to add to the dock", ShowNewFolderButton = false })
                if (d.ShowDialog() == DialogResult.OK) AddPin(d.SelectedPath, null);
        }

        public void AddFileDialog()
        {
            using (var d = new OpenFileDialog { Title = "Choose an app or file to add to the dock", Filter = "Apps and shortcuts (*.exe;*.lnk)|*.exe;*.lnk|All files (*.*)|*.*", DereferenceLinks = false })
            {
                try { d.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Programs); } catch { }
                if (d.ShowDialog() == DialogResult.OK) AddPin(d.FileName, null);
            }
        }

        public void AddWebsiteDialog()
        {
            string url = InputDialog.Ask("Add Website to Dock", "Address of the website (for example https://example.com):", "https://");
            if (string.IsNullOrWhiteSpace(url) || url.Trim() == "https://") return;
            url = url.Trim();
            if (!url.Contains("://")) url = "https://" + url;
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u) || (u.Scheme != "http" && u.Scheme != "https")) return;
            AddPin(url, u.Host.StartsWith("www.") ? u.Host.Substring(4) : u.Host);
        }
    }
}
