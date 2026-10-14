using System.Runtime.InteropServices;

namespace SysMonitor.Extensions;

/// <summary>
/// Native P/Invoke methods for secure unmanaged library loading and unloading.
/// </summary>
internal static class NativeMethods
{
    public const uint LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR = 0x00000100;
    public const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadLibraryEx(string lpLibFileName, IntPtr hFile, uint dwFlags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FreeLibrary(IntPtr hLibModule);
}
