# Changelog

## 0.2.0 — first public release

**Dock**
- Magnification, name labels, running dots; click to open (bounce) and to minimise / restore with a genie effect.
- Live window previews, including apps with several windows.
- Folder stacks as Fan, Grid or List, with colour tags and "Open in Explorer".
- Drag to reorder, drag off to remove; Trash with full / empty state; website pins with the site's icon.
- Auto-hide; maximised windows stop above the dock; light and dark appearance.
- Hides the Windows taskbar while running and always restores it on quit (`/restore` after a crash).

**Settings**
- System Settings–style window with search; dock size, magnification, appearance and taskbar hiding apply live.
- Settings in a plain text file (`%APPDATA%\WinDock\windock.txt`), reloaded when edited by hand.

**Install**
- New `WinDock-Setup.exe`: per-user installer, no admin rights, silent mode (`/S`).
- WinDock now appears in Settings → Apps → Installed apps; Uninstall there removes the program completely
  (settings are kept).
- Portable zip with `Install.bat` / `Uninstall.bat` for people who prefer no installer.
