// WinDock - the dock (SPEC section 2): layout, magnification, drawing and mouse handling.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinDock
{
    public partial class DockWindow : LayeredForm
    {
        readonly Config cfg;
        public readonly DockBackdrop Backdrop;
        public List<DockItem> Items = new List<DockItem>();

        DockItem launcherItem, trashItem, dividerItem;
        readonly Dictionary<string, DockItem> pinItems = new Dictionary<string, DockItem>();
        readonly Dictionary<string, DockItem> runningItems = new Dictionary<string, DockItem>();
        readonly Dictionary<IntPtr, DockItem> minItems = new Dictionary<IntPtr, DockItem>();
        readonly List<string> unpinnedOrder = new List<string>();
        List<AppWindow> lastWindows = new List<AppWindow>();

        readonly Timer animTimer = new Timer { Interval = 10 };
        readonly Timer slowTimer = new Timer { Interval = 1000 };
        readonly Timer edgeTimer = new Timer { Interval = 100 };
        readonly Timer refreshDebounce = new Timer { Interval = 120 };
        readonly Stopwatch clock = Stopwatch.StartNew();
        double lastFrame;

        // geometry (physical pixels, screen coordinates)
        Rectangle monitor;
        float dpiScale = 1f;
        int S;                 // base icon size
        float maxScale;
        float gap, padX, vpad, barH;
        int winH, winTop, dockBottom;
        RectangleF barRect;
        bool trashFull;
        int slowTicks;

        // hover / press / drag
        bool hovering;
        float mouseX;
        DockItem hoverItem, pressItem, dropTarget;
        double hoverSince;
        Point pressPoint;
        bool pressed, dragging, dragRemove, dragOutside;
        int dragInsert = -1;
        DragGhost ghost;
        public bool MenuOpen;

        // auto-hide and visibility
        float hideAmount;      // 0 = shown, 1 = fully hidden
        bool hideTarget;
        double leaveSince = -1;
        bool fullscreenHidden;
        bool appBarRegistered;
        public bool Suppressed { get { return !cfg.GetBool("dock-show", true) || fullscreenHidden; } }

        WinEventProc winEventProc;
        IntPtr hookFg, hookShow, hookMin;

        public PreviewPopup Previews;
        public StackPopup Stack;

        public DockWindow(Config cfg)
        {
            this.cfg = cfg;
            Text = "WinDock Dock";
            Backdrop = new DockBackdrop(this);
            AllowDrop = true;
            animTimer.Tick += (s, e) => Frame();
            slowTimer.Tick += (s, e) => SlowTick();
            edgeTimer.Tick += (s, e) => EdgeTick();
            refreshDebounce.Tick += (s, e) => { refreshDebounce.Stop(); RefreshRunning(); };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            winEventProc = OnWinEvent;
            uint flags = Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS;
            hookFg = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, winEventProc, 0, 0, flags);
            hookMin = Native.SetWinEventHook(Native.EVENT_SYSTEM_MINIMIZESTART, Native.EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, winEventProc, 0, 0, flags);
            hookShow = Native.SetWinEventHook(Native.EVENT_OBJECT_DESTROY, Native.EVENT_OBJECT_HIDE, IntPtr.Zero, winEventProc, 0, 0, flags);
        }

        void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != 0 || idChild != 0) return;
            if (evt == Native.EVENT_SYSTEM_MINIMIZESTART && cfg.GetBool("dock-minimize-thumbnails", false))
                Effects.CaptureForThumbnail(hwnd);
            if (evt == Native.EVENT_OBJECT_SHOW || evt == Native.EVENT_OBJECT_HIDE || evt == Native.EVENT_OBJECT_DESTROY)
            {
                if (Native.GetParent(hwnd) != IntPtr.Zero) return; // only top-level windows matter
            }
            if (!refreshDebounce.Enabled) refreshDebounce.Start();
        }

        // ---------------------------------------------------------------- startup / settings

        public void Start()
        {
            CreateHandle();
            Backdrop.CreateControl();
            ApplySettings();
            RebuildItems();
            RefreshRunning();
            slowTimer.Start();
            Wake();
        }

        /// <summary>Re-read every dock setting (called whenever Settings or the config file change).</summary>
        public void ApplySettings()
        {
            IntPtr hmon;
            var mi = Native.PrimaryMonitor(out hmon);
            monitor = mi.rcMonitor.ToRectangle();
            dpiScale = Native.MonitorScale(hmon);
            int size = cfg.GetInt("dock-size", 48, 24, 128);
            S = (int)Math.Round(size * dpiScale);
            bool magnify = cfg.GetBool("dock-magnification", true);
            int amount = cfg.GetInt("dock-magnify", 50, 0, 100);
            maxScale = magnify ? 1f + amount / 100f * 1.2f : 1f;
            gap = S * 0.14f;
            padX = S * 0.14f;
            vpad = Math.Max(4, S * 0.15f);
            barH = S + vpad * 2;
            winH = (int)Math.Ceiling(barH + S * (maxScale - 1) + S * 0.6f + 34 * dpiScale);
            UpdateAppBar();
            if (Items.Count > 0) RebuildItems();
            Wake();
        }

        public void ReloadPins() { pinItems.Clear(); RebuildItems(); RefreshRunning(); }

        // ---------------------------------------------------------------- work area (app bar)

        public const int AppBarMessage = 0x8000 + 0x57; // WM_APP + 'W'

        void UpdateAppBar()
        {
            bool want = !Suppressed && !cfg.GetBool("dock-autohide", false);
            var abd = new APPBARDATA(); abd.cbSize = Marshal.SizeOf(abd); abd.hWnd = Backdrop.Handle;
            if (want && !appBarRegistered)
            {
                abd.uCallbackMessage = AppBarMessage;
                appBarRegistered = Native.SHAppBarMessage(Native.ABM_NEW, ref abd) != IntPtr.Zero;
                if (appBarRegistered) SaveAppBarHandle(Backdrop.Handle);
            }
            else if (!want && appBarRegistered)
            {
                Native.SHAppBarMessage(Native.ABM_REMOVE, ref abd);
                appBarRegistered = false;
                SaveAppBarHandle(IntPtr.Zero);
            }
            if (appBarRegistered)
            {
                int reserve = (int)Math.Ceiling(barH + 6 * dpiScale);
                abd.uEdge = Native.ABE_BOTTOM;
                abd.rc = new RECT(monitor.Left, monitor.Bottom - reserve, monitor.Right, monitor.Bottom);
                Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref abd);
                abd.rc.Top = abd.rc.Bottom - reserve;
                Native.SHAppBarMessage(Native.ABM_SETPOS, ref abd);
                dockBottom = abd.rc.Bottom - (int)(4 * dpiScale);
            }
            else
            {
                IntPtr hmon;
                var mi = Native.PrimaryMonitor(out hmon);
                dockBottom = mi.rcWork.Bottom - (int)(4 * dpiScale);
                if (Taskbar.IsHiddenByUs || cfg.GetBool("dock-autohide", false)) dockBottom = Math.Max(dockBottom, monitor.Bottom - (int)(4 * dpiScale));
            }
            winTop = dockBottom - winH;
        }

        static string AppBarFile { get { return Path.Combine(Config.Dir, "appbar-hwnd.txt"); } }
        static void SaveAppBarHandle(IntPtr h)
        {
            try
            {
                if (h == IntPtr.Zero) { if (File.Exists(AppBarFile)) File.Delete(AppBarFile); }
                else File.WriteAllText(AppBarFile, h.ToInt64().ToString());
            }
            catch { }
        }

        /// <summary>Used by /restore: give back the screen space a crashed WinDock had reserved.</summary>
        public static void RemoveStaleAppBar()
        {
            try
            {
                if (!File.Exists(AppBarFile)) return;
                long v;
                if (long.TryParse(File.ReadAllText(AppBarFile).Trim(), out v))
                {
                    var abd = new APPBARDATA(); abd.cbSize = Marshal.SizeOf(abd); abd.hWnd = new IntPtr(v);
                    Native.SHAppBarMessage(Native.ABM_REMOVE, ref abd);
                }
                File.Delete(AppBarFile);
            }
            catch { }
        }

        public void RemoveAppBar()
        {
            if (!appBarRegistered) return;
            var abd = new APPBARDATA(); abd.cbSize = Marshal.SizeOf(abd); abd.hWnd = Backdrop.Handle;
            Native.SHAppBarMessage(Native.ABM_REMOVE, ref abd);
            appBarRegistered = false;
            SaveAppBarHandle(IntPtr.Zero);
        }

        public void OnAppBarNotify(int code, IntPtr lParam)
        {
            if (code == Native.ABN_POSCHANGED) { UpdateAppBar(); Wake(); }
            else if (code == Native.ABN_FULLSCREENAPP) { CheckFullscreen(); }
        }

        // ---------------------------------------------------------------- items

        static string PinKey(PinEntry p) { return (p.RightSide ? "R|" : "L|") + p.ToLine(); }

        public void RebuildItems()
        {
            var list = new List<DockItem>();
            if (cfg.GetBool("dock-launcher", true))
            {
                if (launcherItem == null) launcherItem = new DockItem { Kind = DockKind.Launcher, Label = "Launcher" };
                list.Add(launcherItem);
            }
            var seen = new HashSet<string>();
            foreach (var p in cfg.Pins.Where(p => !p.RightSide)) list.Add(ItemForPin(p, seen));
            foreach (var key in unpinnedOrder)
            {
                DockItem it;
                if (runningItems.TryGetValue(key, out it)) list.Add(it);
            }
            if (dividerItem == null) dividerItem = new DockItem { Kind = DockKind.Divider };
            list.Add(dividerItem);
            foreach (var p in cfg.Pins.Where(p => p.RightSide)) list.Add(ItemForPin(p, seen));
            foreach (var it in minItems.Values) list.Add(it);
            if (trashItem == null) trashItem = new DockItem { Kind = DockKind.Trash, Label = "Trash" };
            list.Add(trashItem);
            foreach (var k in pinItems.Keys.Where(k => !seen.Contains(k)).ToList()) pinItems.Remove(k);
            Items = list;
            AssignWindows();
            Wake();
        }

        DockItem ItemForPin(PinEntry p, HashSet<string> seen)
        {
            string key = PinKey(p);
            DockItem it;
            if (!pinItems.TryGetValue(key, out it) || it.Pin == null)
            {
                it = DockItem.FromPin(p);
                pinItems[key] = it;
            }
            it.Pin = p;
            seen.Add(key);
            return it;
        }

        void RefreshRunning()
        {
            lastWindows = Apps.Enumerate();
            AssignWindows();
        }

        /// <summary>Gives every running window to a pinned item, or to an item of its own; adds / drops running-only items.</summary>
        void AssignWindows()
        {
            foreach (var it in Items) it.Windows = new List<AppWindow>();
            foreach (var it in runningItems.Values) it.Windows = new List<AppWindow>();
            var pinnedApps = Items.Where(i => i.Kind == DockKind.App && i.Pin != null).ToList();
            var groups = new Dictionary<string, DockItem>();
            foreach (var w in lastWindows)
            {
                var owner = pinnedApps.FirstOrDefault(i => i.Matches(w));
                if (owner == null)
                {
                    DockItem ri;
                    if (!runningItems.TryGetValue(w.GroupKey, out ri))
                    {
                        ri = DockItem.FromWindow(w);
                        runningItems[w.GroupKey] = ri;
                    }
                    owner = ri;
                    groups[w.GroupKey] = ri;
                }
                owner.Windows.Add(w);
            }
            bool changed = false;
            foreach (var key in runningItems.Keys.ToList())
                if (!groups.ContainsKey(key)) { runningItems.Remove(key); }
            int before = unpinnedOrder.Count;
            unpinnedOrder.RemoveAll(k => !groups.ContainsKey(k));
            if (unpinnedOrder.Count != before) changed = true;
            foreach (var key in groups.Keys) if (!unpinnedOrder.Contains(key)) { unpinnedOrder.Add(key); changed = true; }

            // minimised windows as their own items (option)
            if (cfg.GetBool("dock-minimize-thumbnails", false))
            {
                var mins = lastWindows.Where(w => w.Minimized).ToList();
                foreach (var h in minItems.Keys.ToList())
                    if (!mins.Any(w => w.Hwnd == h)) { minItems.Remove(h); changed = true; }
                foreach (var w in mins)
                    if (!minItems.ContainsKey(w.Hwnd))
                    {
                        var parent = Items.Concat(runningItems.Values).FirstOrDefault(i => i.Windows.Contains(w));
                        minItems[w.Hwnd] = new DockItem { Kind = DockKind.MinWindow, MinHwnd = w.Hwnd, Label = w.Title, Target = parent != null ? parent.IconTarget : w.ExePath, MinThumb = Effects.ThumbnailFor(w.Hwnd) };
                        changed = true;
                    }
                foreach (var kv in minItems) { var w = mins.First(x => x.Hwnd == kv.Key); kv.Value.Label = w.Title; }
            }
            else if (minItems.Count > 0) { minItems.Clear(); changed = true; }

            if (changed) { RebuildItems(); return; }
            foreach (var it in Items)
            {
                if (it.LaunchStart >= 0 && it.Running) it.LaunchStart = -1;
            }
            Wake();
        }

        // ---------------------------------------------------------------- timers

        void SlowTick()
        {
            slowTicks++;
            // once start-up is over, give back memory that was only needed while loading (icons, fonts, COM)
            if (slowTicks == 8 || slowTicks % 600 == 0) TrimMemory();
            if (slowTicks % 2 == 0) RefreshRunning();
            if (slowTicks % 3 == 0)
            {
                var info = new SHQUERYRBINFO(); info.cbSize = Marshal.SizeOf(info);
                bool full = Native.SHQueryRecycleBin(null, ref info) == 0 && info.i64NumItems > 0;
                if (full != trashFull) { trashFull = full; Wake(); }
            }
            if (cfg.GetBool("hide-taskbar", true)) Taskbar.KeepHidden();
            CheckFullscreen();
        }

        [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

        static void TrimMemory()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, new IntPtr(-1), new IntPtr(-1));
        }

        void CheckFullscreen()
        {
            bool fs = Apps.ForegroundIsFullscreen();
            if (fs != fullscreenHidden)
            {
                fullscreenHidden = fs;
                UpdateAppBar();
                Wake();
            }
        }

        void EdgeTick()
        {
            if (!cfg.GetBool("dock-autohide", false)) { edgeTimer.Stop(); return; }
            var c = Native.CursorPos();
            bool atEdge = c.Y >= monitor.Bottom - 2 && c.X >= monitor.Left && c.X < monitor.Right;
            if (atEdge && hideTarget) { hideTarget = false; leaveSince = clock.Elapsed.TotalSeconds; Wake(); }
            else if (!hideTarget) Wake(); // lets the frame loop notice the pointer has left, so the dock hides again
        }

        /// <summary>Start the frame timer (it stops itself when nothing moves, so idle CPU stays near zero).</summary>
        public void Wake()
        {
            if (!IsHandleCreated) return;
            if (!animTimer.Enabled) { lastFrame = clock.Elapsed.TotalSeconds; animTimer.Start(); }
        }

        // ---------------------------------------------------------------- layout

        class Slot { public DockItem Item; public float BaseW, W, Start, BaseStart; public float Scale = 1; }
        List<Slot> slots = new List<Slot>();
        float layoutLeft, layoutWidth;

        float SlotBaseWidth(DockItem it) { return it != null && it.Kind == DockKind.Divider ? S * 0.32f : S + gap; }

        List<Slot> BuildSlots()
        {
            var list = new List<Slot>();
            int index = 0;
            foreach (var it in Items)
            {
                if (dragging && it == pressItem) continue;
                if (dragging && !dragRemove && !dragOutside && index == dragInsert) list.Add(new Slot { Item = null, BaseW = S + gap });
                list.Add(new Slot { Item = it, BaseW = SlotBaseWidth(it) });
                index++;
            }
            if (dragging && !dragRemove && !dragOutside && dragInsert >= index) list.Add(new Slot { Item = null, BaseW = S + gap });
            return list;
        }

        static float Bell(float d)
        {
            if (d > 3f) return 0f;
            return (float)Math.Exp(-(d * d) / (2 * 0.9f * 0.9f));
        }

        void LayoutDock(float dt)
        {
            slots = BuildSlots();
            float total = 0;
            foreach (var s in slots) { s.BaseStart = total; total += s.BaseW; }
            float baseLeft = monitor.Left + (monitor.Width - total) / 2f;
            float mx = Math.Max(0, Math.Min(total - 0.01f, mouseX - baseLeft));
            bool magnifyNow = hovering && !MenuOpen && maxScale > 1.001f && (Stack == null || !Stack.Visible);
            float k = Math.Min(1f, dt * 16f);
            foreach (var s in slots)
            {
                float target = 1f;
                if (magnifyNow && (s.Item == null || s.Item.Kind != DockKind.Divider))
                {
                    float center = s.BaseStart + s.BaseW / 2f;
                    float d = Math.Abs(mx - center) / (S + gap);
                    target = 1f + (maxScale - 1f) * Bell(d);
                }
                if (s.Item != null)
                {
                    s.Item.Scale += (target - s.Item.Scale) * k;
                    if (Math.Abs(target - s.Item.Scale) < 0.002f) s.Item.Scale = target;
                    s.Scale = s.Item.Scale;
                }
                else s.Scale = target;
                s.W = (s.Item != null && s.Item.Kind == DockKind.Divider) ? s.BaseW : S * s.Scale + gap;
            }
            float mag = 0;
            foreach (var s in slots) { s.Start = mag; mag += s.W; }
            // keep the point under the cursor under the cursor: the dock grows outwards from it
            float left = baseLeft;
            int slotIndex = slots.FindIndex(s => mx >= s.BaseStart && mx < s.BaseStart + s.BaseW);
            if (slotIndex >= 0)
            {
                var s = slots[slotIndex];
                float frac = (mx - s.BaseStart) / s.BaseW;
                left = (baseLeft + mx) - (s.Start + frac * s.W);
            }
            layoutLeft = left; layoutWidth = mag;
            float barBottom = dockBottom + hideAmount * (barH + 12 * dpiScale);
            barRect = new RectangleF(left - padX, barBottom - barH, mag + padX * 2, barH);
            foreach (var s in slots)
            {
                if (s.Item == null) continue;
                s.Item.CurX = left + s.Start + s.W / 2f;
                s.Item.CurW = s.Item.Kind == DockKind.Divider ? 0 : S * s.Scale;
            }
        }

        public RectangleF IconRect(DockItem it)
        {
            float w = it.CurW;
            float bottom = barRect.Bottom - vpad;
            return new RectangleF(it.CurX - w / 2, bottom - w, w, w);
        }

        DockItem ItemAt(Point screen)
        {
            if (screen.Y > barRect.Bottom + 2 || screen.Y < barRect.Top - S * (maxScale - 1) - 4) return null;
            foreach (var s in slots)
            {
                if (s.Item == null || s.Item.Kind == DockKind.Divider) continue;
                float l = layoutLeft + s.Start, r = l + s.W;
                if (screen.X >= l && screen.X < r)
                {
                    var ir = IconRect(s.Item);
                    if (screen.Y >= ir.Top - 2 || screen.Y >= barRect.Top) return s.Item;
                }
            }
            return null;
        }

        // ---------------------------------------------------------------- frame

        void Frame()
        {
            double now = clock.Elapsed.TotalSeconds;
            float dt = (float)Math.Min(0.05, now - lastFrame);
            lastFrame = now;
            if (!IsHandleCreated) return;

            var cursor = Native.CursorPos();
            bool overUs = Native.WindowFromPoint(new POINT(cursor.X, cursor.Y)) == Handle;
            bool inBar = barRect.Contains(cursor.X, cursor.Y) || (cursor.Y >= barRect.Top && cursor.Y <= monitor.Bottom && cursor.X >= barRect.Left && cursor.X <= barRect.Right);
            bool popupOpen = MenuOpen || (Stack != null && Stack.Visible) || (Previews != null && Previews.Visible);
            hovering = !Suppressed && hideAmount < 0.5f && (overUs || inBar || dragging);
            if (hovering || dragging) mouseX = cursor.X;

            // auto-hide
            bool autoHide = cfg.GetBool("dock-autohide", false);
            if (autoHide)
            {
                if (hovering || popupOpen || dragging || dropTarget != null) leaveSince = -1;
                else if (leaveSince < 0) leaveSince = now;
                if (!hideTarget && leaveSince >= 0 && now - leaveSince > 0.6) hideTarget = true;
                if (!edgeTimer.Enabled) edgeTimer.Start();
            }
            else hideTarget = false;
            float hideGoal = (Suppressed || hideTarget) ? 1f : 0f;
            if (Suppressed && !autoHide) hideAmount = hideGoal;
            float hs = Math.Min(1f, dt * 9f);
            hideAmount += (hideGoal - hideAmount) * hs;
            if (Math.Abs(hideGoal - hideAmount) < 0.01f) hideAmount = hideGoal;

            // hover item, label delay, previews and stacks on hover
            var hit = hovering && !dragging ? ItemAt(cursor) : null;
            if (hit != hoverItem) { hoverItem = hit; hoverSince = now; }
            HoverPopups(now);

            LayoutDock(dt);
            bool animating = Render(now);

            if (hideAmount >= 1f)
            {
                if (Visible) { Hide(); Backdrop.Hide(); }
            }
            else Backdrop.Place(barRect, S * 0.3f, Theme.Transparency);

            bool settled = !animating && !hovering && !dragging && hideAmount == hideGoal && Items.All(i => i.Scale == 1f || !hovering && Math.Abs(i.Scale - 1f) < 0.002f);
            if (settled && !popupOpen) animTimer.Stop();
        }

        /// <summary>Draws the dock. Returns true while something is still animating.</summary>
        bool Render(double now)
        {
            if (hideAmount >= 1f) return false;
            bool animating = false;
            int width = monitor.Width;
            Surface.Ensure(width, winH);
            bool dark = Theme.ShellDark;
            float originX = monitor.Left, originY = winTop + 0; // window origin on screen
            int yShift = (int)Math.Round(hideAmount * (barH + 12 * dpiScale));
            using (var g = Surface.BeginDraw())
            {
                g.TranslateTransform(-originX, -originY - yShift);
                // ---- the bar
                var bar = new RectangleF(barRect.X, barRect.Y - yShift + yShift, barRect.Width, barRect.Height);
                float radius = S * 0.3f;
                bool glass = Theme.Transparency;
                Color fill = dark ? Color.FromArgb(glass ? 60 : 255, 38, 38, 42) : Color.FromArgb(glass ? 70 : 255, 246, 246, 250);
                Color edge = dark ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(60, 0, 0, 0);
                using (var path = Theme.Rounded(new RectangleF(bar.X, bar.Y - yShift * 0, bar.Width, bar.Height), radius))
                {
                    using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                    using (var pen = new Pen(edge, Math.Max(1f, dpiScale))) g.DrawPath(pen, path);
                }
                // ---- items
                foreach (var s in slots)
                {
                    var it = s.Item;
                    if (it == null) continue;
                    if (it.Kind == DockKind.Divider)
                    {
                        using (var pen = new Pen(dark ? Color.FromArgb(90, 255, 255, 255) : Color.FromArgb(70, 0, 0, 0), Math.Max(1f, dpiScale)))
                            g.DrawLine(pen, it.CurX, bar.Top + vpad * 0.6f, it.CurX, bar.Bottom - vpad * 0.6f);
                        continue;
                    }
                    var r = IconRect(it);
                    float bounce = 0;
                    if (it.LaunchStart >= 0)
                    {
                        double t = now - it.LaunchStart;
                        if (t > 8) it.LaunchStart = -1;
                        else { bounce = (float)Math.Abs(Math.Sin(t * Math.PI / 0.55)) * S * 0.45f; animating = true; }
                    }
                    else if (it.BounceStart >= 0)
                    {
                        double t = now - it.BounceStart;
                        if (t > 0.55) it.BounceStart = -1;
                        else { bounce = (float)Math.Sin(t * Math.PI / 0.55) * S * 0.45f; animating = true; }
                    }
                    r.Offset(0, -bounce);
                    DrawIcon(g, it, r, dark);
                    if (it == pressItem && pressed && !dragging)
                        using (var b = new SolidBrush(Color.FromArgb(90, 0, 0, 0))) using (var p = Theme.Squircle(RectangleF.Inflate(r, -r.Width * 0.06f, -r.Height * 0.06f))) g.FillPath(b, p);
                    if (it == dropTarget)
                        using (var b = new SolidBrush(Color.FromArgb(70, 255, 255, 255))) using (var p = Theme.Squircle(RectangleF.Inflate(r, -r.Width * 0.06f, -r.Height * 0.06f))) g.FillPath(b, p);
                    if (it.Running && it.Kind == DockKind.App)
                    {
                        float d = Math.Max(3.5f, S * 0.08f);
                        using (var b = new SolidBrush(dark ? Color.FromArgb(220, 255, 255, 255) : Color.FromArgb(170, 0, 0, 0)))
                            g.FillEllipse(b, it.CurX - d / 2, bar.Bottom - vpad * 0.5f - d / 2, d, d);
                    }
                    if (Math.Abs(it.Scale - (hovering ? it.Scale : 1f)) > 0.001f) animating = true;
                }
                // ---- label above the hovered item
                if (hoverItem != null && !dragging && !MenuOpen && (Stack == null || !Stack.Visible) && (Previews == null || !Previews.Visible))
                    DrawLabel(g, hoverItem, dark);
            }
            Surface.Present(Handle, monitor.Left, winTop + yShift, 255);
            if (!Visible)
            {
                Bounds = new Rectangle(monitor.Left, winTop + yShift, Surface.Width, Surface.Height);
                Native.ShowWindow(Handle, Native.SW_SHOWNA);
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                Surface.Present(Handle, monitor.Left, winTop + yShift, 255);
            }
            foreach (var s in slots) if (s.Item != null && Math.Abs(s.Item.Scale - 1f) > 0.002f && !hovering) animating = true;
            return animating;
        }

        void DrawIcon(Graphics g, DockItem it, RectangleF r, bool dark)
        {
            switch (it.Kind)
            {
                case DockKind.Launcher: Glyphs.Launcher(g, r); return;
                case DockKind.Trash: Glyphs.Trash(g, r, trashFull, dark); return;
                case DockKind.Folder:
                    Glyphs.Folder(g, r, Folders.Emblem(it.Target), DockState.Get("tag|" + it.Target, null));
                    return;
                case DockKind.MinWindow:
                    DrawMinWindow(g, it, r);
                    return;
            }
            EnsureIcon(it);
            int hiPx = (int)Math.Ceiling(S * maxScale);
            var bmp = it.IconFor(S, hiPx, r.Width);
            if (bmp != null)
            {
                g.InterpolationMode = Math.Abs(r.Width - bmp.Width) < 0.5f ? InterpolationMode.NearestNeighbor : InterpolationMode.Bilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(bmp, r);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            }
            else if (it.Kind == DockKind.Website) Glyphs.Globe(g, r, it.Label);
            else Glyphs.Letter(g, r, it.Label);
        }

        void DrawMinWindow(Graphics g, DockItem it, RectangleF r)
        {
            var inner = RectangleF.Inflate(r, -r.Width * 0.06f, -r.Height * 0.06f);
            if (it.MinThumb != null)
            {
                float ar = it.MinThumb.Width / (float)Math.Max(1, it.MinThumb.Height);
                var tr = ar >= 1 ? new RectangleF(inner.X, inner.Y + (inner.Height - inner.Width / ar) / 2, inner.Width, inner.Width / ar)
                                 : new RectangleF(inner.X + (inner.Width - inner.Height * ar) / 2, inner.Y, inner.Height * ar, inner.Height);
                g.DrawImage(it.MinThumb, tr);
                using (var pen = new Pen(Color.FromArgb(60, 0, 0, 0))) g.DrawRectangle(pen, tr.X, tr.Y, tr.Width, tr.Height);
            }
            else Theme.FillRounded(g, Color.FromArgb(200, 200, 200, 205), inner, inner.Width * 0.08f);
            // small app badge
            EnsureIcon(it);
            var bmp = it.IconFor(S / 2, S, r.Width * 0.45f);
            if (bmp != null) g.DrawImage(bmp, new RectangleF(r.Right - r.Width * 0.45f, r.Bottom - r.Height * 0.45f, r.Width * 0.45f, r.Height * 0.45f));
        }

        void DrawLabel(Graphics g, DockItem it, bool dark)
        {
            if (string.IsNullOrEmpty(it.Label)) return;
            var r = IconRect(it);
            using (var f = Theme.MakeFont(13f * dpiScale, FontStyle.Regular))
            {
                var sz = Theme.Measure(g, it.Label, f);
                float w = Math.Min(sz.Width + 22 * dpiScale, 420 * dpiScale), h = sz.Height + 9 * dpiScale;
                var box = new RectangleF(it.CurX - w / 2, r.Top - h - 9 * dpiScale, w, h);
                Color bg = dark ? Color.FromArgb(235, 44, 44, 48) : Color.FromArgb(240, 248, 248, 250);
                Color fg = dark ? Color.FromArgb(240, 240, 242) : Color.FromArgb(30, 30, 32);
                Theme.FillRounded(g, bg, box, 7 * dpiScale);
                Theme.StrokeRounded(g, dark ? Color.FromArgb(60, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0), 1, box, 7 * dpiScale);
                Theme.DrawText(g, it.Label, f, fg, box, StringAlignment.Center, StringAlignment.Center);
            }
        }

        void EnsureIcon(DockItem it)
        {
            if (it.IconRequested) return;
            it.IconRequested = true;
            int px = S * maxScale > 96 ? 256 : 128;
            if (it.Kind == DockKind.Website)
            {
                var cached = SiteIcons.Cached(it.Target);
                if (cached != null) { it.SetIcon(WebTile(cached)); cached.Dispose(); return; }
                if (cfg.GetBool("website-icons", true))
                    SiteIcons.Fetch(it.Target, bmp => { it.SetIcon(WebTile(bmp)); bmp.Dispose(); Wake(); });
                return;
            }
            string target = it.IconTarget;
            if (target == null) return;
            var windows = it.Windows.Select(w => w.Hwnd).ToList();
            IconLoader.Request(() =>
            {
                var bmp = Shell.GetImage(target, px, it.Kind != DockKind.File);
                if (bmp == null && windows.Count > 0) bmp = WindowIcon(windows[0]);
                return bmp;
            }, bmp => { if (bmp != null) { it.SetIcon(bmp); Wake(); } });
        }

        static Bitmap WebTile(Bitmap favicon)
        {
            return Glyphs.Render(256, (g, r) => Glyphs.WebTile(g, r, favicon));
        }

        static Bitmap WindowIcon(IntPtr hwnd)
        {
            try
            {
                IntPtr res;
                Native.SendMessageTimeout(hwnd, Native.WM_GETICON, new IntPtr(1), IntPtr.Zero, 2, 200, out res);
                if (res == IntPtr.Zero) res = Native.GetClassLongPtr(hwnd, -14);
                if (res == IntPtr.Zero) return null;
                using (var ico = Icon.FromHandle(res)) return ico.ToBitmap();
            }
            catch { return null; }
        }

        /// <summary>Forget icons (e.g. after the dock size changed a lot) so they are reloaded at the right size.</summary>
        public void ReloadIcons()
        {
            foreach (var it in Items.Concat(runningItems.Values)) { it.IconRequested = false; }
            Wake();
        }

        // ---------------------------------------------------------------- hover popups

        void HoverPopups(double now)
        {
            if (dragging || MenuOpen) return;
            var it = hoverItem;
            // window previews after ~0.5 s on a running app
            if (it != null && it.Kind == DockKind.App && it.Running && cfg.GetBool("dock-previews", true) && now - hoverSince > 0.5)
            {
                if (Previews == null || !Previews.Visible || Previews.Owner_ != it)
                {
                    if (Stack == null || !Stack.Visible) ShowPreviews(it);
                }
            }
            // folder stacks on hover (when "open on click" is off)
            if (it != null && it.Kind == DockKind.Folder && !cfg.GetBool("stacks-click", true) && now - hoverSince > 0.45)
            {
                if (Stack == null || !Stack.Visible || Stack.Item != it) OpenStack(it);
            }
            if (Previews != null && Previews.Visible && it != Previews.Owner_ && !Previews.ContainsCursor())
                Previews.HideSoon();
        }

        public void ShowPreviews(DockItem it)
        {
            if (Previews == null) Previews = new PreviewPopup(this);
            var r = IconRect(it);
            Previews.ShowFor(it, new Point((int)it.CurX, (int)(r.Top - 8 * dpiScale)), dpiScale);
        }

        public void OpenStack(DockItem it)
        {
            if (Previews != null) Previews.Hide();
            if (Stack != null && Stack.Visible && Stack.Item == it) { Stack.Close(); return; }
            if (Stack != null) { Stack.CloseNow(); }
            Stack = new StackPopup(this, it, cfg, dpiScale);
            var r = IconRect(it);
            Stack.Open(new Point((int)it.CurX, (int)(barRect.Top - 6 * dpiScale)), (int)r.Width);
            Wake();
        }

        // ---------------------------------------------------------------- mouse

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Wake();
            if (!pressed) return;
            var c = Native.CursorPos();
            if (!dragging && pressItem != null && pressItem.Kind != DockKind.Launcher && pressItem.Kind != DockKind.Trash && pressItem.Kind != DockKind.MinWindow
                && (Math.Abs(c.X - pressPoint.X) > 5 * dpiScale || Math.Abs(c.Y - pressPoint.Y) > 5 * dpiScale))
            {
                BeginDrag();
            }
            if (dragging) UpdateDrag(c);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var c = Native.CursorPos();
            if (e.Button == MouseButtons.Left)
            {
                pressItem = ItemAt(c);
                pressPoint = c;
                pressed = true;
                Capture = true;
                Wake();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var c = Native.CursorPos();
            if (e.Button == MouseButtons.Right)
            {
                var it = ItemAt(c);
                if (it != null) ShowItemMenu(it); else if (barRect.Contains(c.X, c.Y) || Native.WindowFromPoint(new POINT(c.X, c.Y)) == Handle) ShowDockMenu(c);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            bool wasDragging = dragging;
            var item = pressItem;
            if (wasDragging) EndDrag(c);
            pressed = false;
            pressItem = null;
            Capture = false;
            if (!wasDragging && item != null && ItemAt(c) == item) ClickItem(item);
            Wake();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (dragging && !Capture) { CancelDrag(); }
        }

        // ---------------------------------------------------------------- clicking

        public void ClickItem(DockItem it)
        {
            if (Previews != null) Previews.Hide();
            hoverSince = double.MaxValue / 4; // no previews popping up again until the pointer moves to another item
            switch (it.Kind)
            {
                case DockKind.Launcher: App.OpenLauncher(); return;
                case DockKind.Trash: Shellx.Open("shell:RecycleBinFolder"); return;
                case DockKind.Folder: OpenStack(it); return;
                case DockKind.File: Shellx.Open(it.Target); it.BounceStart = clock.Elapsed.TotalSeconds; return;
                case DockKind.Website: Shellx.Open(it.Target); it.BounceStart = clock.Elapsed.TotalSeconds; return;
                case DockKind.MinWindow: Effects.Restore(it.MinHwnd, IconRect(it), cfg); return;
            }
            // apps
            if (!it.Running)
            {
                it.LaunchStart = clock.Elapsed.TotalSeconds;
                it.Launch();
                Wake();
                return;
            }
            IntPtr fg = Apps.ForegroundRoot();
            var front = it.Windows.FirstOrDefault(w => w.Hwnd == fg);
            if (front != null)
            {
                var others = it.Windows.Where(w => w.Hwnd != fg).ToList();
                if (others.Count == 0) Effects.Minimize(front.Hwnd, IconRect(it), cfg);
                else
                {
                    // several windows: cycle to the next one
                    var next = others.LastOrDefault(w => !w.Minimized) ?? others.Last();
                    ActivateWindow(next.Hwnd, it);
                }
                return;
            }
            // bring the app forward: its most recent window (restoring it if all are minimised)
            var target = it.Windows.FirstOrDefault(w => !w.Minimized) ?? it.Windows.First();
            ActivateWindow(target.Hwnd, it);
        }

        void ActivateWindow(IntPtr h, DockItem it)
        {
            if (Native.IsIconic(h)) Effects.Restore(h, IconRect(it), cfg);
            else Apps.Activate(h);
        }

        // ---------------------------------------------------------------- drag to reorder / remove

        void BeginDrag()
        {
            dragging = true;
            if (Previews != null) Previews.Hide();
            if (Stack != null && Stack.Visible) Stack.CloseNow();
            ghost = new DragGhost();
            var src = pressItem;
            ghost.SetImage(RenderItemBitmap(src, S));
            dragInsert = Items.IndexOf(src);
            UpdateDrag(Native.CursorPos());
        }

        Bitmap RenderItemBitmap(DockItem it, int px)
        {
            var bmp = new Bitmap(px, px, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                Theme.HighQuality(g);
                DrawIcon(g, it, new RectangleF(0, 0, px, px), Theme.ShellDark);
            }
            return bmp;
        }

        bool CanRemove(DockItem it) { return it.Pin != null; }

        void UpdateDrag(Point c)
        {
            float bandTop = barRect.Top - S * 1.1f;
            dragOutside = c.Y < bandTop || c.X < barRect.Left - S || c.X > barRect.Right + S;
            dragRemove = dragOutside && CanRemove(pressItem) && c.Y < barRect.Top - S * 1.6f;
            ghost.ShowAt(c, dragRemove);
            // where would the item go?
            var others = Items.Where(i => i != pressItem).ToList();
            int lo, hi;
            SectionRange(pressItem, others, out lo, out hi);
            int best = lo;
            float bestDist = float.MaxValue;
            for (int i = lo; i <= hi; i++)
            {
                // boundary i lies before others[i]
                float x;
                if (i < others.Count) x = others[i].CurX - (others[i].CurW / 2 + gap / 2);
                else x = others.Count > 0 ? others[others.Count - 1].CurX + others[others.Count - 1].CurW / 2 + gap / 2 : barRect.Left;
                float d = Math.Abs(c.X - x);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            dragInsert = best;
            Wake();
        }

        /// <summary>Insertion range in the list without the dragged item: apps stay left of the divider, folders/files right.</summary>
        void SectionRange(DockItem it, List<DockItem> others, out int lo, out int hi)
        {
            int divider = others.IndexOf(dividerItem);
            if (it.RightSide)
            {
                lo = divider + 1;
                int trash = others.IndexOf(trashItem);
                hi = trash;
                int firstMin = others.FindIndex(i => i.Kind == DockKind.MinWindow);
                if (firstMin >= 0) hi = firstMin;
            }
            else
            {
                lo = launcherItem != null && others.Contains(launcherItem) ? others.IndexOf(launcherItem) + 1 : 0;
                // pinned apps only: running-only items sit after them
                int lastPinned = lo;
                for (int i = 0; i < divider; i++) if (others[i].Pin != null && !others[i].RightSide) lastPinned = i + 1;
                hi = Math.Max(lo, lastPinned);
            }
        }

        void EndDrag(Point c)
        {
            var it = pressItem;
            dragging = false;
            pressItem = null; pressed = false;
            if (ghost != null) { ghost.Close(); ghost.Dispose(); ghost = null; }
            if (dragRemove && it.Pin != null)
            {
                Poof.At(c, dpiScale);
                cfg.Pins.Remove(it.Pin);
                cfg.Save();
                pinItems.Remove(PinKey(it.Pin));
                it.Pin = null;
                RebuildItems();
                RefreshRunning();
                return;
            }
            if (dragOutside) { Wake(); return; }
            // reorder: rebuild the pin list from the visual order
            var others = Items.Where(i => i != it).ToList();
            int idx = Math.Max(0, Math.Min(dragInsert, others.Count));
            others.Insert(idx, it);
            if (it.Pin == null && it.Kind == DockKind.App)
            {
                it.Pin = new PinEntry(it.Label, it.Target, false);
                runningItems.Remove(it.GroupKey);
                unpinnedOrder.Remove(it.GroupKey);
                it.GroupKey = null;
                pinItems[PinKey(it.Pin)] = it;
            }
            var newPins = new List<PinEntry>();
            foreach (var x in others) if (x.Pin != null && !x.Pin.RightSide) newPins.Add(x.Pin);
            foreach (var x in others) if (x.Pin != null && x.Pin.RightSide) newPins.Add(x.Pin);
            cfg.Pins.Clear();
            cfg.Pins.AddRange(newPins);
            cfg.Save();
            RebuildItems();
            RefreshRunning();
        }

        void CancelDrag()
        {
            dragging = false; pressed = false;
            if (ghost != null) { ghost.Close(); ghost.Dispose(); ghost = null; }
            Wake();
        }

        // ---------------------------------------------------------------- dropping files

        protected override void OnDragEnter(DragEventArgs e) { OnDragOver(e); }

        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effect = DragDropEffects.None; return; }
            var it = ItemAt(new Point(e.X, e.Y));
            dropTarget = it;
            if (it == null) e.Effect = DragDropEffects.Link;                   // pin it
            else if (it.Kind == DockKind.Trash) e.Effect = DragDropEffects.Move;
            else if (it.Kind == DockKind.App || it.Kind == DockKind.Folder) e.Effect = DragDropEffects.Copy;
            else e.Effect = DragDropEffects.None;
            hovering = true; mouseX = e.X;
            Wake();
        }

        protected override void OnDragLeave(EventArgs e) { base.OnDragLeave(e); dropTarget = null; Wake(); }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            var it = ItemAt(new Point(e.X, e.Y));
            dropTarget = null;
            if (files == null || files.Length == 0) return;
            if (it == null)
            {
                foreach (var f in files) AddPin(f, null);
                return;
            }
            switch (it.Kind)
            {
                case DockKind.Trash: Shellx.Recycle(files, Handle); break;
                case DockKind.App: it.LaunchStart = it.Running ? -1 : clock.Elapsed.TotalSeconds; it.BounceStart = clock.Elapsed.TotalSeconds; it.OpenFiles(files); break;
                case DockKind.Folder: Shellx.CopyOrMove(files, it.Target, Handle); break;
            }
            Wake();
        }

        public void AddPin(string target, string label)
        {
            var pin = new PinEntry(label, target, false);
            bool right = !pin.IsWebsite && (Directory.Exists(target) || (File.Exists(target) && !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)));
            pin.RightSide = right;
            if (right) cfg.Pins.Add(pin);
            else
            {
                int lastLeft = cfg.Pins.FindLastIndex(p => !p.RightSide);
                cfg.Pins.Insert(lastLeft + 1, pin);
            }
            cfg.Save();
            RebuildItems();
            RefreshRunning();
        }

        public void TogglePin(DockItem it)
        {
            if (it.Pin != null)
            {
                cfg.Pins.Remove(it.Pin);
                pinItems.Remove(PinKey(it.Pin));
                it.Pin = null;
                it.IconRequested = false;
            }
            else
            {
                var pin = new PinEntry(it.Label, it.Target, false);
                int lastLeft = cfg.Pins.FindLastIndex(p => !p.RightSide);
                cfg.Pins.Insert(lastLeft + 1, pin);
                runningItems.Remove(it.GroupKey);
                unpinnedOrder.Remove(it.GroupKey);
            }
            cfg.Save();
            RebuildItems();
            RefreshRunning();
        }

        public void SetAutoHide(bool on)
        {
            cfg.SetBool("dock-autohide", on);
            hideTarget = on;
            UpdateAppBar();
            Wake();
        }

        public void Shutdown()
        {
            animTimer.Stop(); slowTimer.Stop(); edgeTimer.Stop();
            if (hookFg != IntPtr.Zero) Native.UnhookWinEvent(hookFg);
            if (hookShow != IntPtr.Zero) Native.UnhookWinEvent(hookShow);
            if (hookMin != IntPtr.Zero) Native.UnhookWinEvent(hookMin);
            RemoveAppBar();
            if (Stack != null) Stack.CloseNow();
            if (Previews != null) Previews.Hide();
            Backdrop.Hide();
            Hide();
        }

        /// <summary>Renders a static picture of the dock for the Settings preview.</summary>
        public Bitmap RenderPreview(int width, int height, int sizeLogical, float magnifyMax, float hoverFrac, float scale)
        {
            var bmp = new Bitmap(Math.Max(1, width), Math.Max(1, height), PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                Theme.HighQuality(g);
                var items = Items.Where(i => i.Kind != DockKind.MinWindow).ToList();
                float s = sizeLogical * scale;
                float gp = s * 0.14f, vp = Math.Max(3, s * 0.15f);
                float avail = width - 16 * scale;
                // show fewer icons rather than shrinking them, so the preview shows the chosen size
                Func<float> sumW = () => items.Sum(i => i.Kind == DockKind.Divider ? s * 0.32f : s + gp) * (1 + (magnifyMax - 1) * 0.5f);
                while (sumW() > avail && items.Count > 5)
                {
                    int drop = items.FindLastIndex(i => i.Kind == DockKind.App);
                    if (drop < 0) drop = items.FindLastIndex(i => i.Kind == DockKind.Folder || i.Kind == DockKind.File || i.Kind == DockKind.Website);
                    if (drop < 0) break;
                    items.RemoveAt(drop);
                }
                float total = items.Sum(i => i.Kind == DockKind.Divider ? s * 0.32f : s + gp);
                if (total * 1.0f > avail) { float f = avail / (total * 1.15f); s *= f; gp *= f; vp *= f; total *= f; }
                float barHh = s + vp * 2;
                float bottom = height - 8 * scale;
                float mx = hoverFrac < 0 ? -1 : hoverFrac * total;
                var scales = new float[items.Count];
                var ws = new float[items.Count];
                float acc = 0, mag = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    float bw = items[i].Kind == DockKind.Divider ? s * 0.32f : s + gp;
                    float c = acc + bw / 2; acc += bw;
                    float d = mx < 0 ? 99 : Math.Abs(mx - c) / (s + gp);
                    scales[i] = items[i].Kind == DockKind.Divider ? 1 : 1 + (magnifyMax - 1) * Bell(d);
                    ws[i] = items[i].Kind == DockKind.Divider ? bw : s * scales[i] + gp;
                    mag += ws[i];
                }
                float left = (width - mag) / 2;
                bool dark = Theme.ShellDark;
                var bar = new RectangleF(left - s * 0.14f, bottom - barHh, mag + s * 0.28f, barHh);
                Theme.FillRounded(g, dark ? Color.FromArgb(230, 52, 52, 56) : Color.FromArgb(235, 250, 250, 252), bar, s * 0.3f);
                Theme.StrokeRounded(g, dark ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(50, 0, 0, 0), 1, bar, s * 0.3f);
                float x = left;
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    float cx = x + ws[i] / 2; x += ws[i];
                    if (it.Kind == DockKind.Divider)
                    {
                        using (var pen = new Pen(dark ? Color.FromArgb(90, 255, 255, 255) : Color.FromArgb(70, 0, 0, 0))) g.DrawLine(pen, cx, bar.Top + vp * 0.6f, cx, bar.Bottom - vp * 0.6f);
                        continue;
                    }
                    float w = s * scales[i];
                    var r = new RectangleF(cx - w / 2, bar.Bottom - vp - w, w, w);
                    DrawIcon(g, it, r, dark);
                    if (it.Running && it.Kind == DockKind.App)
                    {
                        float d = Math.Max(2.5f, s * 0.08f);
                        using (var b = new SolidBrush(dark ? Color.FromArgb(220, 255, 255, 255) : Color.FromArgb(170, 0, 0, 0)))
                            g.FillEllipse(b, cx - d / 2, bar.Bottom - vp * 0.5f - d / 2, d, d);
                    }
                }
            }
            return bmp;
        }
    }

    /// <summary>The blurred glass behind the dock bar. Also the window that owns WinDock's app bar (reserved screen space).</summary>
    public class DockBackdrop : Form
    {
        readonly DockWindow dock;
        Rectangle current;
        int currentRadius;
        bool glassOn = true, accentSet;

        public DockBackdrop(DockWindow dock)
        {
            this.dock = dock;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            Text = "WinDock Dock Backdrop";
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= (int)(Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST);
                return cp;
            }
        }
        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == DockWindow.AppBarMessage) { dock.OnAppBarNotify(m.WParam.ToInt32(), m.LParam); return; }
            if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = new IntPtr(Native.MA_NOACTIVATE); return; }
            base.WndProc(ref m);
        }

        public void Place(RectangleF bar, float radius, bool glass)
        {
            if (!glass)
            {
                if (Visible) Hide();
                glassOn = false;
                return;
            }
            if (!accentSet || !glassOn)
            {
                Native.SetAccent(Handle, Native.ACCENT_ENABLE_ACRYLICBLURBEHIND, Theme.ShellDark ? 0x40201E1Eu : 0x40F4F2F2u);
                accentSet = true; glassOn = true;
            }
            var r = Rectangle.Round(bar);
            int rad = (int)Math.Round(radius * 2);
            if (r != current || !Visible)
            {
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, r.X, r.Y, r.Width, r.Height, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
                if (r.Size != current.Size || rad != currentRadius)
                {
                    IntPtr rgn = Native.CreateRoundRectRgn(0, 0, r.Width + 1, r.Height + 1, rad, rad);
                    Native.SetWindowRgn(Handle, rgn, true);
                }
                current = r; currentRadius = rad;
                if (!Visible) { Native.ShowWindow(Handle, Native.SW_SHOWNA); }
                // keep the icons above the glass
                Native.SetWindowPos(dock.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
        }
    }
}
