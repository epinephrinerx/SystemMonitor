using System.Runtime.InteropServices;
using SysMonitor.Extensibility;

namespace SysMonitor.Tests.DummyExtension;

public class DummyNativeExtension : IExtension
{
    // The test copies a harmless system DLL (shlwapi) to "dummynative.dll" and lists it in the
    // manifest. Not kernel32: loading and freeing a second copy of that one corrupts the process.
    [DllImport("dummynative.dll", EntryPoint = "StrToIntW", CharSet = CharSet.Unicode)]
    private static extern int NativeStrToInt(string text);

    /// <summary>42 when the call went through the preloaded library.</summary>
    public int NativeResult { get; private set; }

    public void Initialize(IExtensionContext context)
    {
        NativeResult = NativeStrToInt("42");
        context.Log($"DummyNativeExtension initialized with NativeResult {NativeResult}");
    }

    public void Shutdown()
    {
    }
}
