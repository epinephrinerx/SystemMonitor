@echo off
setlocal
cd /d "%~dp0"

rem Builds the System Monitor widget and its installer into dist-wpf\.

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

echo.
echo [1/4] Tests...
"%DOTNET%" test SysMonitor.Tests --nologo -v quiet || exit /b 1

echo.
echo [2/4] Publishing SystemMonitor.exe...
rem Framework-dependent on purpose: .NET 8 Desktop is already present, so the
rem executable stays under a megabyte. Self-contained would be 70-150 MB.
"%DOTNET%" publish SysMonitor.Wpf -c Release -r win-x64 --self-contained false ^
    -p:PublishSingleFile=true --nologo -v quiet || exit /b 1

echo.
echo [3/4] Building the installer...
if not exist "SysMonitor.Setup\payload" mkdir "SysMonitor.Setup\payload"
copy /y "SysMonitor.Wpf\%PUBLISH%\SystemMonitor.exe" "SysMonitor.Setup\payload\SystemMonitor.exe" >nul || exit /b 1
rem The payload is an embedded resource, so the setup must rebuild whenever
rem the application does.
"%DOTNET%" publish SysMonitor.Setup -c Release -r win-x64 --self-contained false ^
    -p:PublishSingleFile=true --nologo -v quiet || exit /b 1

echo.
echo [4/4] Collecting...

rem The version is read back off the executable that was just built, so the
rem names cannot drift from what is actually inside the file.
for /f "usebackq delims=" %%v in (`"%DOTNET%" msbuild SysMonitor.Wpf -nologo -getProperty:Version`) do set "VERSION=%%v"
if not defined VERSION (
    echo ERROR: could not read the version from the project.
    exit /b 1
)

rem dist-wpf is never emptied: every build that has been made stays on the
rem machine, and the version in the name keeps them apart.
rem Only the distributed copies are named with the version. From 4.0 on the
rem installer lays down SystemMonitor.exe; the pre-4.0 copies (SysMonitor.exe
rem under the SysMonitor.NET folder) are cleaned up by the installer's
rem legacy-build removal.
if not exist "%OUT%" mkdir "%OUT%"
copy /y "SysMonitor.Wpf\%PUBLISH%\SystemMonitor.exe" "%OUT%\SystemMonitor-%VERSION%.exe" >nul || exit /b 1
copy /y "SysMonitor.Setup\%PUBLISH%\SystemMonitor-Setup.exe" "%OUT%\SystemMonitor-Setup-%VERSION%.exe" >nul || exit /b 1

echo.
echo Done.  version %VERSION%
for %%F in ("%OUT%\SystemMonitor-%VERSION%.exe" "%OUT%\SystemMonitor-Setup-%VERSION%.exe") do @echo   %%~nxF  %%~zF bytes
echo.
echo Install silently with:  dist-wpf\SystemMonitor-Setup-%VERSION%.exe /S
echo Run it from PowerShell, not Git Bash: Git Bash rewrites a bare /S into a path.
endlocal
