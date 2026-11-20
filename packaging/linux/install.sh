#!/bin/sh
# System Monitor for Linux -- per-user installer (R-027 phase 4).
#
# Unpack a release archive and run ./install.sh. Everything lands in the
# user's own XDG directories; nothing here needs root and nothing is written
# outside the paths printed at the end.
#
# The updater in the running app unpacked this archive and calls
#     sh install.sh --update --wait-pid <pid>
# so the old program can exit before its files are replaced (phase 3
# handoff). --update still installs and then starts the new copy; the
# app's own autostart entry and settings are never touched, because the
# install path is the same on every update.
#
# The installer also leaves a copy of uninstall.sh in
# $XDG_DATA_HOME/SystemMonitor/, outside app/, so the program can be removed
# without the unpacked archive (handoff Amendment 1). Both scripts accept the
# same boundary: every path they touch is physically under $HOME.
#
# The program directory is replaced by two renames, not by a single atomic
# step. If a run is interrupted between them, app/ is missing and app.old/
# holds the previous version; the next run of this script (or the next
# update) puts it back before doing anything else (handoff Amendment 2).
#
# Everything else is staged in a mktemp file beside its destination and renamed
# into place only after the destination passed the boundary check, so a link
# planted at a fixed staging name cannot be written through (Amendment 3).
set -eu

program=$(basename -- "$0")

usage() {
    cat <<'USAGE'
Usage: ./install.sh [--update [--wait-pid PID]]

  --update          install, then start the new program in the background
  --wait-pid PID    wait up to 30 s for PID to exit before installing
  --help            show this text

Without --update the program is installed but not started.
Set SYSMONITOR_NO_LAUNCH=1 to skip the launch even with --update.
USAGE
}

say() { printf '%s\n' "$*"; }
warn() { printf '%s: %s\n' "$program" "$*" >&2; }
fail() {
    printf '%s: %s\n' "$program" "$*" >&2
    exit 1
}

# A staged file is written to a temporary created by mktemp in its destination
# directory, then renamed onto the final name. $staging names the temporary
# that exists right now, if any, so the trap below can remove it on every
# failure path (handoff Amendment 3 item 1).
staging=
cleanup_staging() {
    if [ -n "$staging" ]; then
        rm -f -- "$staging"
        staging=
    fi
}
trap 'cleanup_staging' EXIT
trap 'cleanup_staging; exit 1' HUP INT TERM

update=0
wait_pid=
wait_pid_given=0
while [ $# -gt 0 ]; do
    case $1 in
    --update)
        update=1
        ;;
    --wait-pid)
        [ $# -ge 2 ] || fail "--wait-pid needs a process id"
        wait_pid=$2
        wait_pid_given=1
        shift
        ;;
    --wait-pid=*)
        wait_pid=${1#--wait-pid=}
        wait_pid_given=1
        ;;
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

if [ "$wait_pid_given" -eq 1 ]; then
    case $wait_pid in
    '' | *[!0-9]*) fail "--wait-pid needs a number, not '$wait_pid'" ;;
    esac
fi

if [ "$(id -u)" -eq 0 ]; then
    fail "run this as the user who will use System Monitor, not as root"
fi

command -v mktemp >/dev/null 2>&1 ||
    fail "mktemp is required to stage files (coreutils)"

[ -n "${HOME:-}" ] || fail "HOME is not set"
home=$HOME
# One trailing slash would break every "$home"/* comparison below.
if [ "$home" != "/" ]; then
    home=${home%/}
fi
data_home=${XDG_DATA_HOME:-$home/.local/share}
config_home=${XDG_CONFIG_HOME:-$home/.config}

# --- physical path boundary (handoff Amendment 2 item 1) --------------------
#
# A lexical "is it under $HOME" test is not enough. An XDG directory can be a
# symlink that leads somewhere else, and a ".." component can climb out of the
# home directory while the path still starts with it. Every path is therefore
# checked twice: once when the script builds it, and again immediately before
# the rename or removal that uses it. uninstall.sh duplicates these helpers on
# purpose: each script has to stand alone.

# Refuse a "." or ".." component and any newline. A name may begin with a dot
# (.local); only a component that is exactly "." or ".." is rejected.
reject_dot_components() {
    _rd_desc=$1
    _rd_rest=$2
    case $_rd_rest in
    *'
'*) fail "$_rd_desc contains a newline" ;;
    esac
    while [ -n "$_rd_rest" ]; do
        case $_rd_rest in
        */*)
            _rd_part=${_rd_rest%%/*}
            _rd_rest=${_rd_rest#*/}
            ;;
        *)
            _rd_part=$_rd_rest
            _rd_rest=
            ;;
        esac
        case $_rd_part in
        . | ..) fail "$_rd_desc has a '$_rd_part' component: $2" ;;
        esac
    done
}

# Print the physical path of $1: resolve the nearest existing ancestor with
# cd -P, then append the components that do not exist yet. A path whose
# ancestor cannot be entered (a loop, a dangling link) fails.
physical_path() {
    _pp_path=$1
    _pp_rest=
    while [ ! -d "$_pp_path" ]; do
        case $_pp_path in
        */*)
            _pp_part=${_pp_path##*/}
            _pp_path=${_pp_path%/*}
            [ -n "$_pp_path" ] || _pp_path=/
            ;;
        *)
            _pp_part=$_pp_path
            _pp_path=.
            ;;
        esac
        _pp_rest=$_pp_part${_pp_rest:+/$_pp_rest}
    done
    _pp_base=$(CDPATH= cd -P -- "$_pp_path" && pwd -P) || return 1
    case $_pp_base in
    /) _pp_base= ;;
    esac
    if [ -n "$_pp_rest" ]; then
        printf '%s/%s\n' "$_pp_base" "$_pp_rest"
    else
        printf '%s\n' "${_pp_base:-/}"
    fi
}

# Require an absolute path with no "." or ".." component whose physical form
# sits under $HOME.
require_under_home() {
    _ru_desc=$1
    _ru_path=$2
    [ -n "$_ru_path" ] || fail "internal error: empty $_ru_desc"
    case $_ru_path in
    /*) ;;
    *) fail "$_ru_desc must be an absolute path: $_ru_path" ;;
    esac
    reject_dot_components "$_ru_desc" "$_ru_path"
    _ru_real=$(physical_path "$_ru_path") ||
        fail "could not resolve $_ru_desc: $_ru_path"
    case $_ru_real in
    "$home_root"/*) ;;
    *)
        fail "$_ru_desc resolves to $_ru_real, which is not under \$HOME ($home_real): $_ru_path"
        ;;
    esac
}

# Re-check right before a rename or a removal: the parent is resolved again
# (a directory may have become a symlink since the script started), and a
# target that is itself a symlink is refused. The launcher symlink is never
# passed here; it is deleted with rm -f.
guard_parent() {
    _gp_desc=$1
    _gp_target=$2
    [ -n "$_gp_target" ] || fail "internal error: empty $_gp_desc"
    case $_gp_target in
    /*) ;;
    *) fail "internal error: relative $_gp_desc: $_gp_target" ;;
    esac
    case $_gp_target in
    */*) _gp_parent=${_gp_target%/*} ;;
    *) _gp_parent=/ ;;
    esac
    [ -n "$_gp_parent" ] || _gp_parent=/
    require_under_home "the parent of $_gp_desc" "$_gp_parent"
    if [ -L "$_gp_target" ]; then
        fail "refusing to replace $_gp_target: it is a symbolic link"
    fi
}

# Create a directory only after the boundary check, then check the result
# again: mkdir -p follows an existing symlink silently.
ensure_dir() {
    _ed_desc=$1
    _ed_dir=$2
    require_under_home "$_ed_desc" "$_ed_dir"
    mkdir -p -- "$_ed_dir" || fail "could not create $_ed_dir"
    require_under_home "$_ed_desc" "$_ed_dir"
}

# Refuse anything other than a real directory at app.new and app.old: a
# symlink or a plain file there is not something an install creates.
refuse_scratch() {
    _rs_path=$1
    if [ -L "$_rs_path" ]; then
        fail "refusing to use $_rs_path: it is a symbolic link"
    fi
    if [ -e "$_rs_path" ] && [ ! -d "$_rs_path" ]; then
        fail "refusing to use $_rs_path: it is not a directory"
    fi
}

# Create the temporary that will become $2 in directory $1. mktemp creates the
# file with O_EXCL under a random name, so a link planted at a predictable
# staging name can never be written through (handoff Amendment 3 item 1). The
# template is absolute, so mktemp variants that reject "--" are fine too.
new_staging() {
    _ns_dir=$1
    _ns_name=$2
    staging=$(mktemp "$_ns_dir/.$_ns_name.XXXXXX") ||
        fail "could not create a temporary file in $_ns_dir"
}

# A staged file is renamed onto its final name, so that name may be absent or a
# regular file and nothing else: a symlink is refused by guard_parent, and a
# directory is refused here (handoff Amendment 3 item 1).
guard_destination() {
    _gd_desc=$1
    _gd_target=$2
    guard_parent "$_gd_desc" "$_gd_target"
    if [ -d "$_gd_target" ]; then
        fail "refusing to replace $_gd_target: it is a directory"
    fi
}

reject_dot_components "HOME" "$home"
reject_dot_components "XDG_DATA_HOME" "$data_home"
reject_dot_components "XDG_CONFIG_HOME" "$config_home"

case $home in
/*) ;;
*) fail "HOME must be an absolute path: $home" ;;
esac

home_real=$(CDPATH= cd -P -- "$home" && pwd -P) ||
    fail "cannot resolve \$HOME ($home)"
# A case pattern of ""/* would be "/*"; HOME=/ is the only case that needs it.
if [ "$home_real" = "/" ]; then
    home_root=
else
    home_root=$home_real
fi

# The uninstaller removes only paths under $HOME, so the installer refuses to
# write anywhere else: nothing can be installed that could not be removed
# again (handoff Amendment 1 item 2). This is the physical check, so a
# symlinked XDG directory that really points outside $HOME is refused too.
require_under_home "XDG_DATA_HOME" "$data_home"
require_under_home "XDG_CONFIG_HOME" "$config_home"

app_dir=$data_home/SystemMonitor/app
app_new=$app_dir.new
app_old=$app_dir.old
sm_dir=$data_home/SystemMonitor
uninstall_file=$sm_dir/uninstall.sh
menu_dir=$data_home/applications
menu_file=$menu_dir/systemmonitor.desktop
icon_dir=$data_home/icons/hicolor/256x256/apps
icon_file=$icon_dir/systemmonitor.png
launcher_dir=$home/.local/bin
launcher=$launcher_dir/sysmonitor

here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)

[ -f "$here/app/sysmonitor" ] ||
    fail "this archive is incomplete: app/sysmonitor is missing"
[ -f "$here/icons/systemmonitor.png" ] ||
    fail "this archive is incomplete: icons/systemmonitor.png is missing"
[ -f "$here/systemmonitor.desktop" ] ||
    fail "this archive is incomplete: systemmonitor.desktop is missing"
[ -f "$here/uninstall.sh" ] ||
    fail "this archive is incomplete: uninstall.sh is missing"

# Refuse to remove anything that is not clearly ours: absolute, below the home
# directory, and below a SystemMonitor directory. An empty or unset variable
# can therefore never turn into "rm -rf /" (handoff section 3). The physical
# parent check runs again inside guard_parent, immediately before rm.
remove_tree() {
    target=$1
    [ -n "$target" ] || fail "internal error: empty path"
    case $target in
    /*) ;;
    *) fail "refusing to remove a relative path: $target" ;;
    esac
    guard_parent "the removal target" "$target"
    case $target in
    */SystemMonitor | */SystemMonitor/*) ;;
    *) fail "refusing to remove $target: not a SystemMonitor path" ;;
    esac
    rm -rf -- "$target"
}

# Stage one of the Exec quoting: inside "..." a backslash escapes " ` $ and
# \, and % becomes %% for the spec's field codes. Stage two doubles every
# backslash, because the value lives in a key file (phase 3 handoff,
# Amendment 2 item 3).
desktop_exec() {
    quoted=$(printf '%s' "$1" |
        sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' -e 's/`/\\`/g' -e 's/\$/\\$/g' -e 's/%/%%/g')
    printf '"%s"' "$quoted" | sed -e 's/\\/\\\\/g'
}

fill_desktop_template() {
    template=$1
    output=$2
    exec_value=$3
    while IFS= read -r line || [ -n "$line" ]; do
        case $line in
        'Exec=@EXEC@') printf 'Exec=%s\n' "$exec_value" ;;
        *) printf '%s\n' "$line" ;;
        esac
    done <"$template" >"$output"
}

# Wait for the old program to exit: 150 polls of 0.2 s = 30 s. Nothing on
# disk has been touched yet, so a timeout leaves the install as it was.
wait_for_exit() {
    pid=$1
    tries=0
    while [ "$tries" -lt 150 ]; do
        if ! kill -0 "$pid" 2>/dev/null; then
            return 0
        fi
        sleep 0.2
        tries=$((tries + 1))
    done
    if kill -0 "$pid" 2>/dev/null; then
        return 1
    fi
    return 0
}

if [ "$update" -eq 1 ] && [ "$wait_pid_given" -eq 1 ]; then
    say "waiting for process $wait_pid to exit (up to 30 s)"
    wait_for_exit "$wait_pid" ||
        fail "process $wait_pid is still running after 30 s; nothing was changed"
fi

say "installing into $app_dir"
ensure_dir "the SystemMonitor data directory" "$sm_dir"

# A symlink or a plain file at app.new/app.old is not ours to delete or move.
refuse_scratch "$app_new"
refuse_scratch "$app_old"
if [ -L "$app_dir" ]; then
    fail "refusing to install: $app_dir is a symbolic link"
fi
if [ -e "$app_dir" ] && [ ! -d "$app_dir" ]; then
    fail "refusing to install: $app_dir is not a directory"
fi

# Recovery (handoff Amendment 2 item 2): a run interrupted between the two
# renames leaves app/ missing with the previous version in app.old/. Put it
# back, then drop a stale app.new, so the swap below starts from a complete
# install. Nothing is claimed to be atomic.
if [ ! -e "$app_dir" ] && [ -d "$app_old" ]; then
    guard_parent "the previous version" "$app_old"
    guard_parent "the restored install" "$app_dir"
    mv -- "$app_old" "$app_dir" ||
        fail "could not restore the previous version from $app_old"
    say "restored the previous version from $app_old"
fi
remove_tree "$app_new"

# The swap is two renames: build app.new beside the live app, move the live
# one to app.old, then move app.new into place.
refuse_scratch "$app_new"
cp -R -- "$here/app" "$app_new" ||
    fail "could not copy the program files to $app_new"
chmod 755 -- "$app_new/sysmonitor"

if [ -d "$app_old" ]; then
    remove_tree "$app_old"
fi
refuse_scratch "$app_new"
if [ -d "$app_dir" ]; then
    guard_parent "the live install" "$app_dir"
    mv -- "$app_dir" "$app_old" || fail "could not move the old install aside"
fi
guard_parent "the new install" "$app_new"
# Forward rename. The test hook named below reports this one rename as failed
# without running the move, so the rollback that follows runs for real
# (handoff R-027 phase 4, Amendment 4).
swapped=1
if [ "${SYSMONITOR_TEST_FAIL_SWAP:-0}" = "1" ]; then
    swapped=0
elif ! mv -- "$app_new" "$app_dir"; then
    swapped=0
fi
if [ "$swapped" -eq 0 ]; then
    # Put the previous version back. The rollback rename gets the same parent
    # and symlink check as the forward one, its status is checked, and every
    # failure path names app.old, so "restored" is only printed after a rename
    # that actually succeeded (handoff Amendment 3 item 2).
    warn "could not put the new files in place"
    if [ ! -d "$app_old" ]; then
        fail "nothing was restored: there is no previous version in $app_old"
    fi
    if [ -e "$app_dir" ]; then
        warn "the previous version is still in $app_old"
        fail "nothing was restored: $app_dir was not left empty"
    fi
    warn "restoring the previous version from $app_old"
    # The guard exits on refusal, so it runs in a subshell and its message is
    # kept: this path has to report the failure rather than vanish inside it.
    if ! rollback_message=$(guard_parent "the restored install" "$app_dir" 2>&1); then
        printf '%s\n' "$rollback_message" >&2
        warn "the restore failed; the previous version is still in $app_old"
        exit 1
    fi
    # The rollback rename, with the same kind of test hook: it reports this one
    # rename as failed without running the move, so the failed-restore message
    # can be tested too (handoff R-027 phase 4, Amendment 4).
    restored=1
    if [ "${SYSMONITOR_TEST_FAIL_RESTORE:-0}" = "1" ]; then
        restored=0
    elif ! mv -- "$app_old" "$app_dir"; then
        restored=0
    fi
    if [ "$restored" -eq 1 ]; then
        say "restored the previous version from $app_old"
        fail "the new files could not be installed; the previous version is back"
    fi
    warn "the restore failed; the previous version is still in $app_old"
    exit 1
fi
remove_tree "$app_old"
chmod 755 -- "$app_dir/sysmonitor"

# Keep the uninstaller beside app/, not inside it: the swap above replaces
# app/ in place, while this copy has to survive updates untouched. uninstall.sh
# finds the install from the XDG paths, so it works from here (handoff
# Amendment 1 item 1).
new_staging "$sm_dir" "uninstall.sh"
cp -- "$here/uninstall.sh" "$staging" ||
    fail "could not copy the uninstaller to $sm_dir"
chmod 755 -- "$staging"
guard_destination "the installed uninstaller" "$uninstall_file"
mv -f -- "$staging" "$uninstall_file" ||
    fail "could not write $uninstall_file"
staging=

# The launcher is ours to create; it is not in the archive (handoff section 2).
ensure_dir "the command directory" "$launcher_dir"
if [ -e "$launcher" ] && [ ! -L "$launcher" ]; then
    fail "refusing to replace $launcher: it exists and is not a symlink"
fi
ln -sfn -- "$app_dir/sysmonitor" "$launcher"

# Menu entry: same file name as the autostart entry, different directory.
ensure_dir "the application menu directory" "$menu_dir"
new_staging "$menu_dir" "systemmonitor.desktop"
fill_desktop_template "$here/systemmonitor.desktop" "$staging" \
    "$(desktop_exec "$app_dir/sysmonitor")"
chmod 644 -- "$staging"
guard_destination "the menu entry" "$menu_file"
mv -f -- "$staging" "$menu_file" ||
    fail "could not write $menu_file"
staging=

ensure_dir "the icon directory" "$icon_dir"
new_staging "$icon_dir" "systemmonitor.png"
cp -- "$here/icons/systemmonitor.png" "$staging" ||
    fail "could not copy the icon"
chmod 644 -- "$staging"
guard_destination "the icon" "$icon_file"
mv -f -- "$staging" "$icon_file" ||
    fail "could not write $icon_file"
staging=

# Refresh the desktop and icon caches when the tools exist. Failures are not
# ours to report: a missing cache only delays the menu showing up.
if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "$menu_dir" >/dev/null 2>&1 || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -q -t -f "$data_home/icons/hicolor" >/dev/null 2>&1 || true
fi

version=unknown
if [ -f "$here/VERSION" ]; then
    version=$(sed -n '1p' "$here/VERSION")
fi

say ""
say "System Monitor $version is installed."
say ""
say "  program     $app_dir/sysmonitor"
say "  command     $launcher"
say "  menu entry  $menu_file"
say "  icon        $icon_file"
say "  uninstall   $uninstall_file"
say ""
say "Start it from your application menu, or run: sysmonitor"
case :${PATH:-}: in
*:"$launcher_dir":*) ;;
*)
    warn "$launcher_dir is not on PATH; run $app_dir/sysmonitor or add it"
    ;;
esac

if [ "$update" -eq 1 ]; then
    # Environment hooks. SYSMONITOR_NO_LAUNCH=1 is the one user-facing variable:
    # it keeps --update from starting the program after the install.
    #   SYSMONITOR_NO_LAUNCH=1           install, but do not start the program.
    #   SYSMONITOR_TEST_FAIL_SWAP=1      make the forward "mv app.new app" fail,
    #                                    so the real rollback path runs.
    #   SYSMONITOR_TEST_FAIL_RESTORE=1   make the rollback "mv app.old app" fail.
    # The two SYSMONITOR_TEST_* variables exist only for the install-script tests
    # (handoff R-027 phase 4, Amendment 4). Each replaces that one rename's
    # result and nothing else: the rest of the path is real, and no other code
    # reads them.
    if [ "${SYSMONITOR_NO_LAUNCH:-0}" = "1" ]; then
        say "launch skipped (SYSMONITOR_NO_LAUNCH=1)"
    elif command -v setsid >/dev/null 2>&1; then
        setsid "$app_dir/sysmonitor" >/dev/null 2>&1 &
    else
        nohup "$app_dir/sysmonitor" >/dev/null 2>&1 &
    fi
fi

exit 0
