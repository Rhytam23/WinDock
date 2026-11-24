@echo off
rem Builds the public release files into dist\ (no SDK needed, only the C# compiler that ships with Windows):
rem   WinDock-Setup-<version>.exe     installer for end users (per user, no admin)
rem   WinDock-<version>-portable.zip  WinDock.exe + Install.bat / Uninstall.bat
rem   SHA256SUMS.txt                  checksums of both
rem Nothing is installed or run here.
setlocal
cd /d "%~dp0"
call "%~dp0build.bat" || exit /b 1

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe

for /f %%v in ('powershell -NoProfile -Command "$v=(Get-Item bin\WinDock.exe).VersionInfo.FileVersion.Split('.'); $v[0..2] -join '.'"') do set VER=%%v
if not defined VER ( echo Could not read the WinDock version. & exit /b 1 )
echo Version %VER%

if exist dist rmdir /s /q dist
mkdir dist

rem Setup's own version info = WinDock's version
> obj\SetupVersion.cs echo [assembly: System.Reflection.AssemblyTitle("WinDock Setup")]
>> obj\SetupVersion.cs echo [assembly: System.Reflection.AssemblyProduct("WinDock")]
>> obj\SetupVersion.cs echo [assembly: System.Reflection.AssemblyDescription("WinDock Setup")]
>> obj\SetupVersion.cs echo [assembly: System.Reflection.AssemblyCopyright("MIT licence")]
>> obj\SetupVersion.cs echo [assembly: System.Reflection.AssemblyVersion("%VER%.0")]
>> obj\SetupVersion.cs echo [assembly: System.Reflection.AssemblyFileVersion("%VER%.0")]

set ICON=
if exist obj\windock.ico set ICON=/win32icon:obj\windock.ico
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /out:dist\WinDock-Setup-%VER%.exe /win32manifest:installer\setup.manifest %ICON% /resource:bin\WinDock.exe,WinDock.exe /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll installer\Setup.cs obj\SetupVersion.cs || exit /b 1
echo Built dist\WinDock-Setup-%VER%.exe
rem the same file without a version number, so ".../releases/latest/download/WinDock-Setup.exe" always works
copy /y dist\WinDock-Setup-%VER%.exe dist\WinDock-Setup.exe >nul

set STAGE=obj\portable\WinDock-%VER%
if exist obj\portable rmdir /s /q obj\portable
mkdir "%STAGE%\bin"
copy /y bin\WinDock.exe "%STAGE%\bin\" >nul
copy /y Install.bat "%STAGE%\" >nul
copy /y Uninstall.bat "%STAGE%\" >nul
copy /y README.md "%STAGE%\" >nul
copy /y LICENSE "%STAGE%\LICENSE.txt" >nul
rem (entries written one by one so they use "/" separators, which every unzip tool understands)
powershell -NoProfile -Command "Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem; $root=(Resolve-Path 'obj\portable').Path; $zip=[IO.Compression.ZipFile]::Open((Join-Path (Resolve-Path dist).Path 'WinDock-%VER%-portable.zip'), 'Create'); Get-ChildItem $root -Recurse -File | ForEach-Object { $name=$_.FullName.Substring($root.Length+1).Replace('\','/'); [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $name, 'Optimal') }; $zip.Dispose()" || exit /b 1
echo Built dist\WinDock-%VER%-portable.zip

powershell -NoProfile -Command "Get-ChildItem dist -File | Where-Object Name -ne 'SHA256SUMS.txt' | ForEach-Object { (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower() + '  ' + $_.Name } | Set-Content -Encoding ascii dist\SHA256SUMS.txt"
type dist\SHA256SUMS.txt
