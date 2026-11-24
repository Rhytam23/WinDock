@echo off
rem Builds WinDock.exe with the C# compiler that ships with Windows (.NET Framework 4.x). No SDK needed.
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find csc.exe from the .NET Framework 4.x. It is part of Windows 10 and 11.
  exit /b 1
)
if not exist obj mkdir obj
if not exist bin mkdir bin
set REFS=/r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:Microsoft.VisualBasic.dll

rem 1) WinDock's own icon, drawn in code (optional: skipped if the helper is not allowed to run)
set ICON=
if not exist obj\windock.ico (
  "%CSC%" /nologo /target:exe /out:obj\MakeIcon.exe /r:System.Drawing.dll /r:System.Core.dll tools\MakeIcon.cs src\Glyphs.cs src\Theme.cs src\Config.cs >nul && obj\MakeIcon.exe obj\windock.ico 2>nul
)
if exist obj\windock.ico set ICON=/win32icon:obj\windock.ico
if not defined ICON echo Note: building without an embedded file icon (the icon helper could not run).

rem 2) the app
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /out:bin\WinDock.exe /win32manifest:src\app.manifest %ICON% %REFS% /recurse:src\*.cs || exit /b 1
echo Built bin\WinDock.exe
