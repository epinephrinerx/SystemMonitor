#!/bin/sh
# System Monitor for Linux -- per-user uninstaller (R-027 phase 4).
#
# Removes what install.sh put in place and leaves the settings alone unless
# --purge is given. Files that are not ours are left where they are: the
# launcher only goes if it is a symlink into our program directory, and the
# autostart entry only if its Exec still points there.
#
# install.sh copies this script to $XDG_DATA_HOME/SystemMonitor/uninstall.sh,
# so it is normally run from there after the unpacked archive is gone. It
# locates the install from the XDG paths, never from its own location, and
# removes that copy plus the now-empty SystemMonitor/ directory (handoff
# Amendment 1 item 1).
#
# Every path is checked twice -- once while the script builds it, and again
# immediately before the removal -- because an XDG directory can be a symlink
# that leads outside $HOME (handoff Amendment 2 item 1).
set -eu

program=$(basename -- "$0")

usage() {
    cat <<'USAGE'
Usage: ./uninstall.sh [--purge]

  --purge   also remove the settings and the log directory
  --help    show this text

Run this from the unpacked archive, or use the copy the installer leaves at
$XDG_DATA_HOME/SystemMonitor/uninstall.sh.

Without --purge the settings in $XDG_CONFIG_HOME/SystemMonitor are kept.
USAGE
}

say() { printf '%s\n' "$*"; }
fail() {
    printf '%s: %s\n' "$program" "$*" >&2
    exit 1
}

purge=0
while [ $# -gt 0 ]; do
    case $1 in
    --purge) purge=1 ;;
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

if [ "$(id -u)" -eq 0 ]; then
    fail "run this as the user who installed System Monitor, not as root"
fi

[ -n "${HOME:-}" ] || fail "HOME is not set"
home=$HOME
# One trailing slash would break every "$home"/* comparison below.
if [ "$home" != "/" ]; then
    home=${home%/}
fi
data_home=${XDG_DATA_HOME:-$home/.local/share}
config_home=${XDG_CONFIG_HOME:-$home/.config}
state_home=${XDG_STATE_HOME:-$home/.local/state}

# --- physical path boundary (handoff Amendment 2 item 1) --------------------
#
# A lexical "is it under $HOME" test is not enough. An XDG directory can be a
# symlink that leads somewhere else, and a ".." component can climb out of the
# home directory while the path still starts with it. Every path is therefore
# checked twice: once when the script builds it, and again immediately before
# the removal that uses it. install.sh duplicates these helpers on purpose:
# each script has to stand alone.

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

# Re-check right before a removal: the parent is resolved again (a directory
# may have become a symlink since the script started), and a tree that is
# itself a symlink is refused. Plain files are deleted with rm -f, which never
# follows a link, so a symlink there is harmless.
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
        fail "refusing to remove $_gp_target: it is a symbolic link, not a tree"
    fi
}

reject_dot_components "HOME" "$home"
reject_dot_components "XDG_DATA_HOME" "$data_home"
reject_dot_components "XDG_CONFIG_HOME" "$config_home"
reject_dot_components "XDG_STATE_HOME" "$state_home"

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

require_under_home "XDG_DATA_HOME" "$data_home"
require_under_home "XDG_CONFIG_HOME" "$config_home"

sm_dir=$data_home/SystemMonitor
app_dir=$sm_dir/app
uninstall_file=$sm_dir/uninstall.sh
menu_dir=$data_home/applications
menu_file=$menu_dir/systemmonitor.desktop
icon_dir=$data_home/icons/hicolor/256x256/apps
icon_file=$icon_dir/systemmonitor.png
launcher_dir=$home/.local/bin
launcher=$launcher_dir/sysmonitor
autostart_file=$config_home/autostart/systemmonitor.desktop
settings_dir=$config_home/SystemMonitor
log_dir=$state_home/SystemMonitor

# Preflight: every path this run may remove is checked before the first
# removal, so a refused path cannot leave a half-removed install behind. The
# state directory is only touched by --purge, so it is only checked then.
require_under_home "the program directory" "$app_dir"
require_under_home "the command directory" "$launcher_dir"
require_under_home "the application menu directory" "$menu_dir"
require_under_home "the icon directory" "$icon_dir"
require_under_home "the autostart directory" "$config_home/autostart"
require_under_home "the SystemMonitor data directory" "$sm_dir"
if [ -L "$app_dir" ]; then
    fail "refusing to remove $app_dir: it is a symbolic link, not a tree"
fi
if [ "$purge" -eq 1 ]; then
    require_under_home "XDG_STATE_HOME" "$state_home"
    require_under_home "the settings directory" "$settings_dir"
    require_under_home "the log directory" "$log_dir"
    for _pf in "$settings_dir" "$log_dir"; do
        if [ -L "$_pf" ]; then
            fail "refusing to remove $_pf: it is a symbolic link, not a tree"
        fi
    done
fi

# The inverses of the two escaping stages install.sh and the app apply:
# halve the backslashes, drop the surrounding quotes, then remove the
# Exec-level backslashes and turn %% back into %.
unquote_exec() {
    sed -e 's/[[:space:]]*$//' \
        -e 's/\\\\/\\/g' \
        -e 's/^"//' \
        -e 's/"$//' \
        -e 's/\\\(.\)/\1/g' \
        -e 's/%%/%/g'
}

remove_tree() {
    path=$1
    guard_parent "the removal target" "$path"
    case $path in
    */SystemMonitor | */SystemMonitor/*) ;;
    *) fail "refusing to remove $path: not a SystemMonitor path" ;;
    esac
    rm -rf -- "$path"
}

remove_file() {
    path=$1
    case $path in
    /*) ;;
    *) fail "refusing to remove a relative path: $path" ;;
    esac
    case $path in
    */*) parent=${path%/*} ;;
    *) parent=/ ;;
    esac
    require_under_home "the parent of $path" "${parent:-/}"
    rm -f -- "$path"
}

removed=0

if [ -d "$app_dir" ]; then
    remove_tree "$app_dir"
    say "removed $app_dir"
    removed=1
fi

if [ -L "$launcher" ]; then
    target=$(readlink -- "$launcher" 2>/dev/null || true)
    case $target in
    "$app_dir"/*)
        remove_file "$launcher"
        say "removed $launcher"
        removed=1
        ;;
    *)
        say "kept $launcher: it points at $target, not at $app_dir"
        ;;
    esac
fi

if [ -f "$menu_file" ]; then
    remove_file "$menu_file"
    say "removed $menu_file"
    removed=1
fi

if [ -f "$icon_file" ]; then
    remove_file "$icon_file"
    say "removed $icon_file"
    removed=1
fi

if [ -f "$autostart_file" ]; then
    exec_line=$(sed -n -e 's/^[[:space:]]*Exec[[:space:]]*=[[:space:]]*//p' "$autostart_file" | sed -n '1p')
    autostart_target=$(printf '%s\n' "$exec_line" | unquote_exec)
    case $autostart_target in
    "$app_dir"/*)
        remove_file "$autostart_file"
        say "removed $autostart_file"
        removed=1
        ;;
    *)
        say "kept $autostart_file: its Exec is $autostart_target"
        ;;
    esac
fi

if [ "$purge" -eq 1 ]; then
    if [ -d "$settings_dir" ]; then
        remove_tree "$settings_dir"
        say "removed $settings_dir"
        removed=1
    fi
    if [ -d "$log_dir" ]; then
        remove_tree "$log_dir"
        say "removed $log_dir"
        removed=1
    fi
else
    if [ -d "$settings_dir" ]; then
        say "kept $settings_dir (use --purge to remove it)"
    fi
fi

# The copy install.sh left beside app/ goes too. This is normally the running
# script: on Linux the open file stays readable after its name is unlinked, so
# the rest of this run is unaffected (handoff Amendment 1 item 1).
if [ -f "$uninstall_file" ]; then
    remove_file "$uninstall_file"
    say "removed $uninstall_file"
    removed=1
fi

# Remove the SystemMonitor data directory only when nothing else lives in it;
# a leftover app.old or any other file keeps it.
if [ -d "$sm_dir" ]; then
    require_under_home "the SystemMonitor data directory" "$sm_dir"
    if rmdir -- "$sm_dir" 2>/dev/null; then
        say "removed $sm_dir"
    else
        say "kept $sm_dir: it is not empty"
    fi
fi

# Refresh the caches the same way install.sh does, so the menu entry and the
# icon disappear now rather than at the next sign-in.
if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "$menu_dir" >/dev/null 2>&1 || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -q -t -f "$data_home/icons/hicolor" >/dev/null 2>&1 || true
fi

if [ "$removed" -eq 0 ]; then
    say "nothing to remove: System Monitor does not look installed for this user"
fi

exit 0
