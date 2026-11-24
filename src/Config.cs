// WinDock - the plain-text configuration file (%APPDATA%\WinDock\windock.txt), see SPEC section 13.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace WinDock
{
    public class PinEntry
    {
        public string Label;   // may be null: derived from the target
        public string Target;
        public bool RightSide;

        public PinEntry(string label, string target, bool right) { Label = label; Target = target; RightSide = right; }

        public bool IsWebsite
        {
            get { return Target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || Target.StartsWith("https://", StringComparison.OrdinalIgnoreCase); }
        }
        public bool IsFolder
        {
            get { try { return !IsWebsite && Target.IndexOf(':') > 1 == false && Directory.Exists(Environment.ExpandEnvironmentVariables(Target)); } catch { return false; } }
        }
        public string ExpandedTarget { get { return Environment.ExpandEnvironmentVariables(Target); } }

        public string ToLine()
        {
            if (string.IsNullOrEmpty(Label)) return Target;
            return Label + " | " + Target;
        }
    }

    public class Config
    {
        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinDock"); }
        }
        public static string FilePath { get { return Path.Combine(Dir, "windock.txt"); } }
        public static string StatePath { get { return Path.Combine(Dir, "dockstate.txt"); } }
        public static string CacheDir { get { return Path.Combine(Dir, "cache"); } }

        public readonly List<PinEntry> Pins = new List<PinEntry>();
        // single-valued switches, in file order
        readonly List<KeyValuePair<string, string>> values = new List<KeyValuePair<string, string>>();
        readonly List<string> unknownLines = new List<string>();
        public string LastSavedText = "";

        public event Action Changed;

        static readonly HashSet<string> Repeatable = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "windowbuttons exclude", "titlerow remove"
        };

        static readonly Regex SettingLine = new Regex(@"^([a-z][a-z0-9\-]*(?: [a-z][a-z0-9\-]*)?)\s*=\s*(.*)$", RegexOptions.IgnoreCase);

        public static Config Load()
        {
            var c = new Config();
            try
            {
                Directory.CreateDirectory(Dir);
                if (!File.Exists(FilePath))
                {
                    c.SetDefaultPins();
                    c.Save();
                }
                else
                {
                    string text = File.ReadAllText(FilePath, Encoding.UTF8);
                    c.Parse(text);
                    c.LastSavedText = text;
                }
            }
            catch (Exception ex) { Log.Write("config load: " + ex.Message); }
            return c;
        }

        public void Parse(string text)
        {
            Pins.Clear(); values.Clear(); unknownLines.Clear();
            bool right = false;
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                string line = StripComment(raw).Trim();
                if (line.Length == 0) continue;
                if (line == "---") { right = true; continue; }
                var m = SettingLine.Match(line);
                if (m.Success && line.IndexOf(":\\", StringComparison.Ordinal) < 0 && !line.Contains("://"))
                {
                    string key = m.Groups[1].Value.ToLowerInvariant().Trim();
                    values.Add(new KeyValuePair<string, string>(key, m.Groups[2].Value.Trim()));
                    continue;
                }
                if (LooksLikeTarget(line))
                {
                    string label = null, target = line;
                    int bar = line.IndexOf(" | ", StringComparison.Ordinal);
                    if (bar < 0) bar = line.IndexOf('|');
                    if (bar > 0)
                    {
                        label = line.Substring(0, bar).Trim();
                        target = line.Substring(bar + 1).Trim().TrimStart('|').Trim();
                    }
                    Pins.Add(new PinEntry(label, target, right));
                    continue;
                }
                unknownLines.Add(raw.TrimEnd());
            }
        }

        static bool LooksLikeTarget(string line)
        {
            string t = line;
            int bar = line.IndexOf('|');
            if (bar >= 0) t = line.Substring(bar + 1).Trim();
            if (t.Length == 0) return false;
            if (t.Contains(":\\") || t.StartsWith("\\\\") || t.Contains("://") || t.StartsWith("%")) return true;
            if (t.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("ms-", StringComparison.OrdinalIgnoreCase)) return true;
            if (t.Contains(":")) return true; // other URI schemes, e.g. mailto:
            return false;
        }

        static string StripComment(string line)
        {
            // '#' starts a comment at the start of a line or after whitespace (so URLs with '#' survive)
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '#' && (i == 0 || char.IsWhiteSpace(line[i - 1]))) return line.Substring(0, i);
            }
            return line;
        }

        void SetDefaultPins()
        {
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            Pins.Add(new PinEntry("File Explorer", Path.Combine(win, "explorer.exe"), false));
            string edge = FindFirst(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"));
            string chrome = FindFirst(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe"));
            if (chrome != null) Pins.Add(new PinEntry("Google Chrome", chrome, false));
            else if (edge != null) Pins.Add(new PinEntry("Microsoft Edge", edge, false));
            Pins.Add(new PinEntry("Notepad", Path.Combine(win, "notepad.exe"), false));
            Pins.Add(new PinEntry("Calculator", @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", false));
            Pins.Add(new PinEntry("Terminal", @"shell:AppsFolder\Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", false));
            Pins.Add(new PinEntry("Settings", @"ms-settings:", false));
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Pins.Add(new PinEntry("Downloads", downloads, true));
            Pins.Add(new PinEntry("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), true));
        }

        static string FindFirst(params string[] paths)
        {
            foreach (var p in paths) if (File.Exists(p)) return p;
            return null;
        }

        // ---- typed access ----
        public string Get(string key, string def)
        {
            for (int i = values.Count - 1; i >= 0; i--)
                if (values[i].Key == key) return values[i].Value;
            return def;
        }
        public bool GetBool(string key, bool def)
        {
            string v = Get(key, null);
            if (v == null) return def;
            v = v.ToLowerInvariant();
            return v == "on" || v == "true" || v == "yes" || v == "1";
        }
        public int GetInt(string key, int def, int min, int max)
        {
            int n;
            if (!int.TryParse(Get(key, ""), out n)) return def;
            return Math.Max(min, Math.Min(max, n));
        }
        public List<string> GetAll(string key)
        {
            return values.Where(kv => kv.Key == key).Select(kv => kv.Value).ToList();
        }

        public void Set(string key, string value)
        {
            int idx = values.FindIndex(kv => kv.Key == key);
            values.RemoveAll(kv => kv.Key == key);
            var kvp = new KeyValuePair<string, string>(key, value);
            if (idx >= 0 && idx <= values.Count) values.Insert(idx, kvp); else values.Add(kvp);
            Save();
        }
        /// <summary>Change a value without writing the file yet (sliders being dragged); call Save() afterwards.</summary>
        public void SetQuiet(string key, string value)
        {
            int idx = values.FindIndex(kv => kv.Key == key);
            var kvp = new KeyValuePair<string, string>(key, value);
            if (idx >= 0) values[idx] = kvp; else values.Add(kvp);
        }
        public void SetIntQuiet(string key, int v) { SetQuiet(key, v.ToString()); }
        public void SetBool(string key, bool v) { Set(key, v ? "on" : "off"); }
        public void SetInt(string key, int v) { Set(key, v.ToString()); }

        public void NotifyChanged() { if (Changed != null) Changed(); }

        // ---- saving ----
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var sb = new StringBuilder();
                sb.AppendLine("# WinDock configuration. One entry per line; '#' starts a comment.");
                sb.AppendLine("# Dock items, in order:  Label | target   (an app, folder, file, website or Settings page)");
                sb.AppendLine("# A line with --- starts the right-hand section of the dock (folders, files).");
                sb.AppendLine();
                foreach (var p in Pins.Where(p => !p.RightSide)) sb.AppendLine(p.ToLine());
                sb.AppendLine("---");
                foreach (var p in Pins.Where(p => p.RightSide)) sb.AppendLine(p.ToLine());
                sb.AppendLine();
                sb.AppendLine("# Switches");
                var written = new HashSet<string>();
                foreach (var kv in values)
                {
                    if (Repeatable.Contains(kv.Key)) { sb.AppendLine(kv.Key + " = " + kv.Value); continue; }
                    if (written.Contains(kv.Key)) continue;
                    written.Add(kv.Key);
                    sb.AppendLine(kv.Key + " = " + Get(kv.Key, kv.Value));
                }
                if (unknownLines.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("# Lines WinDock did not understand (kept as they were)");
                    foreach (var l in unknownLines) sb.AppendLine(l);
                }
                string text = sb.ToString();
                LastSavedText = text;
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, text, new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath);
            }
            catch (Exception ex) { Log.Write("config save: " + ex.Message); }
        }
    }

    /// <summary>Small key/value store for state WinDock keeps for itself (folder views, colour tags).</summary>
    public static class DockState
    {
        static Dictionary<string, string> map;

        static void Ensure()
        {
            if (map != null) return;
            map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(Config.StatePath))
                    foreach (var line in File.ReadAllLines(Config.StatePath, Encoding.UTF8))
                    {
                        int i = line.IndexOf('\t');
                        if (i > 0) map[line.Substring(0, i)] = line.Substring(i + 1);
                    }
            }
            catch { }
        }

        public static string Get(string key, string def)
        {
            Ensure();
            string v;
            return map.TryGetValue(key, out v) ? v : def;
        }

        public static void Set(string key, string value)
        {
            Ensure();
            if (value == null) map.Remove(key); else map[key] = value;
            try
            {
                Directory.CreateDirectory(Config.Dir);
                File.WriteAllLines(Config.StatePath, map.Select(kv => kv.Key + "\t" + kv.Value).ToArray(), new UTF8Encoding(false));
            }
            catch { }
        }
    }

    public static class Log
    {
        static readonly object gate = new object();
        public static void Write(string msg)
        {
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(Config.Dir);
                    string path = Path.Combine(Config.Dir, "windock.log");
                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > 256 * 1024) fi.Delete();
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
