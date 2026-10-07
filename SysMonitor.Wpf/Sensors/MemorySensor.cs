using System.Diagnostics;
using SysMonitor.Model;
using SysMonitor.Native;

namespace SysMonitor.Sensors;

/// <summary>
/// What is running and what it holds. Process sizes come straight from the
/// process table, which needs no handle on the process, so a protected
/// process still reports; one that throws anyway is listed as unreadable
/// rather than left out.
/// </summary>
internal static class MemorySensor
{
    public static List<ProcessEntry> ReadProcesses()
    {
        var list = new List<ProcessEntry>();
        Process[] all;
        try
        {
            all = Process.GetProcesses();
        }
        catch (Exception)
        {
            return list;
        }

        foreach (Process process in all)
        {
            try
            {
                list.Add(Entry(process));
            }
            finally
            {
                process.Dispose();
            }
        }
        return list;
    }

    private static ProcessEntry Entry(Process process)
    {
        int pid;
        string name;
        try
        {
            pid = process.Id;
            name = process.ProcessName;
        }
        catch (Exception)
        {
            return new ProcessEntry { Pid = 0, Name = "?", Readable = false };
        }

        try
        {
            return new ProcessEntry
            {
                Pid = pid,
                Name = name,
                WorkingSet = process.WorkingSet64,
                PrivateBytes = process.PrivateMemorySize64,
            };
        }
        catch (Exception)
        {
            return new ProcessEntry { Pid = pid, Name = name, Readable = false };
        }
    }

    public static List<ServiceEntry> ReadServices() => ServiceControl.List();
}
