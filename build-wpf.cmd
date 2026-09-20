@echo off
setlocal
cd /d "%~dp0"

rem Builds the C# widget and its installer into dist-wpf\.
rem The Python build has its own build.cmd; this one does not touch it.

if not defined DOTNET set "DOTNET=dotnet"
set "PUBLISH=bin\Release\net8.0-windows\win-x64\publish"
set "OUT=%~dp0dist-wpf"

echo ============================================================
echo  Building SysMonitor (C# / WPF)
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
echo [2/4] Publishing SysMonitor.exe...
rem Framework-dependent on purpose: .NET 8 Desktop is already present, so the
rem executable stays under a megabyte. Self-contained would be 70-150 MB.
"%DOTNET%" publish SysMonitor.Wpf -c Release -r win-x64 --self-contained false ^
    -p:PublishSingleFile=true --nologo -v quiet || exit /b 1

echo.
echo [3/4] Building the installer...
if not exist "SysMonitor.Setup\payload" mkdir "SysMonitor.Setup\payload"
copy /y "SysMonitor.Wpf\%PUBLISH%\SysMonitor.exe" "SysMonitor.Setup\payload\SysMonitor.exe" >nul || exit /b 1
rem The payload is an embedded resource, so the setup must rebuild whenever
rem the application does.
"%DOTNET%" publish SysMonitor.Setup -c Release -r win-x64 --self-contained false ^
    -p:PublishSingleFile=true --nologo -v quiet || exit /b 1

echo.
echo [4/4] Collecting...
if not exist "%OUT%" mkdir "%OUT%"
copy /y "SysMonitor.Wpf\%PUBLISH%\SysMonitor.exe" "%OUT%\SysMonitor.exe" >nul || exit /b 1
copy /y "SysMonitor.Setup\%PUBLISH%\SysMonitor-Setup.exe" "%OUT%\SysMonitor-Setup.exe" >nul || exit /b 1

echo.
echo Done.
for %%F in ("%OUT%\SysMonitor.exe" "%OUT%\SysMonitor-Setup.exe") do @echo   %%~nxF  %%~zF bytes
echo.
echo Install silently with:  dist-wpf\SysMonitor-Setup.exe /S
echo Run it from PowerShell, not Git Bash: Git Bash rewrites a bare /S into a path.
endlocal
