using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace SysMonitor.Extensions;

/// <summary>
/// Collectible AssemblyLoadContext that loads managed dependencies strictly from verified
/// in-memory byte arrays and resolves declared native DLLs from preloaded handles.
/// Fall-through to system resolution allows standard OS libraries (e.g. kernel32, user32)
/// while extension folder isolation is enforced by manifest file listing.
/// </summary>
internal sealed class ExtensionLoadContext : AssemblyLoadContext
{
    private readonly Dictionary<string, byte[]> _managedAssemblies;
    private readonly Dictionary<string, IntPtr> _nativeHandles;

    public ExtensionLoadContext(
        string name,
        Dictionary<string, byte[]> managedAssemblies,
        Dictionary<string, IntPtr> nativeHandles)
        : base(name, isCollectible: true)
    {
        _managedAssemblies = managedAssemblies;
        _nativeHandles = nativeHandles;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // The contract assembly must be shared with the host for type identity
        if (string.Equals(assemblyName.Name, "SysMonitor.Extensibility", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (assemblyName.Name != null)
        {
            if (_managedAssemblies.TryGetValue(assemblyName.Name, out byte[]? bytes)
                || _managedAssemblies.TryGetValue(assemblyName.Name + ".dll", out bytes))
            {
                using var stream = new MemoryStream(bytes);
                return LoadFromStream(stream);
            }
        }

        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string fileName = unmanagedDllName;
        if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".dll";
        }

        if (_nativeHandles.TryGetValue(fileName, out IntPtr handle)
            || _nativeHandles.TryGetValue(unmanagedDllName, out handle))
        {
            return handle;
        }

        return IntPtr.Zero;
    }
}
