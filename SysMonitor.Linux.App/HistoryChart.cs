using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering;
using Avalonia.Visuals;
using SysMonitor.Model;
using System;

namespace SysMonitor.Linux.App
{
    public sealed class HistoryChart : Control
    {
        public static readonly StyledProperty<History?> SeriesProperty =
            AvaloniaProperty.Register<HistoryChart, History?>(
                nameof(Series),
                defaultBindingMode: Avalonia.Data.BindingMode.OneWay);

        public static readonly StyledProperty<string> LineColorProperty =
            AvaloniaProperty.Register<HistoryChart, string>(
                nameof(LineColor),
                "#3b82f6",
                defaultBindingMode: Avalonia.Data.BindingMode.OneWay);

        public static readonly StyledProperty<string> BackgroundColorProperty =
            AvaloniaProperty.Register<HistoryChart, string>(
                nameof(BackgroundColor),
                "#f3f5f9",
                defaultBindingMode: Avalonia.Data.BindingMode.OneWay);

        public static readonly StyledProperty<double?> FixedMaxProperty =
            AvaloniaProperty.Register<HistoryChart, double?>(
                nameof(FixedMax),
                defaultBindingMode: Avalonia.Data.BindingMode.OneWay);

        public static readonly StyledProperty<int> RevisionProperty =
            AvaloniaProperty.Register<HistoryChart, int>(
                nameof(Revision),
                defaultBindingMode: Avalonia.Data.BindingMode.OneWay);

        public static readonly StyledProperty<string> GridColorProperty =
            AvaloniaProperty.Register<HistoryChart, string>(
                nameof(GridColor),
                "#c8cdd6",
                defaultBindingMode: Avalonia.Data.BindingMode.OneWay);

        static HistoryChart()
        {
            AffectsRender<HistoryChart>(SeriesProperty, LineColorProperty, BackgroundColorProperty, FixedMaxProperty, RevisionProperty, GridColorProperty);
        }

        public History? Series
        {
            get => GetValue(SeriesProperty);
            set => SetValue(SeriesProperty, value);
        }

        public string LineColor
        {
            get => GetValue(LineColorProperty);
            set => SetValue(LineColorProperty, value);
        }

        public string BackgroundColor
        {
            get => GetValue(BackgroundColorProperty);
            set => SetValue(BackgroundColorProperty, value);
        }

        public double? FixedMax
        {
            get => GetValue(FixedMaxProperty);
            set => SetValue(FixedMaxProperty, value);
        }

        public int Revision
        {
            get => GetValue(RevisionProperty);
            set => SetValue(RevisionProperty, value);
        }

        public string GridColor
        {
            get => GetValue(GridColorProperty);
            set => SetValue(GridColorProperty, value);
        }

        public override void Render(DrawingContext context)
        {
            try
            {
                double w = Bounds.Width, h = Bounds.Height;
                if (w <= 0 || h <= 0) return;

                context.FillRectangle(HexBrush.Parse(BackgroundColor), new Rect(0, 0, w, h));

                var gridPen = new Pen(HexBrush.Parse(GridColor), 1);
                for (int k = 1; k <= 4; k++)
                {
                    double y = h * k / 5.0;
                    context.DrawLine(gridPen, new Point(0, y), new Point(w, y));
                }

                var s = Series;
                if (s == null || s.Count < 2) return;

                var buf = new double[s.Count];
                int n = s.CopyTo(buf);
                if (n < 2) return;

                for (int i = 0; i < n; i++)
                {
                    if (!double.IsFinite(buf[i])) buf[i] = 0;
                }

                double? fixedMax = FixedMax;
                double max = (fixedMax.HasValue && double.IsFinite(fixedMax.Value) && fixedMax.Value > 0) ? fixedMax.Value : s.Max;
                if (!double.IsFinite(max) || max < 1) max = 1;

                using (context.PushClip(new Rect(0, 0, w, h)))
                {
                    var geo = new StreamGeometry();
                    using (var ctx = geo.Open())
                    {
                        ctx.BeginFigure(new Point(0, h - (Math.Clamp(buf[0], 0, max) / max) * h), false);
                        for (int i = 1; i < n; i++)
                        {
                            double x = w * i / (n - 1);
                            double v = Math.Clamp(buf[i], 0, max);
                            double y = h - (v / max) * h;
                            ctx.LineTo(new Point(x, y));
                        }
                        ctx.EndFigure(false);
                    }

                    context.DrawGeometry(null, new Pen(HexBrush.Parse(LineColor), 2), geo);
                }
            }
            catch (Exception)
            {
                // Never throw
            }
        }
    }
}
