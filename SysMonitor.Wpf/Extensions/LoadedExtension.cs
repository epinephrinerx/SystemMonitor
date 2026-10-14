using System.Runtime.Loader;
using SysMonitor.Extensibility;

namespace SysMonitor.Extensions;

/// <summary>
/// Represents an active loaded extension package, its collectible AssemblyLoadContext,
/// and its preloaded unmanaged library handles.
/// </summary>
public sealed class LoadedExtension : IDisposable
{
    private readonly AssemblyLoadContext _alc;
    private readonly IReadOnlyList<IntPtr> _nativeHandles;
    private readonly Action<string>? _log;
    private bool _disposed;

    public ExtensionManifest Manifest { get; }
    public IExtension Instance { get; }

    internal AssemblyLoadContext LoadContext => _alc;

    internal LoadedExtension(
        ExtensionManifest manifest,
        IExtension instance,
        AssemblyLoadContext alc,
        IReadOnlyList<IntPtr> nativeHandles,
        Action<string>? log)
    {
        Manifest = manifest;
        Instance = instance;
        _alc = alc;
        _nativeHandles = nativeHandles;
        _log = log;
    }

    /// <summary>
    /// Invokes Shutdown() on the extension instance (swallowing and logging any exceptions),
    /// unloads the underlying AssemblyLoadContext, and frees all preloaded unmanaged library handles.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            Instance.Shutdown();
        }
        catch (Exception ex)
        {
            _log.SafeLog($"Extension '{Manifest.Id}' threw an exception during Shutdown(): {ex.Describe()}");
        }

        try
        {
            // Unload() runs the extension's own Unloading handlers, which can throw.
            _alc.Unload();
        }
        catch (Exception ex)
        {
            _log.SafeLog($"Extension '{Manifest.Id}' threw an exception while unloading: {ex.Describe()}");
        }
        finally
        {
            foreach (IntPtr handle in _nativeHandles)
            {
                if (handle != IntPtr.Zero)
                {
                    try
                    {
                        NativeMethods.FreeLibrary(handle);
                    }
                    catch
                    {
                        // Ignore errors during native library release
                    }
                }
            }
        }
    }
}
