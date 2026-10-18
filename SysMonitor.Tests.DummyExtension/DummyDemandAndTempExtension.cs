using SysMonitor.Extensibility;

namespace SysMonitor.Tests.DummyExtension;

/// <summary>
/// Dummy extension implementing Contract 1.1 features (IDemandAware and IDiskTemperatureSource).
/// Uses AppDomain data to communicate across AssemblyLoadContext boundaries.
/// </summary>
public class DummyDemandAndTempExtension : DummyViewsExtension, IDemandAware, IDiskTemperatureSource
{
    private static T? ReadData<T>(string name) => AppDomain.CurrentDomain.GetData("DummyDemand." + name) is T value ? value : default;
    private static void WriteData(string name, object? value) => AppDomain.CurrentDomain.SetData("DummyDemand." + name, value);

    public static List<int> DemandHistory
    {
        get => ReadData<List<int>>(nameof(DemandHistory)) ?? new List<int>();
        set => WriteData(nameof(DemandHistory), value);
    }

    public static int SetDemandCallCount
    {
        get => ReadData<int>(nameof(SetDemandCallCount));
        set => WriteData(nameof(SetDemandCallCount), value);
    }

    public static IReadOnlyDictionary<int, int>? TemperaturesToReturn
    {
        get => ReadData<IReadOnlyDictionary<int, int>>(nameof(TemperaturesToReturn));
        set => WriteData(nameof(TemperaturesToReturn), value);
    }

    public static bool ThrowOnGetTemperatures
    {
        get => ReadData<bool>(nameof(ThrowOnGetTemperatures));
        set => WriteData(nameof(ThrowOnGetTemperatures), value);
    }

    public static new void Reset()
    {
        DummyViewsExtension.Reset();
        DemandHistory = new List<int>();
        SetDemandCallCount = 0;
        TemperaturesToReturn = null;
        ThrowOnGetTemperatures = false;
    }

    public void SetDemand(ExtensionDemand demand)
    {
        SetDemandCallCount++;
        var list = DemandHistory;
        list.Add((int)demand);
        DemandHistory = list;
    }

    public IReadOnlyDictionary<int, int> GetDiskTemperatures()
    {
        if (ThrowOnGetTemperatures)
        {
            throw new InvalidOperationException("Hostile disk temperature source threw an error");
        }

        return TemperaturesToReturn ?? new Dictionary<int, int>();
    }
}
