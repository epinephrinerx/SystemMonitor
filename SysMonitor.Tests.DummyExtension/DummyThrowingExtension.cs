using SysMonitor.Extensibility;

namespace SysMonitor.Tests.DummyExtension;

public class DummyThrowingExtension : IExtension
{
    public void Initialize(IExtensionContext context)
    {
        context.Log("DummyThrowingExtension initializing: throwing deliberate exception");
        throw new InvalidOperationException("Deliberate failure in DummyThrowingExtension.Initialize");
    }

    public void Shutdown()
    {
    }
}
