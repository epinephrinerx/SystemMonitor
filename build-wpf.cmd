@echo off
setlocal
cd /d "%~dp0"

rem Builds the System Monitor widget, extensions, pack tool, and installer into dist-wpf\.

if not defined DOTNET set "DOTNET=dotnet"
set "PUBLISH=bin\Release\net8.0-windows\win-x64\publish"
set "OUT=%~dp0dist-wpf"

echo ============================================================
echo  Building System Monitor (C# / WPF)
echo ============================================================

"%DOTNET%" --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: the .NET SDK is not on PATH. Set DOTNET to its dotnet.exe.
    exit /b 1
)

rem Read the version from the WPF project upfront so both the packaging tool and
rem the distributed binary names stay perfectly aligned.
for /f "usebackq delims=" %%v in (`"%DOTNET%" msbuild SysMonitor.Wpf -nologo -getProperty:Version`) do set "VERSION=%%v"
if not defined VERSION (
    echo ERROR: could not read the version from the project.
    exit /b 1
)

echo.
echo [1/5] Tests...
"%DOTNET%" test SysMonitor.Tests --nologo -v quiet || exit /b 1

echo.
echo [2/5] Building and packaging extensions...
"%DOTNET%" build SysMonitor.Ext.Smart -c Release --nologo -v quiet || exit /b 1
"%DOTNET%" build SysMonitor.Ext.Sample -c Release --nologo -v quiet || exit /b 1
"%DOTNET%" build SysMonitor.PackTool -c Release --nologo -v quiet || exit /b 1

rem Staging folder in the repo root, ignored by git.
rem A leftover here would end up in the installer or, worse, in the first-party allowlist
rem (the packer merges into an existing file), so each removal is checked.
if exist "build-ext" rmdir /s /q "build-ext"
if exist "build-ext" (
    echo ERROR: could not remove build-ext.
    exit /b 1
)
if exist "SysMonitor.Wpf\first-party-extensions.json" del /f /q "SysMonitor.Wpf\first-party-extensions.json"
if exist "SysMonitor.Wpf\first-party-extensions.json" (
    echo ERROR: could not remove the old first-party-extensions.json.
    exit /b 1
)

set "PACKTOOL=SysMonitor.PackTool\bin\Release\net8.0\SysMonitor.PackTool.exe"

rem DiskInfoCore.dll is optional: it lets the app read SMART for disks Windows will
rem not report a temperature for (USB bridges, external NVMe). Point DISKINFOCORE at a
rem build of it, or leave it to find the sibling DiskInfo checkout; without one the setup
rem is built without it and the app simply does without.
if not defined DISKINFOCORE set "DISKINFOCORE=%~dp0..\DiskInfo\DiskInfoCore\Build\Releasex64\DiskInfoCore.dll"
rem The path is quoted where it is used and never expanded inside a parenthesised block, so a
rem folder name with spaces or parentheses cannot split the command line.
set "HAVE_CORE="
if exist "%DISKINFOCORE%" set "HAVE_CORE=1"
if defined HAVE_CORE echo   including DiskInfoCore.dll in the SMART extension package
if not defined HAVE_CORE echo   DiskInfoCore.dll not found: packaging SMART without the native fallback

if defined HAVE_CORE "%PACKTOOL%" --template SysMonitor.Ext.Smart\extension.template.json --out build-ext\smart --version %VERSION% --file "SysMonitor.Ext.Smart.dll=SysMonitor.Ext.Smart\bin\Release\net8.0\SysMonitor.Ext.Smart.dll" --file "DiskInfoCore.dll=%DISKINFOCORE%" --allowlist SysMonitor.Wpf\first-party-extensions.json || exit /b 1
if not defined HAVE_CORE "%PACKTOOL%" --template SysMonitor.Ext.Smart\extension.template.json --out build-ext\smart --version %VERSION% --file "SysMonitor.Ext.Smart.dll=SysMonitor.Ext.Smart\bin\Release\net8.0\SysMonitor.Ext.Smart.dll" --allowlist SysMonitor.Wpf\first-party-extensions.json || exit /b 1

"%PACKTOOL%" --template SysMonitor.Ext.Sample\extension.template.json --out build-ext\sample --version %VERSION% --file SysMonitor.Ext.Sample.dll=SysMonitor.Ext.Sample\bin\Release\net8.0\SysMonitor.Ext.Sample.dll || exit /b 1

if not exist "SysMonitor.Wpf\first-party-extensions.json" (
    echo ERROR: first-party-extensions.json was not created after packing SMART extension.
    exit /b 1
)

echo.
echo [3/5] Publishing SystemMonitor.exe...
rem Framework-dependent on purpose: .NET 8 Desktop is already present, so the
rem executable stays under a megabyte. Self-contained would be 70-150 MB.
rem Embedding first-party-extensions.json happens here because it now exists.
"%DOTNET%" publish SysMonitor.Wpf -c Release -r win-x64 --self-contained false ^
    -p:PublishSingleFile=true --nologo -v quiet || exit /b 1

echo.
echo [4/5] Building the installer...
if not exist "SysMonitor.Setup\payload" mkdir "SysMonitor.Setup\payload"
copy /y "SysMonitor.Wpf\%PUBLISH%\SystemMonitor.exe" "SysMonitor.Setup\payload\SystemMonitor.exe" >nul || exit /b 1
if exist "SysMonitor.Setup\payload\DiskInfoCore.dll" del "SysMonitor.Setup\payload\DiskInfoCore.dll"

if exist "SysMonitor.Setup\payload\extensions" rmdir /s /q "SysMonitor.Setup\payload\extensions"
if exist "SysMonitor.Setup\payload\extensions" (
    echo ERROR: could not clear SysMonitor.Setup\payload\extensions; a stale file would be installed.
    exit /b 1
)
mkdir "SysMonitor.Setup\payload\extensions\smart"
xcopy /e /y /q "build-ext\smart\*" "SysMonitor.Setup\payload\extensions\smart\" >nul || exit /b 1

rem The payload is an embedded resource, so the setup must rebuild whenever
rem the application does.
"%DOTNET%" publish SysMonitor.Setup -c Release -r win-x64 --self-contained false ^
    -p:PublishSingleFile=true --nologo -v quiet || exit /b 1

echo.
echo [5/5] Collecting...

rem dist-wpf is never emptied: every build that has been made stays on the
rem machine, and the version in the name keeps them apart.
rem Only the distributed copies are named with the version. From 4.0 on the
rem installer lays down SystemMonitor.exe; the pre-4.0 copies (SysMonitor.exe
rem under the SysMonitor.NET folder) are cleaned up by the installer's
rem legacy-build removal.
if not exist "%OUT%" mkdir "%OUT%"
copy /y "SysMonitor.Wpf\%PUBLISH%\SystemMonitor.exe" "%OUT%\SystemMonitor-%VERSION%.exe" >nul || exit /b 1
copy /y "SysMonitor.Setup\%PUBLISH%\SystemMonitor-Setup.exe" "%OUT%\SystemMonitor-Setup-%VERSION%.exe" >nul || exit /b 1

if exist "%OUT%\sample-extension" rmdir /s /q "%OUT%\sample-extension"
mkdir "%OUT%\sample-extension\sample"
xcopy /e /y /q "build-ext\sample\*" "%OUT%\sample-extension\sample\" >nul || exit /b 1

echo.
echo Done.  version %VERSION%
for %%F in ("%OUT%\SystemMonitor-%VERSION%.exe" "%OUT%\SystemMonitor-Setup-%VERSION%.exe") do @echo   %%~nxF  %%~zF bytes
echo.
echo To try the sample extension:
echo   1. Install, then copy dist-wpf\sample-extension\sample into
echo      %%LOCALAPPDATA%%\Programs\SystemMonitor\extensions\
echo   2. Start System Monitor and find "Sample extension" in the Extensions list
echo      of the side panel
echo   3. Use its approve button and confirm the UAC prompt yourself
echo.
echo Install silently with:  dist-wpf\SystemMonitor-Setup-%VERSION%.exe /S
echo Run it from PowerShell, not Git Bash: Git Bash rewrites a bare /S into a path.
endlocal
