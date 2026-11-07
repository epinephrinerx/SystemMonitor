# SysMonitor for Linux

A small always-on-top widget showing CPU, memory, disks and network on a Linux
desktop. It is the Linux build of SysMonitor (C# / .NET 10 / Avalonia); the
Windows build (`SysMonitor.Wpf`) is unchanged.

## Views

* **Mini** - one metric at a time, rotating every 5 seconds. The left and right
  buttons move to the previous / next metric at once. Double-click to change
  view.
* **Expanded** - every metric as a card in two columns, with a sidebar:
  **Full Data** button on top, then the window settings (theme, always on top,
  opacity, update speed).
* **Full** - a tab per device: CPU, Memory, Disk, then one tab per logical disk
  (mount point) with filesystem, physical disk, partition, model and live
  graphs. It is an ordinary window, not an OS full-screen mode. Below
  640 x 480 it falls back to Expanded.

Light theme with a white background is the default on every view, whatever the
desktop theme is; dark can be switched on in the sidebar.

## Install (from the tarball)

    tar xzf sysmonitor-<version>-linux-x64.tar.gz
    cd sysmonitor-<version>-linux-x64
    ./install.sh          # per-user, no root; ./uninstall.sh removes it

The archive is self-contained (it carries the .NET runtime), so nothing else
needs installing. You can also run `app/sysmonitor` directly. Start in a given
view with `--mode mini|expanded|full`.

Requirements: x86-64 Linux with an X11 session (Wayland sessions work through
XWayland), Ubuntu 22.04 / Debian 12 or newer. Settings are kept in
`$XDG_CONFIG_HOME/sysmonitor/config.linux.json` (default
`~/.config/sysmonitor/`).

## What is read, and what is not

Everything comes from `/proc` and `/sys` without root: CPU from `/proc/stat`,
memory from `/proc/meminfo`, disks from `/proc/self/mountinfo`,
`/proc/diskstats` and `/sys/block`, network from `/proc/net/dev` and
`/sys/class/net`, temperatures from `/sys/class/hwmon`. Values the kernel does
not expose to an ordinary user (for example memory module slots, which need
`dmidecode`) are shown as `N/A`, never guessed.

## Build from source

    # .NET 10 SDK (Ubuntu 26.04: apt install dotnet-sdk-10.0)
    dotnet test SysMonitor.Linux.Tests
    dotnet test SysMonitor.Linux.App.Tests     # headless render, no display needed
    dotnet run --project SysMonitor.Linux.App -- --mode expanded
    packaging/build-linux.sh 3.2.0             # -> dist-linux/*.tar.gz

The Linux projects and the shared `SysMonitor.Core` library keep the Windows
build's tests unchanged: `dotnet test SysMonitor.Tests` still has to be run on
Windows.
