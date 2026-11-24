@echo off
rem Installs WinDock for the current user (no admin rights): %LOCALAPPDATA%\WinDock, Start Menu shortcut,
rem start at login (on by default; "Install.bat noautostart" turns it off). Settings stay in %APPDATA%\WinDock.
setlocal
cd /d "%~dp0"
if not exist bin\WinDock.exe (
  echo Building WinDock first...
  call build.bat || goto :fail
)
bin\WinDock.exe /install %1 || goto :fail
echo.
echo Done. The dock appears at the bottom of the screen. Quit it from the dock's right-click menu or Settings.
timeout /t 5 >nul
exit /b 0
:fail
echo.
echo WinDock could not be installed. See the messages above.
pause
exit /b 1
