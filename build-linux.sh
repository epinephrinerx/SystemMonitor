#!/bin/sh
# Build the Linux release: the per-user archive (R-027 phase 4) and, with
# --deb, the .deb package (R-027 release).
#
#   ./build-linux.sh [--arch x64|arm64] [--framework-dependent] [--deb]
#
# Produces dist/SystemMonitor-<version>-linux-<arch>.tar.gz and its .sha256
# file. The archive is flat: install.sh, uninstall.sh, the menu template, the
# icon and VERSION sit at the root, with the published program in app/. The
# updater requires install.sh at the root and rejects any link in the archive,
# so both are checked here.
#
# --deb also packs the same publish output into
# dist/systemmonitor_<version>_<amd64|arm64>.deb, with a .sha256 file, using
# the templates in packaging/linux/deb/.
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
Usage: ./build-linux.sh [--arch x64|arm64] [--framework-dependent] [--deb]

  --arch ARCH            x64 (default) or arm64
  --framework-dependent  do not bundle the .NET runtime
  --deb                  also build the .deb package
  --help                 show this text

Output: dist/SystemMonitor-<version>-linux-<arch>.tar.gz
        dist/SystemMonitor-<version>-linux-<arch>.tar.gz.sha256
        dist/systemmonitor_<version>_<debarch>.deb          (--deb)
        dist/systemmonitor_<version>_<debarch>.deb.sha256   (--deb)
USAGE
}

say() { printf '%s\n' "$*"; }
fail() {
    printf '%s: %s\n' "$program" "$*" >&2
    exit 1
}

arch=x64
self_contained=true
deb=0
while [ $# -gt 0 ]; do
    case $1 in
    --arch)
        [ $# -ge 2 ] || fail "--arch needs a value"
        arch=$2
        shift
        ;;
    --arch=*) arch=${1#--arch=} ;;
    --framework-dependent) self_contained=false ;;
    --deb) deb=1 ;;
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

# Amendment 1, item 1: the .deb always carries the runtime, so the two flags
# together describe a package we would not know how to build.
if [ "$deb" -eq 1 ] && [ "$self_contained" = false ]; then
    printf '%s: --deb cannot be used with --framework-dependent; the .deb is always self-contained\n' \
        "$program" >&2
    exit 2
fi

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

# --- .deb package (R-027 release) -------------------------------------------
#
# Same publish output, laid out the way a distribution package expects:
# /opt/systemmonitor holds the program, /usr/bin/sysmonitor is a symlink to it,
# and only maintscripts touch the system (they refresh the menu and icon
# caches). No user files, no services. The updater starts xdg-open and lets the
# desktop's package installer ask for authentication; nothing here runs
# pkexec, sudo, apt or dpkg -i.

# Escape a value for the replacement side of a sed s||| expression.
escape_sed() { printf '%s' "$1" | sed -e 's/[\\&|]/\\&/g'; }

# What the package needs at run time. dpkg-shlibdeps reads the native
# libraries the publish output ships; the rest is what the .NET runtime and
# Avalonia open with dlopen and dpkg-shlibdeps therefore cannot see.
deb_depends() {
    _app=$1
    _work=$dist/.shlibdeps
    rm -rf -- "$_work"
    mkdir -p -- "$_work/debian"
    {
        printf 'Source: systemmonitor\n'
        printf 'Section: utils\n'
        printf 'Priority: optional\n'
        printf 'Maintainer: %s\n' "$maintainer"
        printf 'Standards-Version: 4.6.2\n'
        printf '\n'
        printf 'Package: systemmonitor\n'
        printf 'Architecture: %s\n' "$debarch"
        printf 'Description: dependency probe\n'
        printf ' A throwaway control file for dpkg-shlibdeps.\n'
    } >"$_work/debian/control"

    # libcoreclrtraceptprovider.so is left out: it links lttng, an optional
    # tracing library that is often absent, and nothing needs that link for
    # the program to run. A batch-splitting xargs still yields one
    # shlibs:Depends= line per call and the merge below keeps them all.
    _shlibs=$(
        cd "$_work" &&
            find "$_app" -maxdepth 1 -type f -name '*.so' \
                ! -name 'libcoreclrtraceptprovider.so' -print0 |
            xargs -0 -r dpkg-shlibdeps -O 2>/dev/null |
            sed -n 's/^shlibs:Depends=//p'
    )
    rm -rf -- "$_work"
    [ -n "$_shlibs" ] || fail "dpkg-shlibdeps could not work out the dependencies"

    # libc6, libgcc-s1, libstdc++6 and libfontconfig1 normally come back from
    # dpkg-shlibdeps with a version; they are repeated here so a run that sees
    # fewer symbols still names them, and the merge drops the duplicate.
    # ICU, OpenSSL and the X11/SM libraries are dlopened, so they are named
    # here; the alternatives cover the soname packages of current releases
    # (several were renamed by the t64 transition).
    _extra='libc6, libgcc-s1, libstdc++6, zlib1g, libfontconfig1, libssl3t64 | libssl3, libicu78 | libicu76 | libicu74 | libicu72 | libicu70, libx11-6, libice6, libsm6'

    {
        printf '%s\n' "$_shlibs"
        printf '%s\n' "$_extra"
    } |
        tr ',' '\n' |
        sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//' |
        awk 'NF && !seen[tolower($1)]++' |
        paste -sd, - |
        sed -e 's/,/, /g'
}

build_deb() {
    command -v dpkg-deb >/dev/null 2>&1 ||
        fail "dpkg-deb is missing; --deb needs dpkg (dpkg-dev)"
    command -v dpkg-shlibdeps >/dev/null 2>&1 ||
        fail "dpkg-shlibdeps is missing; --deb needs dpkg-dev"

    for template in control.in copyright postinst postrm systemmonitor.desktop; do
        [ -f "$packaging/deb/$template" ] ||
            fail "cannot find packaging/linux/deb/$template"
    done

    case $arch in
    x64) debarch=amd64 ;;
    arm64) debarch=arm64 ;;
    *) fail "no deb architecture for $arch" ;;
    esac

    holder=$(git -C "$root" config user.name 2>/dev/null || true)
    [ -n "$holder" ] ||
        fail "git config user.name is empty; the .deb needs a Maintainer"
    maintainer="$holder <epinephrinerx@users.noreply.github.com>"

    debroot=$dist/deb/systemmonitor
    debname=systemmonitor_${version}_${debarch}.deb
    rm -rf -- "$dist/deb"
    mkdir -p -- "$debroot/opt" "$debroot/usr/bin" \
        "$debroot/usr/share/applications" \
        "$debroot/usr/share/icons/hicolor/256x256/apps" \
        "$debroot/usr/share/doc/systemmonitor" "$debroot/DEBIAN"

    cp -a -- "$stage/app" "$debroot/opt/systemmonitor"
    ln -s -- /opt/systemmonitor/sysmonitor "$debroot/usr/bin/sysmonitor"
    cp -- "$packaging/deb/systemmonitor.desktop" \
        "$debroot/usr/share/applications/systemmonitor.desktop"
    cp -- "$assets/systemmonitor.png" \
        "$debroot/usr/share/icons/hicolor/256x256/apps/systemmonitor.png"

    installed_size=$(du -sk --exclude=DEBIAN "$debroot" | cut -f1)
    depends=$(deb_depends "$debroot/opt/systemmonitor")

    sed -e "s|@VERSION@|$(escape_sed "$version")|g" \
        -e "s|@ARCH@|$(escape_sed "$debarch")|g" \
        -e "s|@MAINTAINER@|$(escape_sed "$maintainer")|g" \
        -e "s|@INSTALLED_SIZE@|$(escape_sed "$installed_size")|g" \
        -e "s|@DEPENDS@|$(escape_sed "$depends")|g" \
        "$packaging/deb/control.in" >"$debroot/DEBIAN/control"
    sed -e "s|@HOLDER@|$(escape_sed "$holder")|g" \
        "$packaging/deb/copyright" >"$debroot/usr/share/doc/systemmonitor/copyright"
    cp -- "$packaging/deb/postinst" "$debroot/DEBIAN/postinst"
    cp -- "$packaging/deb/postrm" "$debroot/DEBIAN/postrm"

    # 755 on directories and on the program, 644 on every other file, and 755
    # on the two maintainer scripts (dpkg requires them executable).
    find "$debroot" -type d -exec chmod 755 {} +
    find "$debroot" -type f -exec chmod 644 {} +
    chmod 755 -- "$debroot/opt/systemmonitor/sysmonitor"
    chmod 755 -- "$debroot/DEBIAN/postinst" "$debroot/DEBIAN/postrm"

    dpkg-deb --root-owner-group --build "$debroot" "$dist/$debname"
    (
        cd "$dist"
        sha256sum "$debname" >"$debname.sha256"
    )

    debsize=$(wc -c <"$dist/$debname" | tr -d ' ')
    say ""
    say "wrote dist/$debname ($debsize bytes, Installed-Size $installed_size KiB)"
    say "depends: $depends"
    say "wrote dist/$debname.sha256"
    cat "$dist/$debname.sha256"
}

if [ "$deb" -eq 1 ]; then
    build_deb
fi
