# WinDock — behaviour specification

WinDock is a Mac-style desktop for Windows 11: a dock, a menu bar, an app launcher, search, a window overview,
Mac-style window buttons, a file browser and a quick-settings panel. This document describes **what WinDock does
and how it behaves**, so it can be built from scratch.

**Status:** draft 1 · **Licence of the result:** MIT · **Platform:** Windows 11 x64 (Windows 10 best effort)

---

## 0. Ground rules for building WinDock

1. **Clean-room.** Build only from this document, public Windows documentation and your own knowledge. Do not
   open, copy, translate or paraphrase code from MacDock (gadgetcue/MacDock) or any other dock project. If a
   detail is missing here, decide it yourself or add it to this spec first.
2. **Own assets only.** No Apple, Microsoft or third-party icons, wallpapers, fonts or screenshots in the repo,
   except files with a licence that allows it (e.g. Inter under the SIL OFL, shipped with its licence text).
   Icons are drawn in code or made by us; users can add their own icon packs.
3. **Names.** Do not use "Mac", "macOS", "Apple", "Launchpad", "Spotlight", "Mission Control", "Dynamic Island",
   "Finder", "Control Center" or the Apple logo in the product or UI. This spec uses neutral names:

   | WinDock name | Plays the role of |
   |---|---|
   | Dock | the macOS Dock |
   | Menu bar | the macOS menu bar |
   | Launcher | Launchpad |
   | Search | Spotlight |
   | Overview | Mission Control |
   | Pill | Dynamic Island |
   | Quick Panel | Control Center |
   | Files | Finder |
   | Window buttons | the red / yellow / green "traffic lights" |
   | System menu | the Apple menu (shows the Windows logo) |

4. **Private by default.** No accounts, analytics, telemetry or self-updater. No network access except features
   the user turns on (file sharing, website icons, optional icon lookup), each clearly labelled.
5. **No admin rights.** Installs and runs per user. Never changes security settings.
6. **Safe to quit or crash.** Anything WinDock hides or changes in Windows (the taskbar, window positions,
   window frame colours) is put back when it quits, and a `/restore` command line switch puts it back after a
   crash.
7. **Tech constraint (recommended):** C# on .NET Framework 4.8 WinForms + Win32/DWM, built with the `csc.exe` that
   ships with Windows (no SDK needed), single exe. Per-monitor DPI aware (v2). Any other stack is fine if it keeps
   "no admin, no SDK, one folder".

Each section below ends with **acceptance checks**: things a tester can do to confirm the behaviour.

---

## 1. Install, start, quit

- **Install** (`Install.bat`, double-clicked from File Explorer): copies the app to `%LOCALAPPDATA%\WinDock`,
  creates a Start Menu shortcut, starts WinDock. Option: start at login (on by default, toggle in Settings).
- **Uninstall** (`Uninstall.bat`): stops WinDock, restores the taskbar, removes autostart and the shortcut and the
  install folder. User settings in `%APPDATA%\WinDock` are kept unless the user deletes them.
- **Single instance**: starting it again while running does nothing harmful (focuses Settings or is ignored).
- **Quit** from the System menu or Settings. Quitting restores the taskbar and undoes window changes.
- `WinDock.exe /restore` restores the taskbar and exits.

Acceptance: install from Explorer → dock and menu bar appear within 3 s; quit → Windows taskbar is back; kill the
process in Task Manager → `/restore` brings the taskbar back.

---

## 2. Dock

### 2.1 Layout and look
- A rounded, translucent, blurred bar centred at the bottom of the primary screen, above the work area.
- Left section: pinned apps, then running apps that aren't pinned. A thin divider. Right section: pinned folders /
  files, then Trash.
- Each item shows its icon; a small dot under the icon means "running".
- Hovering an item shows its name in a small label above it.
- **Magnification**: icons near the cursor grow smoothly (a bell-shaped curve over ~3 icons), on by default;
  size and magnification strength adjustable in Settings.
- Dark and light appearance follow the Windows theme (or a manual choice).

### 2.2 Using it
- **Click** a pinned app that isn't running → start it (with a short bounce animation while it starts).
- **Click** a running app → bring its window to the front; if it is already in front, minimise it; if it has
  several windows, cycle through them or show previews (see 2.3).
- **Right-click** an item → menu: Open / New window (when possible), Show in Explorer, Keep in Dock / Remove from
  Dock, Quit. Right-click on empty dock space → Add folder / app or file / website to Dock, Turn hiding on/off,
  Show all apps (Launcher), Dock settings.
- **Drag** an icon to reorder; drag it off the dock to remove (with a small "poof").
- **Drop** files onto an app icon to open them with that app; onto Trash to delete them (to the Recycle Bin).
- **Trash**: shows empty/full state; click opens the Recycle Bin; right-click → Empty Trash (with confirmation).
- **Folders** in the dock open a stack (see 2.4).
- **Website pins** open in the default browser and show the site's icon (fetched once, cached).
- **Web apps** installed from Chrome / Edge / Brave ("Install as app") count as their own apps in the dock.
- Clicking a dock item while the Launcher or Overview is open closes it first.

### 2.3 Window previews
- Hovering a running app for ~0.5 s shows live thumbnails of its windows above the icon; click one to focus it.

### 2.4 Folder stacks
- Clicking a dock folder opens its contents as a **Fan** (curved column of icons), **Grid** or **List**, chosen per
  user. Each entry opens on click; the top has "Open in Explorer". Option: open on hover instead of click.
- Folder colour tags (a few preset colours) shown on the folder icon.

### 2.5 Minimise animation
- Optional "genie" effect: a minimised window shrinks and flows into its dock icon; restoring reverses it.
  Off = plain minimise.

### 2.6 Auto-hide and the Windows taskbar
- Auto-hide: the dock slides away and comes back when the cursor touches the bottom edge.
- Option (on by default): hide the Windows taskbar while WinDock runs; always restored on quit.
- The dock, menu bar and Pill hide while a fullscreen app (video, game, F11 browser) is in front.

### 2.7 Pins file
- Pins are stored in a plain text file the user can edit (see section 13).

Acceptance: pin Notepad, quit it, click → it starts and bounces; click again while focused → it minimises; drag
an icon to the right → order is saved after restart; drop a .txt on Notepad → it opens.

---

## 3. Menu bar

- A thin translucent bar across the top of the primary screen; the work area starts below it (maximised windows
  don't cover it).
- **Left:** System menu (Windows logo), then the **name of the app in front** in bold, then app menus:
  File, Edit, View, Window, Help — each with working, generic actions (e.g. Edit → Undo, Cut, Copy, Paste,
  Select All sent to the front app; Window → Minimise, Zoom, Bring All to Front; File → Close Window).
- **Right (each can be turned on/off and reordered):** CPU %, RAM %, network speed, tray icons, Bluetooth, volume,
  Wi-Fi, battery, Search button, Quick Panel button, Notifications, screenshot / screen-record buttons, user
  picture, date and time.
- Clicking an item opens its small panel or the related Windows setting.

### 3.1 System menu (Windows logo)
In this order, like the macOS Apple menu:
- About This PC
- System Settings… · Microsoft Store…
- Recent Items…
- Force Quit… (`Ctrl+Shift+Esc`, opens Task Manager)
- Sleep · Restart… · Shut Down… (Restart / Shut Down / Log Out ask for confirmation)
- Lock Screen (`Win+L`) · Log Out *Full Name*…

Dock-related items are **not** here; they are in the dock's right-click menu.

### 3.2 Tray icons
- The app icons from the Windows notification area appear in the menu bar. Left-click brings the app's own window
  forward (or opens its menu); right-click opens its menu.
- Option to fold them behind a small arrow, and to choose which are shown.

Acceptance: switch between Notepad and Explorer → the bold app name changes; Edit → Copy in Notepad copies the
selection; Shut Down… asks before doing anything.

---

## 4. Launcher (all apps)

- Opens full screen over a blurred desktop: a grid of every installed app (Start Menu shortcuts + Store apps),
  with a search field at the top showing the **Windows logo** and the placeholder "Applications".
- Typing filters the grid instantly. Enter opens the first result. Esc or clicking empty space closes it.
- Category chips (All, Productivity, Creativity, Utilities, Games, …) filter the grid; a sort button switches
  between A–Z, most used and recently installed.
- Apps can be dragged onto the dock to pin them; folders of apps can be created by dropping one app on another.
- Opening: Launcher icon in the dock, **tapping the Windows key on its own**, or a shortcut (default `Win+Shift+A`).
  Win+*other key* shortcuts must keep working. Option "Windows key opens Start" restores the normal Start menu.

Acceptance: tap Win → Launcher opens; type "note" → Notepad is first; Enter → Notepad starts and the Launcher
closes; Win+E still opens Explorer.

---

## 5. Search

- `Win+Space` opens a centred search box (same look as the Launcher's search). It finds apps first, then files
  from the user's folders, then settings pages. Arrow keys move, Enter opens, Esc closes.
- `Win+Space` must not switch the keyboard language while WinDock runs.

---

## 6. Overview (all windows)

- Opens with `Win+Tab` (replacing Task View), a dock icon, or a 4/5-finger swipe up on a precision touchpad
  (swipe down closes).
- Shows every open window as a live thumbnail, arranged without overlap, with app icon and title. Click focuses
  the window and closes the Overview; Esc closes.

---

## 7. Pill (top-centre status pill)

- A small black pill at the top centre of the screen, over the menu bar. It springs open for:
  - **Notifications**: new Windows notifications (app icon, title, text) for a few seconds.
  - **Now playing**: current media (artwork, title, play/pause/next).
  - **Power**: plugged in / on battery, with the battery level.
  - **Low battery**: on battery at **30%, 20%, 10% and 5%**, a warning with a sound and the line
    "Plug in your charger" (at ≤10%: "Plug in your charger now – your PC will sleep soon"), shown for 8 s.
    Each level warns once per discharge; plugging in resets. If the Pill is off, hidden or busy for >15 s,
    a banner at the top of the screen shows it instead — the warning is never lost.
  - **Clipboard**: "Copied" with a preview; hovering shows recent clipboard items (kept in memory only).
- Hover expands it into a panel: date, time, battery ring, media controls, latest notifications, Clear.
- Can be turned off in Settings.

Acceptance: unplug the charger at 31% and wait → warning appears at 30% with sound; plug in → "Charging".

---

## 8. Quick Panel

- Opened from the menu bar. Tiles: Wi-Fi, Bluetooth, Do Not Disturb / Focus, Night light, Airplane mode,
  Battery saver, dark mode, screen brightness slider, volume slider, now playing, screenshot, screen recording,
  project/cast. Each tile uses the real Windows setting.
- Tiles can be added, removed and reordered (edit mode) and some can be resized.

---

## 9. Window buttons on every window

The core "Mac look". WinDock draws **red / yellow / green buttons at the top-left** of every normal window and
hides the Windows minimise / maximise / close buttons. It only draws *over* other apps; it never injects code
into them.

### 9.1 Behaviour of the buttons
- 12 px circles (at 100% scaling) 8 px apart, 13 px from the left edge, vertically centred in the title area.
- Hovering any of them shows ×, −, and ⤢ (or ⤡ when zoomed) symbols in all three.
- Red = close, yellow = minimise, green = zoom (maximise / restore). Inactive windows show grey buttons until
  hovered. Disabled actions (no minimise box etc.) stay grey.
- Only the circles take the mouse; everything else stays click-through, so dragging, snapping and double-click
  on the title bar keep working.
- Admin (elevated) windows, dialogs, tool windows, fullscreen windows and the desktop are left alone.
- Per-app opt-out (`windowbuttons exclude app.exe`) and a global off switch.

### 9.2 Standard title bars (Notepad, Control Panel, …)
- A Mac-style title bar is drawn over the Windows one: the bar colour of the theme, the title centred, the circles
  at the left, a hairline at the bottom. Rounded top corners when the window isn't maximised.

### 9.3 Apps that draw their own title bar (browsers, Electron apps, Explorer, Terminal, Office, …)
- WinDock finds where the app's own minimise / maximise / close glyphs are by looking at the window's pixels
  (three small glyphs, evenly spaced, at the top-right). It must ignore full-width frame lines (Chromium draws a
  1 px light line on top of non-maximised windows) and faint inactive glyphs must still be found.
- Those native buttons are covered by a patch painted with the bar's real background, row by row, so it blends
  in seamlessly (including gradients and Mica, sampled from the screen, not from a captured image that lacks
  Mica).
- **Title row (default):** above the window WinDock adds a ~30 px Mac title row with the circles at the left and
  the window title centred, in the colour of the app's own tab bar. The app's tabs and toolbar stay fully visible
  on the row below. The window is moved down to make room:
  - A maximised window becomes a normal window that fills the work area **below** the title row; the visible
    frame (not the invisible resize borders) lines up exactly with the screen edges.
  - Green button / double-click on the title row toggles between that and the window's previous size.
  - Dragging the title row moves the window.
  - Never fights the user: no adjustments while they drag or resize; gives up on an app that keeps moving itself.
  - When a window is maximised, room is made immediately (no flash of the Windows buttons).
  - Turn off globally or per app (`titlerow off`, `titlerow remove app.exe`).
- **Without the title row:** the circles sit at the top-left over the app's bar; small buttons the app had in that
  corner (e.g. a sidebar toggle) are shown again in the freed space at the right, each with its own hover
  highlight, and clicking one clicks the real button.
- **Plain Store-app bars** (Calculator, Clock): the whole bar is redrawn Mac-style with the title centred.
- Overlays follow their window instantly (move, resize, minimise, z-order) and are never drawn above a window
  that is in front of their own.
- When WinDock focuses a window it must not send keys that other apps use as hotkeys (e.g. a lone Alt).

Acceptance: open Notepad, Chrome, File Explorer, Calculator, Settings and Terminal; each shows the circles at the
top-left and no Windows buttons; red closes, yellow minimises, green zooms; Chrome maximised fills the screen
under its title row edge to edge; dragging Chrome by its tab strip still works; a background window's buttons
never show through a window in front.

---

## 10. Files (file browser)

A Mac-style file browser that the dock's file-manager icon opens (option: open File Explorer instead).

- Own window chrome: circles, back/forward, folder title, icon/list view toggle, search field.
- Sidebar: Favourites (Home, Desktop, Documents, Downloads, Pictures, Music, Videos) and Locations (drives).
- Icon view (large icons, two-line names) and list view (Name, Date Modified, Size, Kind), sortable.
- Hidden, system and dot-files are not shown.
- Actions: open (double-click / Enter), rename (F2), Move to Trash (Delete → Recycle Bin), new folder
  (Ctrl+Shift+N), copy/cut/paste (Ctrl+C/X/V, compatible with Explorer's clipboard), Back (Backspace, Alt+←),
  Forward (Alt+→), enclosing folder (Alt+↑), new window / close window (Ctrl+N / Ctrl+W), icon/list view
  (Ctrl+1 / Ctrl+2), search (Ctrl+F, filters the current folder).
- Context menus: on a file — Open, Copy Path, Show in Explorer, Rename, Move to Trash; on empty space — Paste,
  New Folder, Open in Explorer.
- Runs as its own process (`WinDock.exe --files [folder]`), one instance; a second launch opens the folder in the
  existing one.

---

## 11. Settings window

### 11.1 Look and layout

A dark (or light, following the theme) window in the style of the macOS "System Settings" app, about
560 × 620 px at 100% scaling, opened from the System menu, the dock's right-click menu or the tray.

```
┌──────────────────┬──────────────────────────────────────────┐
│ ● ● ●            │ ‹ ›  Dock                                │
│ [ Search       ] │                                          │
│ (◉) Your Name    │ Dock                                     │
│     WinDock      │ ┌──────────────────────────────────────┐ │
│     settings     │ │      live preview of the dock        │ │
│                  │ └──────────────────────────────────────┘ │
│ WINDOCK          │ ┌──────────────────────────────────────┐ │
│ ⚙ General        │ │ Size                    ●─────────── │ │
│ ◐ Appearance     │ │ Magnification                   (●=) │ │
│ ▭ Dock     ◀sel  │ │ Magnification amount    ──────────●  │ │
│ ▭ Menu Bar & Pill│ └──────────────────────────────────────┘ │
│ ▦ Launcher       │  grey footnote explaining the group      │
│ ★ Icons          │                                          │
│ ──────────────── │ ┌──────────────────────────────────────┐ │
│ SYSTEM           │ │ Show Dock                       (=●) │ │
│ ▭ Display & Sound│ │ Minimise effect             Fade ⌃⌄  │ │
│ ⌔ Network & BT   │ │ Open folder fans on click ...  (●=)  │ │
│ ▭ Power & Battery│ └──────────────────────────────────────┘ │
│ …                │ Windows taskbar                          │
└──────────────────┴──────────────────────────────────────────┘
```

- **Window chrome:** WinDock's own red / yellow / green buttons at the top-left of the sidebar; no Windows title
  bar. Rounded corners, drop shadow.
- **Sidebar (left, ~170 px, slightly lighter than the content):**
  - A rounded **Search** field at the top; typing filters to matching settings across all pages and jumps to them.
  - An **account row**: the Windows account picture (or a coloured circle with the initial), the user's name and a
    small grey "WinDock settings" subtitle.
  - Two groups with small grey all-caps headers:
    - **WINDOCK:** General, Appearance, Dock, Menu Bar & Pill, Launcher, Icons, Window Buttons.
    - **SYSTEM:** shortcuts into Windows' own settings, opened on the right as WinDock pages where useful and
      otherwise opening the Windows Settings page: Display & Sound, Network & Bluetooth, Power & Battery,
      Personalization, Notifications & Focus, Privacy & Security, Apps, Accounts, Time & Language,
      Windows Update, File Explorer, About This PC.
  - Each entry has a small coloured icon (drawn in code, rounded-square background, white glyph) and a label.
    The selected entry is a rounded **blue** highlight with white text.
- **Content (right):**
  - Top bar: back / forward arrows (history of visited pages) and the **page title** in large semibold text.
  - Sections, each with a bold heading (e.g. "Dock", "Windows taskbar", "Status readouts",
    "Order of items on the right", "Icon style", "Custom icon pack").
  - Rows sit in **rounded grouped cards** (slightly lighter than the background), separated by hairlines:
    - **Toggle row:** label left, blue/grey switch right.
    - **Slider row:** label left, slider right (live).
    - **Picker row:** label left, current value and an up/down chevron right; click opens a small menu.
    - **Link row:** label left, chevron `›` right; click performs the action or opens a dialog.
    - **Info row:** label left, grey value right (e.g. "Installed icons — 0 in custom-pack").
  - A small **grey footnote** under a card explains what the group does, in one or two sentences.
  - The **Dock** page starts with a live preview of the dock that reacts to size and magnification.
  - The page scrolls; the sidebar stays put.
- **Behaviour:** every change applies immediately and is saved; no OK / Apply buttons. Opening a page remembers the
  scroll position. Esc or the red button closes the window.

### 11.2 Pages and their rows

- **General:** open at login, force stop WinDock, open configuration file, open data folder, back up / restore
  settings (zip), scan tray icons.
- **Appearance:** match Windows light/dark, or force dark/light; transparency effects; font.
- **Dock:** live preview; Size (slider), Magnification (toggle), Magnification amount (slider), footnote. Then
  Show Dock, Auto-hide Dock, Show Launcher icon in Dock, Genie effect, Minimise windows into the Dock (as
  thumbnails), Show window previews on hover, Minimise effect (picker: Genie / Scale / Fade), Open folder fans on
  click instead of hover, footnote about auto-hide. Section **Windows taskbar**: Hide the Windows taskbar while
  WinDock runs. Then add folder / app / website, edit dock items.
- **Menu Bar & Pill:**
  - **Status readouts:** CPU usage, Memory (RAM) usage, Disk usage, Network speed, Network speed shows (picker:
    Download / Upload / Download and upload), Arrows next to network speed, Temperature (only if the PC reports
    it), Weather (off by default; uses the network). Footnote: click a readout to open Task Manager; CPU and
    memory turn orange above 80% and red above 90%.
  - **Order of items on the right:** one picker row per item (Account picture, Date & time, Notifications,
    Screenshot button, Screen record button, Quick Panel, Search, Battery, Network, …) with its position
    number ("1 (far right)", 2, 3, …).
  - Show Pill; screenshot and screen-record buttons and tiles; tray icons (show, arrow, collapsible, which ones);
    notification and Focus settings links; keyboard shortcuts (Search, Overview, Launcher; record a new shortcut
    by pressing it).
- **Launcher:** categories, sort, Windows-key behaviour (Launcher or Start).
- **Icons:**
  - **Icon style:** Use original app icons (master switch), with a footnote explaining it overrides styled and
    custom icons everywhere (dock, Launcher, Overview, notifications) while per-app choices still win.
  - **Custom icon pack:** Installed icons (info), Install icon pack from folder…, Install icon pack from .zip…,
    Open custom pack folder, Remove custom pack, with a footnote describing the format (one image per app, named
    like the app or its exe; .png, .ico, .icns or .jpg; square, 256 px or larger).
  - **Icon pack:** Use styled icons; Restart WinDock now.
  - **Online icon lookup:** optional, off by default, needs an API key.
- **Window Buttons:** on/off, title row on/off, per-app exclusions.
- **Gestures:** 4/5-finger swipes on/off.
- **Power & Battery:** battery level and state, Battery saver on/off and threshold (picker), power mode,
  low-battery warnings on/off.
- **File Explorer:** show file name extensions, show hidden files, folder options.
- **About This PC:** device name, Windows version, processor, memory; Task Manager, Sleep, Lock, Restart…,
  Shut down….

---

## 12. Other features

- **Screenshot / screen recording:** menu bar buttons and Quick Panel tiles; screenshots of an area/window/screen
  saved to Pictures\Screenshots; recording via the Windows Game Bar or a bundled-free method.
- **Touchpad gestures:** 4/5-finger swipe up/down for the Overview (precision touchpads).
- **User picture** in the menu bar and Settings, taken from the Windows account (or chosen from a file).
- **Low-battery warnings** (see 7).
- **Wallpapers:** original wave wallpapers drawn by a script (dark / light, any size) and a script to set one.
- **Cursors (optional):** a script that installs a user-supplied cursor pack for the current user only (no admin,
  no system folders) after backing up the current cursors, and a script to restore them.
- **File sharing (optional, off by default):** send files to nearby devices on the local network using the
  open LocalSend protocol. When off, no port is opened and no Explorer menu entries exist. When turned on,
  WinDock says which port it opens.

---

## 13. Configuration file

`%APPDATA%\WinDock\windock.txt`, UTF-8, one entry per line, `#` starts a comment. Changes apply after restart (or
live where noted). Unknown lines are ignored.

```
# Dock items, in order
Notepad | C:\Windows\notepad.exe        # Label | target
C:\Users\me\Documents                     # a folder or file
https://example.com                       # a website
ms-settings:display                       # a Settings page
---                                       # divider: right-hand section starts here

# Switches
hide-taskbar = on | off
windowbuttons = on | off
windowbuttons exclude = chrome.exe        # repeatable
titlerow = on | off
titlerow remove = claude.exe              # repeatable
winkey = launcher | start
files = on | off                          # dock's file-manager icon opens Files or Explorer
filesharing = off | on
hotkey-search = Win+Space
hotkey-overview = Win+Tab
hotkey-launcher = Win+Shift+A
icon-lookup-key = YOUR_KEY                # optional online icon lookup
```

Other state (Launcher order, Quick Panel layout, icon choices) lives in separate files in the same folder and is
created by WinDock itself.

---

## 14. Quality targets

- Idle CPU ≈ 0%; idle memory under ~60 MB working set.
- Animations at the display's refresh rate; no visible stutter when magnifying the dock.
- Correct at 100%–200% scaling and on multiple monitors with different scaling.
- Never leaves Windows in a changed state after quitting (taskbar, window positions, frame colours).
- Never blocks typing: global hooks must return quickly and only swallow the keys they act on.

---

## 15. Build order (milestones)

Each milestone is usable on its own.

1. **Skeleton:** single exe, install/uninstall scripts, config file, Settings window shell, tray-safe quit and
   `/restore`.
2. **Dock** (section 2) + hiding the taskbar.
3. **Menu bar** with System menu and app menus (section 3).
4. **Launcher + Search** (sections 4–5), Windows-key behaviour.
5. **Window buttons** incl. title row (section 9).
6. **Pill** incl. low-battery warnings (section 7) and **Quick Panel** (section 8).
7. **Overview** and gestures (section 6).
8. **Files** (section 10).
9. Remaining Settings pages, screenshots/recording, wallpapers/cursors, optional file sharing.
