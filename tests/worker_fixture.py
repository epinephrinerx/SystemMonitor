"""Spawn fixture: exercise real IPC with entirely synthetic hardware."""


def simulated_worker(connection, values):
    from sysmonitor import process_sampler, win32
    win32.quiet_error_dialogs = lambda: None
    win32.cpu_times = lambda: [(0, 100, 100)]
    win32.memory_status = lambda: (1024 ** 3, 4 * 1024 ** 3)
    win32.logical_drives = lambda *args: ["X"]
    win32.physical_drive_number = lambda *args: None
    win32.volume_label = lambda *args: "Synthetic"
    win32.disk_space = lambda *args: (1024 ** 3, 8 * 1024 ** 3)
    win32.volume_io_counters = lambda *args: (0, 0)
    values["diagnostics"] = False
    process_sampler._worker(connection, values)
