#!/bin/sh
# Build the per-user Linux release archive (R-027 phase 4).
#
#   ./build-linux.sh [--arch x64|arm64] [--framework-dependent]
#
# Produces dist/SystemMonitor-<version>-linux-<arch>.tar.gz and its .sha256
# file. The archive is flat: install.sh, uninstall.sh, the menu template, the
# icon and VERSION sit at the root, with the published program in app/. The
# updater requires install.sh at the root and rejects any link in the archive,
# so both are checked here.
set -eu

program=$(basename -- "$0")
root=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
project=$root/SysMonitor.Linux.App/SysMonitor.Linux.App.csproj
packaging=$root/packaging/linux
assets=$root/Assets
dist=$root/dist
stage=$dist/stage

usage() {
    cat <<'USAGE'
Usage: ./build-linux.sh [--arch x64|arm64] [--framework-dependent]

  --arch ARCH            x64 (default) or arm64
  --framework-dependent  do not bundle the .NET runtime
  --help                 show this text

Output: dist/SystemMonitor-<version>-linux-<arch>.tar.gz
        dist/SystemMonitor-<version>-linux-<arch>.tar.gz.sha256
USAGE
}

say() { printf '%s\n' "$*"; }
fail() {
    printf '%s: %s\n' "$program" "$*" >&2
    exit 1
}

arch=x64
self_contained=true
while [ $# -gt 0 ]; do
    case $1 in
    --arch)
        [ $# -ge 2 ] || fail "--arch needs a value"
        arch=$2
        shift
        ;;
    --arch=*) arch=${1#--arch=} ;;
    --framework-dependent) self_contained=false ;;
    -h | --help)
        usage
        exit 0
        ;;
    *)
        fail "unknown argument: $1"
        ;;
    esac
    shift
done

case $arch in
x64 | arm64) ;;
*) fail "--arch must be x64 or arm64, not '$arch'" ;;
esac

[ -f "$project" ] || fail "cannot find $project"
[ -f "$packaging/install.sh" ] || fail "cannot find $packaging/install.sh"
[ -f "$packaging/uninstall.sh" ] || fail "cannot find $packaging/uninstall.sh"
[ -f "$packaging/systemmonitor.desktop" ] ||
    fail "cannot find $packaging/systemmonitor.desktop"
[ -f "$assets/systemmonitor.png" ] || fail "cannot find $assets/systemmonitor.png"

command -v dotnet >/dev/null 2>&1 ||
    fail "dotnet is not on PATH (try export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:\$PATH)"

version=$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$project" | sed -n '1p')
[ -n "$version" ] || fail "no <Version> in $project"

name=SystemMonitor-$version-linux-$arch
archive=$name.tar.gz

say "System Monitor $version, linux-$arch, self-contained=$self_contained"

# Start from an empty stage so nothing from an older build can be shipped.
rm -rf -- "$stage"
mkdir -p -- "$stage/app"

# No single-file bundle: the updater compares and installs a directory tree.
dotnet publish "$project" \
    -c Release \
    -r "linux-$arch" \
    --self-contained "$self_contained" \
    -p:DebugType=none \
    -p:PublishSingleFile=false \
    -o "$stage/app"
[ -f "$stage/app/sysmonitor" ] || fail "the publish did not produce app/sysmonitor"

cp -- "$packaging/install.sh" "$stage/install.sh"
cp -- "$packaging/uninstall.sh" "$stage/uninstall.sh"
cp -- "$packaging/systemmonitor.desktop" "$stage/systemmonitor.desktop"
mkdir -p -- "$stage/icons"
cp -- "$assets/systemmonitor.png" "$stage/icons/systemmonitor.png"
printf '%s\n' "$version" >"$stage/VERSION"
chmod 755 -- "$stage/install.sh" "$stage/uninstall.sh" "$stage/app/sysmonitor"

# The updater refuses an archive that holds any link, so the build does too.
links=$(find "$stage" -type l | wc -l)
if [ "$links" -ne 0 ]; then
    find "$stage" -type l >&2
    fail "the staged tree holds $links symbolic link(s); the updater rejects them"
fi

# Reproducible membership and order: a sorted name list, pax format, numeric
# owner 0:0 and a fixed sort. Directory entries come from the same list, and
# --no-recursion keeps tar from walking them a second time (which would add
# every file twice as a hard link to itself).
list=$dist/.filelist
(
    cd "$stage"
    find . -mindepth 1 \( -type f -o -type d \) | sed 's:^\./::' | LC_ALL=C sort
) >"$list"
tar --format=pax --owner=0 --group=0 --numeric-owner --sort=name --no-recursion \
    -C "$stage" -czf "$dist/$archive" -T "$list"
rm -f -- "$list"

# A link of any kind would make the updater reject the whole archive. The
# staged tree has none; a hard link can only appear here if two paths share an
# inode, so check the archive itself as well.
if tar -tvzf "$dist/$archive" | grep -q '^h'; then
    fail "the archive holds a hard link; the updater rejects them"
fi

(
    cd "$dist"
    sha256sum "$archive" >"$archive.sha256"
)

size=$(wc -c <"$dist/$archive" | tr -d ' ')
say ""
say "wrote dist/$archive ($size bytes)"
say "wrote dist/$archive.sha256"
cat "$dist/$archive.sha256"
