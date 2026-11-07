namespace SysMonitor.Linux.Presentation
{
    public enum ViewMode
    {
        Mini,
        Expanded,
        Full
    }

    public static class ViewModes
    {
        public static ViewMode Next(ViewMode m)
        {
            return m switch
            {
                ViewMode.Mini => ViewMode.Expanded,
                ViewMode.Expanded => ViewMode.Full,
                ViewMode.Full => ViewMode.Mini,
                _ => ViewMode.Mini
            };
        }

        public static ViewMode Effective(ViewMode requested, double width, double height)
        {
            if (requested == ViewMode.Mini)
                return ViewMode.Mini;

            if (requested == ViewMode.Expanded)
                return ViewMode.Expanded;

            if (requested == ViewMode.Full)
            {
                bool ok = !double.IsNaN(width) && !double.IsNaN(height) && width >= 640 && height >= 480;
                return ok ? ViewMode.Full : ViewMode.Expanded;
            }

            return ViewMode.Expanded;
        }
    }
}
