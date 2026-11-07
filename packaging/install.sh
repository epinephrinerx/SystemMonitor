#!/bin/sh
# Per-user install of SysMonitor from the extracted tarball. No root needed.
# Usage: ./install.sh            installs into ~/.local/share/sysmonitor
set -eu
here="$(cd "$(dirname "$0")" && pwd)"
dest="${XDG_DATA_HOME:-$HOME/.local/share}/sysmonitor"
apps="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
mkdir -p "$dest" "$apps"
# Copy only what the tarball ships; never walk or delete anything else.
cp -R "$here/app/." "$dest/"
chmod +x "$dest/sysmonitor"
sed "s|@INSTALL_DIR@|$dest|g" "$here/sysmonitor.desktop" > "$apps/sysmonitor.desktop"
echo "Installed to $dest"
echo "Launcher: $apps/sysmonitor.desktop"
