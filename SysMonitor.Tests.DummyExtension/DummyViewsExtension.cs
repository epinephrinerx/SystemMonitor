using SysMonitor.Extensibility;

namespace SysMonitor.Tests.DummyExtension;

/// <summary>
/// The extension is loaded into its own AssemblyLoadContext, so the copy of this class the host
/// runs is a different type from the one the tests reference: ordinary statics are not shared.
/// The hooks therefore live in AppDomain data, which both copies see; only types from the
/// contract assembly or the BCL cross the boundary.
/// </summary>
public class DummyViewsExtension : IExtension, IViewProvider
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

    public static void Reset()
    {
        Initialized = false;
        ShutdownCalled = false;
        DisplayNameFunc = null;
        ViewFunc = null;
        InitializeAction = null;
    }

    public void Initialize(IExtensionContext context)
    {
        Initialized = true;
        InitializeAction?.Invoke();
    }

    public void Shutdown()
    {
        ShutdownCalled = true;
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
