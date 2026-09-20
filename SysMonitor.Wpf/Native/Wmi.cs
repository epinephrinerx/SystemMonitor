using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SysMonitor.Native;

/// <summary>
/// A WMI query, through the scripting COM object by late binding.
///
/// Not System.Management: that is a NuGet package, and this build has none.
/// Not a PowerShell subprocess either, which is what the Python build did --
/// spawning a process and waiting up to eight seconds for a number is a poor
/// trade for a widget whose whole point is costing nothing.
/// </summary>
internal static class Wmi
{
    /// <summary>
    /// Run a query and hand back the requested fields per row. Returns an
    /// empty list if WMI is unavailable, the class is not implemented, or
    /// access is denied -- all normal outcomes for hardware queries.
    /// </summary>
    public static List<Dictionary<string, object?>> Query(
        string wmiNamespace, string query, params string[] fields)
    {
        var rows = new List<Dictionary<string, object?>>();
        Type? locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
        if (locatorType is null)
        {
            return rows;
        }

        object? locator = null;
        object? services = null;
        object? results = null;
        try
        {
            locator = Activator.CreateInstance(locatorType);
            if (locator is null)
            {
                return rows;
            }
            services = Invoke(locator, "ConnectServer", ".", wmiNamespace);
            if (services is null)
            {
                return rows;
            }
            results = Invoke(services, "ExecQuery", query);
            if (results is not IEnumerable enumerable)
            {
                return rows;
            }

            foreach (object? item in enumerable)
            {
                if (item is null)
                {
                    continue;
                }
                try
                {
                    var row = new Dictionary<string, object?>();
                    object? properties = Get(item, "Properties_");
                    foreach (string field in fields)
                    {
                        object? property = properties is null
                            ? null : Invoke(properties, "Item", field);
                        row[field] = property is null ? null : Get(property, "Value");
                        Release(property);
                    }
                    Release(properties);
                    rows.Add(row);
                }
                finally
                {
                    Release(item);
                }
            }
        }
        catch (Exception)
        {
            // A missing class, a denied namespace or a broken WMI repository
            // are all "no reading", not failures worth surfacing.
            return rows;
        }
        finally
        {
            Release(results);
            Release(services);
            Release(locator);
        }
        return rows;
    }

    private static object? Invoke(object target, string method, params object[] args) =>
        target.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, target, args);

    private static object? Get(object target, string property) =>
        target.GetType().InvokeMember(property, BindingFlags.GetProperty, null, target, null);

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            try
            {
                Marshal.ReleaseComObject(com);
            }
            catch (Exception)
            {
                // Already released, or never ours to release.
            }
        }
    }
}
