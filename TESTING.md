# On-screen checks (milestones 1–2)

Run on 2026-11-24 on Windows 11, 1920×1080 at 125 %, after Smart App Control was turned off. MacDock was
stopped first. ✅ = passed on screen. ⬜ = still to check by hand (the remote-control tooling could not do it).

## Section 1: install, start, quit
- ✅ Double-click `Install.bat` in File Explorer → the dock appears in about 2 s, and the taskbar is hidden.
- ✅ A Start Menu shortcut "WinDock" and the start-at-login entry are created.
- ✅ Starting WinDock again shows its Settings window in front, and no second dock appears.
- ✅ Dock right-click → Quit WinDock → the taskbar is shown again with the auto-hide setting it had before, and the
  work area goes back to full height.
- ✅ Kill WinDock.exe → `WinDock.exe /restore` → the taskbar is shown, and the crash-state files are removed.
- ✅ `Uninstall.bat` → WinDock stops, the install folder, shortcut and autostart are removed, and settings are kept.

## Section 2: dock
- ✅ Pinned Notepad: click → it starts. Click while it is in front → it minimises with the genie effect. Click again →
  it is restored with the reverse genie.
- ✅ Drag an icon to a new place → the order is saved in `windock.txt`.
- ✅ Magnification follows the pointer, the name label shows on hover, and running dots show.
- ✅ Right-click menus: empty space, Trash, folder (with View Content As / Sort By / Colour Tag submenus).
- ✅ Add Website → the site's icon is fetched once and shown on a white tile.
- ✅ Folder stacks: Fan, Grid and List. The fan fits the screen height and links to the rest in Explorer. Colour tag dot on the folder icon.
- ✅ Window previews after hovering a running app, including apps with several windows.
- ✅ Drag an icon off the dock → "Remove" label, a "poof", and it is removed.
- ✅ Auto-hide: hides, comes back at the bottom edge, and hides again. Turning it off works from the menu.
- ✅ Maximised windows stop above the dock (its space is reserved at the bottom).
- ✅ Trash shows full or empty.
- ✅ Editing `windock.txt` by hand applies right away (dock size changed back live).
- ✅ Idle CPU about 0.05 %. Working set about 13 MB after start-up trimming (about 70 MB before trimming).
- ⬜ Drop a .txt on Notepad → it opens. Drop a file on Trash → it goes to the Recycle Bin. (OLE drag from File
  Explorer was not possible with the test tooling.)
- ⬜ Empty Trash shows Windows' own confirmation. (Not tried: the Recycle Bin held the user's own files.)
- ⬜ A browser web app ("Install as app") shows as its own dock item.
- ⬜ A fullscreen video or F11 browser hides the dock.

## Settings window (11.1)
- ✅ Rounded window with no Windows title bar. Red button and Esc close it.
- ✅ Sidebar search finds "Hide the Windows taskbar…" and jumps to it with a highlight.
- ✅ The Size slider resizes the real dock live. The preview shows fewer icons at the chosen size.
- ✅ Appearance → Light / Match Windows switch both Settings and the dock live.
- ✅ The "Hide the Windows taskbar" switch shows and hides the taskbar live.
