using System;

namespace SysMonitor.Linux.Presentation
{
    public enum MetricKind
    {
        Cpu,
        Ram,
        Disk,
        Network
    }

    public sealed record MetricItem(MetricKind Kind, string Key, string Title, string Subtitle, int Usage, string ValueText, string ColorHex, string AccentHex);
}
