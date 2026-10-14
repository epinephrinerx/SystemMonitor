using System.IO;
using SysMonitor.Extensibility;

namespace SysMonitor.Tests.DummyExtension;

public class DummyNormalExtension : IExtension
{
    public static bool Initialized { get; set; }
    public static bool ShutdownCalled { get; set; }
    public static IExtensionContext? LastContext { get; set; }

    public void Initialize(IExtensionContext context)
    {
        Initialized = true;
        LastContext = context;
        context.Log("DummyNormalExtension initialized");

        try
        {
            File.WriteAllText(Path.Combine(context.ExtensionDirectory, "initialized.txt"), "ok");
            File.WriteAllLines(Path.Combine(context.ExtensionDirectory, "context_marker.txt"), new[]
            {
                context.Language,
                context.HostIsElevated.ToString(),
                context.HostContractVersion.ToString(),
                context.ExtensionDirectory
            });
        }
        catch
        {
            // Ignore if directory not writable
        }
    }

    public void Shutdown()
    {
        ShutdownCalled = true;
        if (LastContext != null)
        {
            try
            {
                File.WriteAllText(Path.Combine(LastContext.ExtensionDirectory, "shutdown.txt"), "ok");
            }
            catch
            {
                // Ignore
            }
        }
    }
}
