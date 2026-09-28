@echo off
setlocal
cd /d "%~dp0"
set "APP_ONLY=0"
if /I "%~1"=="--app-only" set "APP_ONLY=1"
if not defined SYSMONITOR_PYTHON set "SYSMONITOR_PYTHON=python"

echo ============================================================
echo  Building SysMonitor %~n0
echo ============================================================

"%SYSMONITOR_PYTHON%" -c "import sys; print(sys.executable); print(sys.version)"
if errorlevel 1 (
    echo ERROR: Set SYSMONITOR_PYTHON to the Python executable used for this build.
    exit /b 1
)

rem Tcl/Tk 9.0.4 panicked in the user's build despite source and startup checks.
rem Package the 8.6 compatibility runtime until the native crash is understood.
"%SYSMONITOR_PYTHON%" -c "import _tkinter,sys; print('Build Tcl/Tk:', _tkinter.TCL_VERSION, _tkinter.TK_VERSION); sys.exit(0 if (_tkinter.TCL_VERSION,_tkinter.TK_VERSION)==('8.6','8.6') else 1)"
if errorlevel 1 (
    echo ERROR: This build requires Tcl/Tk 8.6. Select its Python with SYSMONITOR_PYTHON.
    exit /b 1
)

"%SYSMONITOR_PYTHON%" -c "import PyInstaller" >nul 2>&1
if errorlevel 1 (
    echo ERROR: PyInstaller is not installed for the selected Python. No packages were installed.
    exit /b 1
)

echo.
echo [1/4] Generating icon...
if "%APP_ONLY%"=="1" goto buildapp
"%SYSMONITOR_PYTHON%" -c "import PIL" >nul 2>&1
if errorlevel 1 (
    echo   Pillow not installed - keeping existing SysMonitor.ico
) else (
    "%SYSMONITOR_PYTHON%" tools\make_icon.py || exit /b 1
)

:buildapp
echo.
echo [2/4] Building SysMonitor.exe...
"%SYSMONITOR_PYTHON%" -m PyInstaller --noconfirm --clean --onefile --noconsole ^
    --name SysMonitor ^
    --icon "%~dp0SysMonitor.ico" ^
    --version-file "%~dp0version_info.txt" ^
    --exclude-module PIL --exclude-module numpy --exclude-module pandas ^
    --exclude-module unittest --exclude-module pydoc --exclude-module email ^
    --distpath "%~dp0dist" --workpath "%~dp0build" --specpath "%~dp0build" ^
    "%~dp0SysMonitor.pyw" || exit /b 1

if "%APP_ONLY%"=="1" goto appdone

echo.
echo [3/4] Building SysMonitor-Setup.exe...
"%SYSMONITOR_PYTHON%" -m PyInstaller --noconfirm --clean --onefile --noconsole ^
    --name SysMonitor-Setup ^
    --icon "%~dp0SysMonitor.ico" ^
    --add-binary "%~dp0dist\SysMonitor.exe;." ^
    --add-data "%~dp0SysMonitor.ico;." ^
    --exclude-module PIL --exclude-module numpy --exclude-module pandas ^
    --distpath "%~dp0dist" --workpath "%~dp0build" --specpath "%~dp0build" ^
    "%~dp0tools\installer.py" || exit /b 1

echo.
echo [4/4] Done.
echo.
for %%F in (dist\SysMonitor.exe dist\SysMonitor-Setup.exe) do (
    if exist "%%F" call :size "%%F"
)
echo.
echo   dist\SysMonitor.exe        portable - just run it
echo   dist\SysMonitor-Setup.exe  installer - Start Menu, uninstaller, autostart
echo.

rem Building does NOT touch an already-installed copy.  Say so loudly:
rem running a stale install against fresh source is exactly the trap that
rem hid an already-fixed memory leak for days.
set "INSTALLED=%LOCALAPPDATA%\Programs\SysMonitor\SysMonitor.exe"
if exist "%INSTALLED%" call :checkinstalled

endlocal
exit /b 0

:appdone
echo.
echo   Application build complete. Installer was not built.
call :size "dist\SysMonitor.exe"
endlocal
exit /b 0

:checkinstalled
for %%A in ("%~dp0dist\SysMonitor.exe") do set "NEWSIZE=%%~zA"
for %%A in ("%INSTALLED%") do set "OLDSIZE=%%~zA"
if "%NEWSIZE%"=="%OLDSIZE%" (
    echo   Installed copy looks up to date.
) else (
    echo   ***************************************************************
    echo   *  An INSTALLED copy exists and is OUT OF DATE.               *
    echo   *  This build did NOT touch it.  To update it in place:       *
    echo   *                                                             *
    echo   *      dist\SysMonitor-Setup.exe /S                           *
    echo   *                                                             *
    echo   *  Keeps your settings, shortcuts and autostart.              *
    echo   ***************************************************************
)
echo.
exit /b 0

:size
for %%A in ("%~1") do echo   %~1  =  %%~zA bytes
exit /b 0
