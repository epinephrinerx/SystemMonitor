"""Thin ctypes bindings for the Win32 APIs SysMonitor needs.

Everything here is stdlib-only.  No psutil, no WMI, no subprocesses on the hot
path -- just direct syscalls, which is what keeps the widget near 0% CPU.
"""

import ctypes
from ctypes import wintypes

kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
ntdll = ctypes.WinDLL("ntdll")
user32 = ctypes.WinDLL("user32", use_last_error=True)

INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value
GENERIC_READ = 0x80000000
FILE_SHARE_READ = 0x00000001
FILE_SHARE_WRITE = 0x00000002
OPEN_EXISTING = 3

SEM_FAILCRITICALERRORS = 0x0001
SEM_NOOPENFILEERRORBOX = 0x8000

DRIVE_REMOVABLE = 2
DRIVE_FIXED = 3
DRIVE_REMOTE = 4
DRIVE_CDROM = 5
DRIVE_RAMDISK = 6

# CTL_CODE(IOCTL_DISK_BASE 0x07, 0x0008, METHOD_BUFFERED, FILE_ANY_ACCESS)
IOCTL_DISK_PERFORMANCE = 0x00070020
# CTL_CODE(IOCTL_STORAGE_BASE 0x2d, 0x0500 / 0x0420, METHOD_BUFFERED, FILE_ANY_ACCESS)
IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400
IOCTL_STORAGE_GET_DEVICE_NUMBER = 0x002D1080

# STORAGE_PROPERTY_ID members we use
StorageAdapterProperty = 1
StorageDeviceSeekPenaltyProperty = 7
StorageDeviceTemperatureProperty = 22
PropertyStandardQuery = 0

# STORAGE_BUS_TYPE -> human label
BUS_TYPES = {
    0: "Unknown", 1: "SCSI", 2: "ATAPI", 3: "ATA", 4: "1394", 5: "SSA",
    6: "Fibre", 7: "USB", 8: "RAID", 9: "iSCSI", 10: "SAS", 11: "SATA",
    12: "SD", 13: "MMC", 14: "Virtual", 15: "Virtual", 16: "Spaces",
    17: "NVMe", 18: "SCM", 19: "UFS",
}

SystemProcessorPerformanceInformation = 8

kernel32.CreateFileW.restype = wintypes.HANDLE
kernel32.CreateFileW.argtypes = [
    wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, wintypes.LPVOID,
    wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE,
]
kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
kernel32.DeviceIoControl.argtypes = [
    wintypes.HANDLE, wintypes.DWORD, wintypes.LPVOID, wintypes.DWORD,
    wintypes.LPVOID, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD), wintypes.LPVOID,
]
kernel32.GetLogicalDrives.restype = wintypes.DWORD
kernel32.GetDriveTypeW.argtypes = [wintypes.LPCWSTR]
kernel32.GetDriveTypeW.restype = wintypes.UINT
kernel32.GetCurrentProcess.restype = wintypes.HANDLE


class MEMORYSTATUSEX(ctypes.Structure):
    _fields_ = [
        ("dwLength", wintypes.DWORD),
        ("dwMemoryLoad", wintypes.DWORD),
        ("ullTotalPhys", ctypes.c_ulonglong),
        ("ullAvailPhys", ctypes.c_ulonglong),
        ("ullTotalPageFile", ctypes.c_ulonglong),
        ("ullAvailPageFile", ctypes.c_ulonglong),
        ("ullTotalVirtual", ctypes.c_ulonglong),
        ("ullAvailVirtual", ctypes.c_ulonglong),
        ("ullAvailExtendedVirtual", ctypes.c_ulonglong),
    ]


class SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION(ctypes.Structure):
    """Per-logical-processor time counters.  48 bytes on x64."""
    _fields_ = [
        ("IdleTime", ctypes.c_longlong),
        ("KernelTime", ctypes.c_longlong),
        ("UserTime", ctypes.c_longlong),
        ("Reserved1", ctypes.c_longlong * 2),
        ("Reserved2", ctypes.c_ulong),
    ]


def quiet_error_dialogs():
    """Stop Windows popping 'no disk in drive' boxes when we probe drives."""
    kernel32.SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOOPENFILEERRORBOX)


def trim_working_set():
    """Hand freed pages back to Windows.  Costs nothing, keeps RSS honest."""
    try:
        h = kernel32.GetCurrentProcess()
        kernel32.SetProcessWorkingSetSize(h, ctypes.c_size_t(-1), ctypes.c_size_t(-1))
    except Exception:
        pass


def set_dpi_aware():
    """Per-monitor v2 if available, else fall back down the ladder."""
    try:
        # DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
        if user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4)):
            return
    except Exception:
        pass
    try:
        ctypes.WinDLL("shcore").SetProcessDpiAwareness(2)
        return
    except Exception:
        pass
    try:
        user32.SetProcessDPIAware()
    except Exception:
        pass


class RECT(ctypes.Structure):
    _fields_ = [("left", ctypes.c_long), ("top", ctypes.c_long),
                ("right", ctypes.c_long), ("bottom", ctypes.c_long)]


class MONITORINFO(ctypes.Structure):
    _fields_ = [("cbSize", wintypes.DWORD), ("rcMonitor", RECT),
                ("rcWork", RECT), ("dwFlags", wintypes.DWORD)]


class POINT(ctypes.Structure):
    _fields_ = [("x", ctypes.c_long), ("y", ctypes.c_long)]


MONITOR_DEFAULTTONEAREST = 2

user32.MonitorFromPoint.argtypes = [POINT, wintypes.DWORD]
user32.MonitorFromPoint.restype = wintypes.HANDLE
user32.GetMonitorInfoW.argtypes = [wintypes.HANDLE, ctypes.POINTER(MONITORINFO)]


def work_area(x, y):
    """Usable desktop bounds (taskbar excluded) of the monitor under a point.

    Returns (left, top, right, bottom), or None if the call fails.
    """
    try:
        handle = user32.MonitorFromPoint(POINT(int(x), int(y)),
                                         MONITOR_DEFAULTTONEAREST)
        info = MONITORINFO()
        info.cbSize = ctypes.sizeof(MONITORINFO)
        if not user32.GetMonitorInfoW(handle, ctypes.byref(info)):
            return None
        r = info.rcWork
        return r.left, r.top, r.right, r.bottom
    except Exception:
        return None


def cpu_times():
    """Return [(idle, kernel, user), ...] per logical core, or None."""
    count = 64
    for _ in range(4):
        buf = (SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION * count)()
        needed = wintypes.ULONG(0)
        status = ntdll.NtQuerySystemInformation(
            SystemProcessorPerformanceInformation,
            ctypes.byref(buf), ctypes.sizeof(buf), ctypes.byref(needed),
        ) & 0xFFFFFFFF
        if status == 0:
            n = needed.value // ctypes.sizeof(SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION)
            return [(buf[i].IdleTime, buf[i].KernelTime, buf[i].UserTime) for i in range(n)]
        if status == 0xC0000004:  # STATUS_INFO_LENGTH_MISMATCH
            count *= 2
            continue
        return None
    return None


def memory_status():
    """(used_bytes, total_bytes) of physical RAM."""
    ms = MEMORYSTATUSEX()
    ms.dwLength = ctypes.sizeof(ms)
    if not kernel32.GlobalMemoryStatusEx(ctypes.byref(ms)):
        return None
    return ms.ullTotalPhys - ms.ullAvailPhys, ms.ullTotalPhys


def logical_drives(include_removable=True, include_network=False):
    """Drive letters that currently hold a usable volume."""
    mask = kernel32.GetLogicalDrives()
    wanted = {DRIVE_FIXED, DRIVE_RAMDISK}
    if include_removable:
        wanted.add(DRIVE_REMOVABLE)
    if include_network:
        wanted.add(DRIVE_REMOTE)
    out = []
    for i in range(26):
        if not mask & (1 << i):
            continue
        letter = chr(ord("A") + i)
        if kernel32.GetDriveTypeW(letter + ":\\") in wanted:
            out.append(letter)
    return out


def disk_space(letter):
    """(used_bytes, total_bytes) for a drive letter, or None if unreadable."""
    free = ctypes.c_ulonglong(0)
    total = ctypes.c_ulonglong(0)
    total_free = ctypes.c_ulonglong(0)
    ok = kernel32.GetDiskFreeSpaceExW(
        letter + ":\\", ctypes.byref(free), ctypes.byref(total), ctypes.byref(total_free)
    )
    if not ok or total.value == 0:
        return None
    return total.value - total_free.value, total.value


def volume_label(letter):
    name = ctypes.create_unicode_buffer(261)
    fsname = ctypes.create_unicode_buffer(261)
    serial = wintypes.DWORD()
    maxlen = wintypes.DWORD()
    flags = wintypes.DWORD()
    ok = kernel32.GetVolumeInformationW(
        letter + ":\\", name, 261, ctypes.byref(serial), ctypes.byref(maxlen),
        ctypes.byref(flags), fsname, 261,
    )
    if not ok:
        return ""
    return name.value


def _open_device(path, access=0):
    h = kernel32.CreateFileW(
        path, access, FILE_SHARE_READ | FILE_SHARE_WRITE, None,
        OPEN_EXISTING, 0, None,
    )
    if not h or h == INVALID_HANDLE_VALUE:
        return None
    return h


def _ioctl(handle, code, in_buf, out_size):
    out = ctypes.create_string_buffer(out_size)
    returned = wintypes.DWORD(0)
    if in_buf is None:
        in_ptr, in_len = None, 0
    else:
        in_ptr, in_len = ctypes.byref(in_buf), ctypes.sizeof(in_buf)
    ok = kernel32.DeviceIoControl(
        handle, code, in_ptr, in_len, out, out_size, ctypes.byref(returned), None
    )
    if not ok:
        return None
    return out.raw[: returned.value]


def volume_io_counters(letter):
    """(bytes_read, bytes_written) lifetime counters for a volume, or None.

    Uses IOCTL_DISK_PERFORMANCE, which Windows maintains for free -- no perf
    counter subsystem and no PowerShell, so a call costs tens of microseconds.
    """
    h = _open_device("\\\\.\\" + letter + ":")
    if h is None:
        return None
    try:
        raw = _ioctl(h, IOCTL_DISK_PERFORMANCE, None, 128)
    finally:
        kernel32.CloseHandle(h)
    if raw is None or len(raw) < 16:
        return None
    read = int.from_bytes(raw[0:8], "little", signed=True)
    written = int.from_bytes(raw[8:16], "little", signed=True)
    return read, written


def physical_drive_number(letter):
    h = _open_device("\\\\.\\" + letter + ":")
    if h is None:
        return None
    try:
        raw = _ioctl(h, IOCTL_STORAGE_GET_DEVICE_NUMBER, None, 32)
    finally:
        kernel32.CloseHandle(h)
    if raw is None or len(raw) < 8:
        return None
    number = int.from_bytes(raw[4:8], "little", signed=True)
    return None if number < 0 else number


def _storage_query(handle, property_id, out_size):
    # STORAGE_PROPERTY_QUERY { DWORD PropertyId; DWORD QueryType; BYTE Extra[1]; }
    query = ctypes.create_string_buffer(12)
    query.raw = (property_id.to_bytes(4, "little")
                 + PropertyStandardQuery.to_bytes(4, "little")
                 + b"\x00" * 4)
    return _ioctl(handle, IOCTL_STORAGE_QUERY_PROPERTY, query, out_size)


def drive_hardware(drive_no):
    """Static facts about a physical drive: (media_type, bus_label) or None.

    media_type is 'SSD' or 'HDD', derived from the seek-penalty descriptor.
    """
    h = _open_device("\\\\.\\PhysicalDrive%d" % drive_no)
    if h is None:
        return None
    media, bus = None, None
    try:
        raw = _storage_query(h, StorageDeviceSeekPenaltyProperty, 32)
        if raw is not None and len(raw) >= 9:
            media = "HDD" if raw[8] else "SSD"
        raw = _storage_query(h, StorageAdapterProperty, 128)
        if raw is not None and len(raw) >= 25:
            bus = BUS_TYPES.get(raw[24], "Unknown")
    finally:
        kernel32.CloseHandle(h)
    if media is None and bus is None:
        return None
    return media or "Disk", bus or "Unknown"


def drive_temperature(drive_no):
    """Drive temperature in Celsius from the storage stack, or None.

    Works on NVMe and most modern SATA SSDs (Windows 10 1803+).  Older or
    USB-bridged drives simply do not expose one.
    """
    for access in (0, GENERIC_READ):
        h = _open_device("\\\\.\\PhysicalDrive%d" % drive_no, access)
        if h is None:
            continue
        try:
            raw = _storage_query(h, StorageDeviceTemperatureProperty, 256)
        finally:
            kernel32.CloseHandle(h)
        # The descriptor has an eight-byte Reserved1 array at offset 16.
        # TemperatureInfo starts at 24; each complete entry is 16 bytes.
        if raw is not None and len(raw) >= 40:
            info_count = int.from_bytes(raw[12:14], "little")
            size = int.from_bytes(raw[4:8], "little")
            if info_count and 24 + info_count * 16 <= min(size, len(raw)):
                temp = int.from_bytes(raw[26:28], "little", signed=True)
                if -50 < temp < 150:
                    return temp
    return None
