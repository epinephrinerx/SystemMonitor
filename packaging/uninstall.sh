#!/bin/sh
# Removes what install.sh created. Settings in ~/.config/sysmonitor are kept.
set -eu
dest="${XDG_DATA_HOME:-$HOME/.local/share}/sysmonitor"
apps="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
rm -f "$apps/sysmonitor.desktop"
if [ -d "$dest" ] && [ -x "$dest/sysmonitor" ]; then rm -rf "$dest"; fi
echo "Removed."
