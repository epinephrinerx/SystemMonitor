namespace SysMonitor.Linux.Presentation
{
    public static class Theme
    {
        public const string DefaultName = "light";

        public static ThemeColors Light { get; } = new ThemeColors("#ffffff", "#f3f5f9", "#eef1f6", "#1e293b", "#64748b", "#d5dae1", "#dfe4ec");
        public static ThemeColors Dark { get; } = new ThemeColors("#0f172a", "#0b1220", "#1a2438", "#ffffff", "#cbd5e1", "#2b374b", "#070c17");

        public static ThemeColors Resolve(string? name)
        {
            if (string.Equals(name?.Trim(), "dark", StringComparison.OrdinalIgnoreCase))
            {
                return Dark;
            }
            else
            {
                return Light;
            }
        }
    }

    public record ThemeColors(string Panel, string Sidebar, string Control, string Text, string Muted, string Border, string BarEmpty);
}
