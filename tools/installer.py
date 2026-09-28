"""SysMonitor setup program.

Built by build.cmd into a single SysMonitor-Setup.exe with the application
executable embedded. Installs per-user into %LOCALAPPDATA%\\Programs\\SysMonitor,
so it never needs administrator rights or a UAC prompt.

    SysMonitor-Setup.exe              interactive wizard
    SysMonitor-Setup.exe /S           silent install with defaults
    SysMonitor-Setup.exe --uninstall  remove (this is what uninstall.exe runs)
"""

import os
import base64
import shutil
import subprocess
import sys
import tkinter as tk
import winreg

APP_NAME = "SysMonitor"
APP_VERSION = "2.0.1"
PUBLISHER = "SysMonitor"
EXE_NAME = "SysMonitor.exe"
UNINST_NAME = "uninstall.exe"
RUN_KEY = r"Software\Microsoft\Windows\CurrentVersion\Run"
UNINSTALL_KEY = r"Software\Microsoft\Windows\CurrentVersion\Uninstall\SysMonitor"

BG = "#0f172a"
SIDEBAR = "#0b1220"
CARD = "#1a2438"
TEXT = "#ffffff"
MUTED = "#cbd5e1"
LABEL = "#94a3b8"
ACCENT = "#3b82f6"
DANGER = "#ef4444"
OK = "#22c55e"

CREATE_NO_WINDOW = 0x08000000
PROGRAM_FILES = (EXE_NAME, APP_NAME + ".ico", UNINST_NAME)


def bundled(name):
    """Path to a file packed into the onefile bundle (or the source tree)."""
    base = getattr(sys, "_MEIPASS", None)
    if base is None:
        base = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        for candidate in (os.path.join(base, "dist", name), os.path.join(base, name)):
            if os.path.exists(candidate):
                return candidate
    return os.path.join(base, name)


def default_dir():
    root = os.environ.get("LOCALAPPDATA") or os.path.expanduser("~")
    return os.path.join(root, "Programs", APP_NAME)


def start_menu_dir():
    appdata = os.environ.get("APPDATA") or os.path.expanduser("~")
    return os.path.join(appdata, "Microsoft", "Windows", "Start Menu", "Programs")


def desktop_dir():
    return os.path.join(os.path.expanduser("~"), "Desktop")


def run_quiet(cmd, timeout=None, shell=False):
    """Run a child process with every standard handle explicitly detached.

    A --noconsole PyInstaller build has no valid stdin/stdout/stderr handles.
    Letting a child inherit them makes subprocess raise
    'OSError [WinError 6] The handle is invalid' -- which is what made silent
    install die on the very first taskkill while working fine from a console.
    """
    return subprocess.run(cmd, stdin=subprocess.DEVNULL,
                          stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                          timeout=timeout, shell=shell,
                          creationflags=CREATE_NO_WINDOW)


def powershell(script):
    try:
        result = run_quiet(["powershell", "-NoProfile", "-NonInteractive",
                   "-ExecutionPolicy", "Bypass", "-Command", script],
                  timeout=30)
        return result.returncode == 0
    except Exception:
        return False


def make_shortcut(link_path, target, icon=None, description=""):
    """Create a .lnk via WScript.Shell -- no pywin32 needed."""
    os.makedirs(os.path.dirname(link_path), exist_ok=True)
    script = (
        "$s = (New-Object -ComObject WScript.Shell).CreateShortcut('%s');"
        "$s.TargetPath = '%s';"
        "$s.WorkingDirectory = '%s';"
        "$s.Description = '%s';"
        % tuple(value.replace("'", "''") for value in
                (link_path, target, os.path.dirname(target), description))
    )
    if icon:
        script += "$s.IconLocation = '%s';" % icon.replace("'", "''")
    script += "$s.Save()"
    return powershell(script)


def stop_running():
    """Close a running copy so its files can be replaced."""
    try:
        run_quiet(["taskkill", "/F", "/IM", EXE_NAME], timeout=30)
    except Exception:
        pass          # nothing running, or taskkill unavailable


def existing_install():
    """What is already set up, so an upgrade does not undo the user's choices.

    Returns (install_dir or None, desktop_shortcut, run_at_startup).
    """
    target = None
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, UNINSTALL_KEY) as key:
            target = winreg.QueryValueEx(key, "InstallLocation")[0]
    except OSError:
        target = None
    if target and not os.path.isdir(target):
        target = None
    desktop = os.path.exists(os.path.join(desktop_dir(), APP_NAME + ".lnk"))
    startup = False
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, RUN_KEY) as key:
            winreg.QueryValueEx(key, APP_NAME)
            startup = True
    except OSError:
        startup = False
    return target, desktop, startup


def install(target_dir, desktop=True, startup=False, launch=True, log=print):
    target_dir, _ = owned_paths(target_dir, PROGRAM_FILES)
    source = bundled(EXE_NAME)
    setup_source = (sys.executable if getattr(sys, "frozen", False)
                    else bundled("SysMonitor-Setup.exe"))
    if not os.path.isfile(source) or not os.path.isfile(setup_source):
        raise IOError("Build both the application and setup before installing")
    stop_running()
    log("Creating %s" % target_dir)
    os.makedirs(target_dir, exist_ok=True)

    exe_path = os.path.join(target_dir, EXE_NAME)
    log("Copying %s" % EXE_NAME)
    shutil.copy2(source, exe_path)

    icon_src = bundled(APP_NAME + ".ico")
    icon_path = os.path.join(target_dir, APP_NAME + ".ico")
    if os.path.exists(icon_src):
        shutil.copy2(icon_src, icon_path)
    else:
        icon_path = exe_path

    # The setup program doubles as the uninstaller.
    uninst_path = os.path.join(target_dir, UNINST_NAME)
    log("Writing uninstaller")
    shutil.copy2(setup_source, uninst_path)

    log("Adding Start Menu shortcut")
    make_shortcut(os.path.join(start_menu_dir(), APP_NAME + ".lnk"),
                  exe_path, icon_path, "System resource monitor widget")
    if desktop:
        log("Adding Desktop shortcut")
        make_shortcut(os.path.join(desktop_dir(), APP_NAME + ".lnk"),
                      exe_path, icon_path, "System resource monitor widget")

    with winreg.CreateKey(winreg.HKEY_CURRENT_USER, RUN_KEY) as key:
        if startup:
            log("Enabling start with Windows")
            winreg.SetValueEx(key, APP_NAME, 0, winreg.REG_SZ, '"%s"' % exe_path)
        else:
            try:
                winreg.DeleteValue(key, APP_NAME)
            except OSError:
                pass

    log("Registering in Apps & features")
    size_kb = sum(os.path.getsize(os.path.join(target_dir, name))
                  for name in PROGRAM_FILES
                  if os.path.isfile(os.path.join(target_dir, name))) // 1024
    with winreg.CreateKey(winreg.HKEY_CURRENT_USER, UNINSTALL_KEY) as key:
        values = {
            "DisplayName": APP_NAME,
            "DisplayVersion": APP_VERSION,
            "Publisher": PUBLISHER,
            "DisplayIcon": icon_path,
            "InstallLocation": target_dir,
            "NoModify": 1,
            "NoRepair": 1,
            "EstimatedSize": size_kb,
        }
        if uninst_path:
            values["UninstallString"] = '"%s" --uninstall' % uninst_path
            values["QuietUninstallString"] = '"%s" --uninstall /S' % uninst_path
        for name, value in values.items():
            kind = winreg.REG_DWORD if isinstance(value, int) else winreg.REG_SZ
            winreg.SetValueEx(key, name, 0, kind, value)

    if launch:
        log("Starting %s" % APP_NAME)
        try:
            subprocess.Popen([exe_path], cwd=target_dir,
                             stdin=subprocess.DEVNULL,
                             stdout=subprocess.DEVNULL,
                             stderr=subprocess.DEVNULL)
        except OSError:
            pass
    log("Done.")
    return exe_path


def owned_paths(directory, names):
    """Validate an absolute directory and its fixed, directly owned filenames.

    Resolve junctions/symlinks before checking containment. Never enumerate or
    recursively remove a user-selected installation directory.
    """
    if not directory or not os.path.isabs(directory):
        raise ValueError("Expected an absolute installation/settings directory")
    directory = os.path.realpath(directory)
    if os.path.dirname(directory) == directory:
        raise ValueError("A drive/share root cannot be an installation directory")
    paths = []
    for name in names:
        if os.path.basename(name) != name:
            raise ValueError("Expected a direct filename")
        path = os.path.join(directory, name)
        resolved = os.path.realpath(path)
        if os.path.normcase(os.path.dirname(resolved)) != os.path.normcase(directory):
            raise ValueError("Owned file resolves outside the target directory: " + path)
        paths.append(path)
    return directory, paths


def remove_empty_dir(directory):
    try:
        os.rmdir(directory)  # non-recursive: preserve all unrecognized files
    except OSError:
        pass


def schedule_self_removal(target_dir, running):
    """Only the installed, frozen uninstaller may schedule its own deletion."""
    target_dir, paths = owned_paths(target_dir, (UNINST_NAME,))
    running = os.path.realpath(running)
    if (not getattr(sys, "frozen", False)
            or os.path.normcase(running) != os.path.normcase(paths[0])):
        return False
    literal_file = running.replace("'", "''")
    literal_dir = target_dir.replace("'", "''")
    # Wait for this wizard to exit; a frozen bootloader may retain the file
    # briefly afterward. EncodedCommand preserves literal paths, including %,
    # apostrophes and shell metacharacters, without cmd.exe expansion.
    script = (
        "$p = Get-Process -Id %d -ErrorAction SilentlyContinue;"
        "if ($p) { $p.WaitForExit() };"
        "for ($attempt=0; $attempt -lt 10; $attempt++) {"
        "try { Remove-Item -LiteralPath '%s' -Force -ErrorAction Stop; break }"
        "catch { Start-Sleep -Milliseconds 500 } };"
        "try { [System.IO.Directory]::Delete('%s') } catch {}"
    ) % (os.getpid(), literal_file, literal_dir)
    encoded = base64.b64encode(script.encode("utf-16-le")).decode("ascii")
    subprocess.Popen(
        ["powershell", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
        creationflags=CREATE_NO_WINDOW, stdin=subprocess.DEVNULL,
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    return True


def uninstall(keep_settings=False, log=print):
    target_dir = ""
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, UNINSTALL_KEY) as key:
            target_dir = winreg.QueryValueEx(key, "InstallLocation")[0]
    except OSError:
        target_dir = default_dir()

    target_dir, program_paths = owned_paths(target_dir, PROGRAM_FILES)
    settings_root = os.environ.get("APPDATA") or os.path.expanduser("~")
    settings, settings_paths = owned_paths(
        os.path.join(settings_root, APP_NAME),
        ("config.json", "config.json.tmp", "sysmonitor.log"))
    stop_running()

    for path in (os.path.join(start_menu_dir(), APP_NAME + ".lnk"),
                 os.path.join(desktop_dir(), APP_NAME + ".lnk")):
        if os.path.exists(path):
            log("Removing shortcut")
            try:
                os.remove(path)
            except OSError:
                pass

    if not keep_settings:
        log("Removing settings")
        for path in settings_paths:
            if os.path.isfile(path):
                os.remove(path)
        remove_empty_dir(settings)

    if os.path.isdir(target_dir):
        log("Removing program files")
        running = os.path.realpath(sys.executable)
        for path in program_paths:
            if os.path.normcase(os.path.realpath(path)) == os.path.normcase(running):
                continue
            if os.path.isfile(path):
                os.remove(path)
        if not schedule_self_removal(target_dir, running):
            remove_empty_dir(target_dir)

    # Keep InstallLocation available for a retry if a file removal fails.
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, RUN_KEY, 0,
                            winreg.KEY_ALL_ACCESS) as key:
            winreg.DeleteValue(key, APP_NAME)
            log("Removed startup entry")
    except OSError:
        pass
    try:
        winreg.DeleteKey(winreg.HKEY_CURRENT_USER, UNINSTALL_KEY)
        log("Deregistered from Apps & features")
    except OSError:
        pass
    log("Done.")


# --------------------------------------------------------------------- UI
class Wizard:
    def __init__(self, mode="install"):
        self.mode = mode
        self.root = tk.Tk()
        self.root.title("%s Setup" % APP_NAME)
        self.root.configure(bg=BG)
        self.root.resizable(False, False)
        try:
            icon = bundled(APP_NAME + ".ico")
            if os.path.exists(icon):
                self.root.iconbitmap(icon)
        except tk.TclError:
            pass

        width, height = 560, 470
        x = (self.root.winfo_screenwidth() - width) // 2
        y = (self.root.winfo_screenheight() - height) // 3
        self.root.geometry("%dx%d+%d+%d" % (width, height, x, y))

        where, has_desktop, has_startup = existing_install()
        self.upgrade = where is not None
        self.dir_var = tk.StringVar(value=where or default_dir())
        # On an upgrade, start from the user's current choices instead of the
        # defaults -- silently dropping their autostart would be a nasty
        # surprise for someone just installing a newer build.
        self.desktop_var = tk.BooleanVar(value=has_desktop if self.upgrade else True)
        self.startup_var = tk.BooleanVar(value=has_startup)
        self.launch_var = tk.BooleanVar(value=True)
        self.keep_var = tk.BooleanVar(value=False)
        self.busy = False

        self._build()

    def _label(self, parent, text, size=10, color=TEXT, bold=False, **kw):
        return tk.Label(parent, text=text, bg=kw.pop("bg", BG), fg=color,
                        font=("Segoe UI", size, "bold" if bold else "normal"),
                        **kw)

    def _check(self, parent, text, var):
        return tk.Checkbutton(
            parent, text=text, variable=var, bg=BG, fg=MUTED,
            selectcolor=CARD, activebackground=BG, activeforeground=TEXT,
            font=("Segoe UI", 9), bd=0, highlightthickness=0, anchor="w")

    def _build(self):
        header = tk.Frame(self.root, bg=SIDEBAR, height=76)
        header.pack(fill="x")
        header.pack_propagate(False)
        self._label(header, APP_NAME, 16, TEXT, True, bg=SIDEBAR).pack(
            anchor="w", padx=24, pady=(16, 0))
        if self.mode == "uninstall":
            subtitle = "Remove this program from your computer"
        elif getattr(self, "upgrade", False):
            subtitle = ("Update the installed copy  •  version %s" % APP_VERSION)
        else:
            subtitle = "System resource monitor  •  version %s" % APP_VERSION
        self._label(header, subtitle, 9, LABEL, bg=SIDEBAR).pack(anchor="w", padx=24)

        # Reserve the footer before the body claims the leftover space,
        # otherwise an expanding body pushes the buttons off the window.
        footer = tk.Frame(self.root, bg=BG)
        footer.pack(side="bottom", fill="x", padx=24, pady=(0, 18))

        body = tk.Frame(self.root, bg=BG)
        body.pack(side="top", fill="both", expand=True, padx=24, pady=18)
        self.body = body

        if self.mode == "uninstall":
            self._build_uninstall(body)
        else:
            self._build_install(body)
        self.status = self._label(footer, "", 8, LABEL)
        self.status.pack(side="left")
        tk.Button(footer, text="Cancel", command=self.root.destroy, bg=CARD,
                  fg=MUTED, font=("Segoe UI", 9), bd=0, padx=18, pady=6,
                  activebackground=CARD, activeforeground=TEXT).pack(
            side="right", padx=(8, 0))
        self.action = tk.Button(
            footer, text=("Uninstall" if self.mode == "uninstall"
                          else ("Update" if getattr(self, "upgrade", False)
                                else "Install")),
            command=self.run, bg=DANGER if self.mode == "uninstall" else ACCENT,
            fg="#ffffff", font=("Segoe UI", 9, "bold"), bd=0, padx=24, pady=6,
            activebackground=ACCENT, activeforeground="#ffffff")
        self.action.pack(side="right")

    def _build_install(self, body):
        self._label(body, "Install location", 9, LABEL, True).pack(anchor="w")
        row = tk.Frame(body, bg=BG)
        row.pack(fill="x", pady=(6, 16))
        entry = tk.Entry(row, textvariable=self.dir_var, bg=CARD, fg=TEXT,
                         insertbackground=TEXT, relief="flat",
                         font=("Segoe UI", 9))
        entry.pack(side="left", fill="x", expand=True, ipady=6, padx=(0, 8))
        tk.Button(row, text="Browse...", command=self.browse, bg=CARD, fg=MUTED,
                  font=("Segoe UI", 9), bd=0, padx=14, pady=5,
                  activebackground=CARD, activeforeground=TEXT).pack(side="right")
        self._label(body, "Installs for the current user only — no administrator "
                          "rights required.", 8, LABEL, wraplength=500,
                    justify="left").pack(anchor="w", pady=(0, 16))

        self._label(body, "Options", 9, LABEL, True).pack(anchor="w")
        for text, var in (("Create a Desktop shortcut", self.desktop_var),
                          ("Start SysMonitor when Windows starts", self.startup_var),
                          ("Run SysMonitor after installing", self.launch_var)):
            self._check(body, text, var).pack(anchor="w", pady=2)

        self.log_box = self._make_log(body)

    def _build_uninstall(self, body):
        self._label(body, "SysMonitor will be removed from this computer.",
                    10).pack(anchor="w", pady=(0, 14))
        self._check(body, "Keep my settings (window size, theme, position)",
                    self.keep_var).pack(anchor="w")
        self.log_box = self._make_log(body)

    def _make_log(self, body):
        box = tk.Text(body, height=6, bg=SIDEBAR, fg=MUTED, relief="flat",
                      font=("Consolas", 8), state="disabled", wrap="none",
                      highlightthickness=0)
        box.pack(fill="both", expand=True, pady=(16, 0))
        return box

    def browse(self):
        from tkinter import filedialog
        chosen = filedialog.askdirectory(initialdir=os.path.dirname(self.dir_var.get()))
        if chosen:
            self.dir_var.set(os.path.join(os.path.normpath(chosen), APP_NAME))

    def log(self, message):
        self.log_box.configure(state="normal")
        self.log_box.insert("end", message + "\n")
        self.log_box.see("end")
        self.log_box.configure(state="disabled")
        self.root.update_idletasks()

    def run(self):
        if self.busy:
            return
        self.busy = True
        self.action.configure(state="disabled", bg=CARD)
        try:
            if self.mode == "uninstall":
                uninstall(self.keep_var.get(), log=self.log)
                self.status.configure(text="Uninstalled.", fg=OK)
            else:
                install(os.path.normpath(self.dir_var.get()),
                        self.desktop_var.get(), self.startup_var.get(),
                        self.launch_var.get(), log=self.log)
                self.status.configure(text="Installed successfully.", fg=OK)
            self.action.configure(text="Close", state="normal", bg=ACCENT,
                                  command=self.root.destroy)
        except Exception as exc:
            self.log("ERROR: %s" % exc)
            self.status.configure(text="Failed.", fg=DANGER)
            self.action.configure(state="normal", bg=ACCENT)
        finally:
            self.busy = False

    def go(self):
        self.root.mainloop()


def main(argv):
    silent = "/S" in argv or "--silent" in argv
    removing = "--uninstall" in argv or "/uninstall" in argv

    if silent:
        if removing:
            uninstall()
        else:
            where, has_desktop, has_startup = existing_install()
            install(where or default_dir(),
                    desktop=has_desktop if where else True,
                    startup=has_startup, launch=False)
        return 0

    Wizard("uninstall" if removing else "install").go()
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
