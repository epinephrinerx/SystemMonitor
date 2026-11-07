using System;

namespace SysMonitor.Linux.Presentation
{
    public enum TempLevel
    {
        None,
        Ok,
        Warm,
        Hot
    }

    public static class TempLevels
    {
        public static TempLevel Of(int? celsius)
        {
            if (!celsius.HasValue)
            {
                return TempLevel.None;
            }

            int temp = celsius.Value;

            if (temp < 65)
            {
                return TempLevel.Ok;
            }
            else if (temp < 80)
            {
                return TempLevel.Warm;
            }
            else
            {
                return TempLevel.Hot;
            }
        }
    }
}
