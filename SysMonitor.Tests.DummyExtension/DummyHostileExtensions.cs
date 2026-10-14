using System.Runtime.Loader;
using SysMonitor.Extensibility;

namespace SysMonitor.Tests.DummyExtension;

/// <summary>An exception whose Message getter itself throws, as extension-defined exceptions may.</summary>
public sealed class BrokenMessageException : Exception
{
    public override string Message => throw new InvalidOperationException("Message getter failed");
}

public class DummyBrokenMessageExtension : IExtension
{
    public void Initialize(IExtensionContext context) => throw new BrokenMessageException();

    public void Shutdown()
    {
    }
}

/// <summary>Registers an Unloading handler that throws, which Unload() runs synchronously.</summary>
public class DummyThrowingUnloadingExtension : IExtension
{
    public void Initialize(IExtensionContext context)
    {
        AssemblyLoadContext.GetLoadContext(GetType().Assembly)!.Unloading +=
            _ => throw new InvalidOperationException("Unloading handler failed");
    }

    public void Shutdown()
    {
    }
}
