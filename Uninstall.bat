@echo off
rem Removes WinDock: stops it, restores the Windows taskbar, removes autostart, the Start Menu shortcut and
rem %LOCALAPPDATA%\WinDock. Your settings in %APPDATA%\WinDock are kept (delete that folder yourself if you like).
setlocal
set DIR=%LOCALAPPDATA%\WinDock
if exist "%DIR%\WinDock.exe" (
  "%DIR%\WinDock.exe" /uninstall
) else if exist "%~dp0bin\WinDock.exe" (
  "%~dp0bin\WinDock.exe" /uninstall
)
timeout /t 1 >nul
if exist "%DIR%" rmdir /s /q "%DIR%"
echo.
echo WinDock has been removed. Settings were kept in %APPDATA%\WinDock.
timeout /t 5 >nul
