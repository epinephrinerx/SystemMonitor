using SysMonitor.Extensibility;

namespace SysMonitor.Tests.DummyExtension;

/// <summary>
/// The extension is loaded into its own AssemblyLoadContext, so the copy of this class the host
/// runs is a different type from the one the tests reference: ordinary statics are not shared.
/// The hooks therefore live in AppDomain data, which both copies see; only types from the
/// contract assembly or the BCL cross the boundary.
/// </summary>
public class DummyViewsExtension : IExtension, IViewProvider, IDemandAware, IDiskTemperatureSource
{
    private static T? Read<T>(string name) => AppDomain.CurrentDomain.GetData("DummyViews." + name) is T value ? value : default;

    private static void Write(string name, object? value) => AppDomain.CurrentDomain.SetData("DummyViews." + name, value);

    public static bool Initialized
    {
        get => Read<bool>(nameof(Initialized));
        set => Write(nameof(Initialized), value);
    }

    public static bool ShutdownCalled
    {
        get => Read<bool>(nameof(ShutdownCalled));
        set => Write(nameof(ShutdownCalled), value);
    }

    public static ExtensionDemand? LastDemand
    {
        get => Read<ExtensionDemand?>(nameof(LastDemand));
        set => Write(nameof(LastDemand), value);
    }

    public static int SetDemandCount
    {
        get => Read<int>(nameof(SetDemandCount));
        set => Write(nameof(SetDemandCount), value);
    }

    public static Action<ExtensionDemand>? SetDemandAction
    {
        get => Read<Action<ExtensionDemand>>(nameof(SetDemandAction));
        set => Write(nameof(SetDemandAction), value);
    }

    public static Func<IReadOnlyDictionary<int, int>>? DiskTemperaturesFunc
    {
        get => Read<Func<IReadOnlyDictionary<int, int>>>(nameof(DiskTemperaturesFunc));
        set => Write(nameof(DiskTemperaturesFunc), value);
    }

    public static Func<string, string>? DisplayNameFunc
    {
        get => Read<Func<string, string>>(nameof(DisplayNameFunc));
        set => Write(nameof(DisplayNameFunc), value);
    }

    public static Func<string, ExtensionView>? ViewFunc
    {
        get => Read<Func<string, ExtensionView>>(nameof(ViewFunc));
        set => Write(nameof(ViewFunc), value);
    }

    public static Action? InitializeAction
    {
        get => Read<Action>(nameof(InitializeAction));
        set => Write(nameof(InitializeAction), value);
    }

    public static int InitializeCount
    {
        get => Read<int>(nameof(InitializeCount));
        set => Write(nameof(InitializeCount), value);
    }

    public static bool DemandDeliveredAfterShutdown
    {
        get => Read<bool>(nameof(DemandDeliveredAfterShutdown));
        set => Write(nameof(DemandDeliveredAfterShutdown), value);
    }

    public static void Reset()
    {
        Initialized = false;
        ShutdownCalled = false;
        InitializeCount = 0;
        SetDemandCount = 0;
        LastDemand = null;
        SetDemandAction = null;
        DiskTemperaturesFunc = null;
        DisplayNameFunc = null;
        ViewFunc = null;
        InitializeAction = null;
        DemandDeliveredAfterShutdown = false;
    }

    private bool _instanceShutdown;

    public void Initialize(IExtensionContext context)
    {
        Initialized = true;
        InitializeCount++;
        InitializeAction?.Invoke();
    }

    public void Shutdown()
    {
        _instanceShutdown = true;
        ShutdownCalled = true;
    }

    public void SetDemand(ExtensionDemand demand)
    {
        if (_instanceShutdown)
        {
            DemandDeliveredAfterShutdown = true;
        }
        LastDemand = demand;
        SetDemandCount++;
        SetDemandAction?.Invoke(demand);
    }

    public IReadOnlyDictionary<int, int> GetDiskTemperatures()
    {
        Func<IReadOnlyDictionary<int, int>>? custom = DiskTemperaturesFunc;
        if (custom != null)
        {
            return custom();
        }
        return new Dictionary<int, int>();
    }

    public string GetDisplayName(string language)
    {
        Func<string, string>? custom = DisplayNameFunc;
        if (custom != null)
        {
            return custom(language);
        }

        return language == "th" ? "ส่วนเสริมจำลอง" : "Dummy Extension";
    }

    public ExtensionView GetView(string language)
    {
        Func<string, ExtensionView>? custom = ViewFunc;
        if (custom != null)
        {
            return custom(language);
        }

        return new ExtensionView(
            new[]
            {
                new WidgetPage("DUMMY", "OK", "Sample detail", 35, 42, Severity.Good),
            },
            new OverviewSection(
                "Note",
                new[]
                {
                    new OverviewRow("Row 1", "Detail 1", "Active", 80, Severity.Good, 40),
                }),
            new[]
            {
                new DetailTab(
                    "tab1", "Tab 1", "Summary 1", "Detail 1", "Hardware 1",
                    new[]
                    {
                        new Fact("Status", "Good"),
                        new Fact("Version", "1.0.0"),
                    }),
            });
    }
}

public sealed class ThrowingEnumeratorDictionary : IReadOnlyDictionary<int, int>
{
    public IEnumerator<KeyValuePair<int, int>> GetEnumerator() => throw new InvalidOperationException("Throwing enumerator");
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    public int Count => 1;
    public bool ContainsKey(int key) => true;
    public bool TryGetValue(int key, out int value) { value = 0; return false; }
    public int this[int key] => 0;
    public IEnumerable<int> Keys => new[] { 0 };
    public IEnumerable<int> Values => new[] { 0 };
}

public sealed class EndlessInvalidDictionary : IReadOnlyDictionary<int, int>
{
    public IEnumerator<KeyValuePair<int, int>> GetEnumerator()
    {
        while (true)
        {
            yield return new KeyValuePair<int, int>(-1, 999);
        }
    }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    public int Count => int.MaxValue;
    public bool ContainsKey(int key) => false;
    public bool TryGetValue(int key, out int value) { value = 0; return false; }
    public int this[int key] => throw new KeyNotFoundException();
    public IEnumerable<int> Keys => Array.Empty<int>();
    public IEnumerable<int> Values => Array.Empty<int>();
}

