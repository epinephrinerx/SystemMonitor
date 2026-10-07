using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SysMonitor.Model;

namespace SysMonitor.Native;

/// <summary>
/// The Service Control Manager, called directly: no NuGet package, no WMI.
/// Listing reads the whole table in one call; the start type comes from the
/// registry, where the SCM keeps it, so nothing here needs elevation to look.
/// Changing a service does.
/// </summary>
internal static class ServiceControl
{
    private const uint ManagerConnect = 0x0001;
    private const uint ManagerEnumerate = 0x0004;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceChangeConfig = 0x0002;
    private const uint ServiceStartRight = 0x0010;
    private const uint ServiceStopRight = 0x0020;
    private const uint Win32Services = 0x00000030;
    private const uint StateAll = 0x00000003;
    private const int InfoLevelProcess = 0;
    private const uint NoChange = 0xFFFFFFFF;

    private const uint StateStopped = 1;
    private const uint StateRunning = 4;

    public const int ErrorAccessDenied = 5;
    private const int ErrorMoreData = 234;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct EnumServiceStatusProcess
    {
        public IntPtr ServiceName;
        public IntPtr DisplayName;
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManagerW(string? machine, string? database, uint access);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenServiceW(IntPtr manager, string name, uint access);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool EnumServicesStatusExW(IntPtr manager, int infoLevel,
        uint serviceType, uint state, IntPtr buffer, uint size, out uint needed,
        out uint returned, ref uint resume, string? group);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool ControlService(IntPtr service, uint control, out ServiceStatus status);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatus(IntPtr service, out ServiceStatus status);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool StartServiceW(IntPtr service, uint argc, IntPtr argv);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ChangeServiceConfigW(IntPtr service, uint serviceType,
        uint startType, uint errorControl, string? path, string? group, IntPtr tag,
        string? dependencies, string? account, string? password, string? display);

    // ---------------------------------------------------------------- listing
    /// <summary>Every Win32 service with its state and process id. Empty if the SCM will not say.</summary>
    public static List<ServiceEntry> List()
    {
        var result = new List<ServiceEntry>();
        IntPtr manager = OpenSCManagerW(null, null, ManagerEnumerate);
        if (manager == IntPtr.Zero)
        {
            return result;
        }

        IntPtr buffer = IntPtr.Zero;
        try
        {
            uint resume = 0;
            EnumServicesStatusExW(manager, InfoLevelProcess, Win32Services, StateAll,
                IntPtr.Zero, 0, out uint needed, out _, ref resume, null);
            uint size = needed + 4096;

            // The table can grow between the sizing call and the real one.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                buffer = Marshal.AllocHGlobal((int)size);
                resume = 0;
                bool ok = EnumServicesStatusExW(manager, InfoLevelProcess, Win32Services,
                    StateAll, buffer, size, out needed, out uint returned, ref resume, null);
                if (ok)
                {
                    int stride = Marshal.SizeOf<EnumServiceStatusProcess>();
                    for (int i = 0; i < returned; i++)
                    {
                        var row = Marshal.PtrToStructure<EnumServiceStatusProcess>(
                            buffer + i * stride);
                        string name = Marshal.PtrToStringUni(row.ServiceName) ?? string.Empty;
                        if (name.Length == 0)
                        {
                            continue;
                        }
                        result.Add(new ServiceEntry
                        {
                            Name = name,
                            Display = Marshal.PtrToStringUni(row.DisplayName) ?? name,
                            Running = row.CurrentState == StateRunning,
                            State = StateText(row.CurrentState),
                            Pid = (int)row.ProcessId,
                            Start = ReadStartType(name),
                        });
                    }
                    return result;
                }
                if (Marshal.GetLastWin32Error() != ErrorMoreData)
                {
                    return result;
                }
                Marshal.FreeHGlobal(buffer);
                buffer = IntPtr.Zero;
                size = needed + 4096;
            }
            return result;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }
            CloseServiceHandle(manager);
        }
    }

    internal static string StateText(uint state) => state switch
    {
        1 => "Stopped",
        2 => "Start Pending",
        3 => "Stop Pending",
        4 => "Running",
        5 => "Continue Pending",
        6 => "Pause Pending",
        7 => "Paused",
        _ => "Unknown",
    };

    /// <summary>2 is automatic, 3 manual, 4 disabled; boot and system drivers are "other".</summary>
    internal static ServiceStart StartFromRegistry(int? value) => value switch
    {
        2 => ServiceStart.Automatic,
        3 => ServiceStart.Manual,
        4 => ServiceStart.Disabled,
        _ => ServiceStart.Other,
    };

    private static ServiceStart ReadStartType(string name)
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\" + name);
            return StartFromRegistry(key?.GetValue("Start") as int?);
        }
        catch (Exception)
        {
            return ServiceStart.Other;
        }
    }

    // ---------------------------------------------------------------- changing
    /// <summary>The outcome of changing a service. Code is a Win32 error, 0 for success.</summary>
    public readonly record struct Result(int Code)
    {
        public bool Ok => Code == 0;
        public bool Denied => Code == ErrorAccessDenied;
        public string Message => Ok ? string.Empty : new Win32Exception(Code).Message;
    }

    public static Result Stop(string name) =>
        With(name, ServiceStopRight | ServiceQueryStatus, handle =>
        {
            if (!QueryServiceStatus(handle, out ServiceStatus current))
            {
                return Marshal.GetLastWin32Error();
            }
            if (current.CurrentState == StateStopped)
            {
                return 0;
            }
            if (!ControlService(handle, 1, out _))
            {
                return Marshal.GetLastWin32Error();
            }
            return WaitFor(handle, StateStopped);
        });

    public static Result Start(string name) =>
        With(name, ServiceStartRight | ServiceQueryStatus, handle =>
        {
            if (!QueryServiceStatus(handle, out ServiceStatus current))
            {
                return Marshal.GetLastWin32Error();
            }
            if (current.CurrentState == StateRunning)
            {
                return 0;
            }
            if (!StartServiceW(handle, 0, IntPtr.Zero))
            {
                return Marshal.GetLastWin32Error();
            }
            return WaitFor(handle, StateRunning);
        });

    public static Result Restart(string name)
    {
        Result stopped = Stop(name);
        return stopped.Ok ? Start(name) : stopped;
    }

    public static Result SetStart(string name, ServiceStart start)
    {
        uint type = start switch
        {
            ServiceStart.Automatic => 2,
            ServiceStart.Manual => 3,
            ServiceStart.Disabled => 4,
            _ => NoChange,
        };
        if (type == NoChange)
        {
            return new Result(87);   // ERROR_INVALID_PARAMETER
        }
        return With(name, ServiceChangeConfig, handle =>
            ChangeServiceConfigW(handle, NoChange, type, NoChange, null, null,
                IntPtr.Zero, null, null, null, null)
                ? 0
                : Marshal.GetLastWin32Error());
    }

    private static Result With(string name, uint access, Func<IntPtr, int> action)
    {
        IntPtr manager = OpenSCManagerW(null, null, ManagerConnect);
        if (manager == IntPtr.Zero)
        {
            return new Result(Marshal.GetLastWin32Error());
        }
        try
        {
            IntPtr service = OpenServiceW(manager, name, access);
            if (service == IntPtr.Zero)
            {
                return new Result(Marshal.GetLastWin32Error());
            }
            try
            {
                return new Result(action(service));
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    /// <summary>Wait up to 30 seconds for the state; 1460 is ERROR_TIMEOUT.</summary>
    private static int WaitFor(IntPtr handle, uint wanted)
    {
        for (int i = 0; i < 120; i++)
        {
            if (QueryServiceStatus(handle, out ServiceStatus now) && now.CurrentState == wanted)
            {
                return 0;
            }
            Thread.Sleep(250);
        }
        return 1460;
    }
}
