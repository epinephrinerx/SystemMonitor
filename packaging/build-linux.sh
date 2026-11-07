#!/bin/bash
# Builds dist-linux/sysmonitor-<version>-linux-x64.tar.gz
# Usage: packaging/build-linux.sh [version]   (default: 3.2.0)
# Needs the .NET 10 SDK and network access to restore Avalonia once.
set -euo pipefail
cd "$(dirname "$0")/.."
version="${1:-3.2.0}"
rid=linux-x64
name="sysmonitor-$version-$rid"
out="dist-linux"
stage="$out/$name"

echo "== tests =="
dotnet test SysMonitor.Linux.Tests --nologo -v q
dotnet test SysMonitor.Linux.App.Tests --nologo -v q

echo "== publish =="
rm -rf "$stage"
mkdir -p "$stage/app"
dotnet publish SysMonitor.Linux.App -c Release -r "$rid" --self-contained true \
  -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false \
  -o "$stage/app" --nologo -v q

cp packaging/sysmonitor.png "$stage/app/sysmonitor.png"
cp packaging/sysmonitor.desktop packaging/install.sh packaging/uninstall.sh "$stage/"
cp README.linux.md "$stage/README.md"
cp CHANGELOG.md "$stage/CHANGELOG.md"
chmod +x "$stage/install.sh" "$stage/uninstall.sh" "$stage/app/sysmonitor"

echo "== archive =="
tar -C "$out" -czf "$out/$name.tar.gz" "$name"
ls -l "$out/$name.tar.gz"
