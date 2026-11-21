using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using SysMonitor.Linux.Presentation;
using Path = Avalonia.Controls.Shapes.Path;

namespace SysMonitor.Linux.App;

/// <summary>
/// Small builders shared by the three views. The visual language (colours,
/// sizes, radii) lives in the spec; this file only turns those values into
/// Avalonia objects.
/// </summary>
internal static class Ui
{
    /// <summary>
    /// The Linux stand-in for Segoe UI + Leelawadee UI (spec section 1).
    /// <para>
    /// IBM Plex Sans Thai draws both the Latin and the Thai glyphs, so a mixed
    /// line no longer switches between two fonts with different x-heights and
    /// line metrics. It is embedded in this assembly, so the primary family does
    /// not depend on what a user has installed; the system families after it are
    /// fallbacks for the glyphs Plex lacks (for example "▲").
    /// </para>
    /// </summary>
    public const string FontStack =
        "avares://sysmonitor/Assets/Fonts#IBM Plex Sans Thai, Noto Sans, Noto Sans Thai, sans-serif";

    /// <summary>The embedded folder the first family in <see cref="FontStack"/> comes from.</summary>
    public const string FontFolder = "avares://sysmonitor/Assets/Fonts";

    /// <summary>
    /// The line box every text block uses, as a multiple of its font size
    /// (spec section 1). Plex Thai's natural line box is 1.65 em and Noto Sans'
    /// was 1.36 em, so without this the sidebar rows and the fixed-height
    /// Widget grow by a third. The explicit value keeps the old vertical
    /// rhythm; <see cref="Text"/> turns the block's clip off, because at 9 and
    /// 10 px the tallest Thai marks reach slightly above this shorter box and
    /// must still be drawn in full.
    /// </summary>
    public const double LineHeightEm = 1.36;

    public static readonly FontFamily Family = new(FontStack);

    /// <summary>
    /// Every embedded face and the OpenType weight class it must serve, so the
    /// two weights the spec uses (SemiBold and Bold) are real files rather than
    /// a synthesised slant. <c>--probe-ui</c> opens each one and compares the
    /// weight the file reports with this list. The numbers are the weights
    /// <see cref="FontWeight.Normal"/>, <see cref="FontWeight.SemiBold"/> and
    /// <see cref="FontWeight.Bold"/> resolve to.
    /// </summary>
    public static readonly (string File, int Weight)[] Faces =
    {
        ("IBMPlexSansThai-Regular.ttf", 400),
        ("IBMPlexSansThai-SemiBold.ttf", 600),
        ("IBMPlexSansThai-Bold.ttf", 700),
    };

    public static Color Color(Colour colour) =>
        Avalonia.Media.Color.FromArgb(colour.A, colour.R, colour.G, colour.B);

    public static SolidColorBrush Brush(Colour colour) => new(Color(colour));

    public static TextBlock Text(
        string text,
        double size,
        Colour colour,
        FontWeight? weight = null,
        bool wrap = false,
        bool ellipsis = false,
        TextAlignment align = TextAlignment.Left)
    {
        var block = new TextBlock
        {
            Text = text,
            FontFamily = Family,
            FontSize = size,
            // Amendment 1: 1.36 x the size, the metrics Noto Sans had, instead
            // of Plex Thai's natural 1.65 em. TextBlock clips to its own bounds
            // by default, and Avalonia puts the baseline at the ascent when the
            // line is this short, so the tallest Thai marks would lose their
            // top pixels. Turning the clip off keeps the glyphs whole while the
            // row keeps the compact 1.36 em height.
            LineHeight = Math.Round(size * LineHeightEm, 2),
            ClipToBounds = false,
            Foreground = Brush(colour),
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = ellipsis ? TextTrimming.CharacterEllipsis : TextTrimming.None,
            TextAlignment = align,
        };
        if (weight is { } value)
        {
            block.FontWeight = value;
        }
        if (ellipsis)
        {
            block.TextTrimming = TextTrimming.CharacterEllipsis;
        }
        return block;
    }

    /// <summary>Thai needs a break opportunity before a leading vowel (spec 4).</summary>
    public static string Thai(string text) => ThaiWrap.Soften(text);

    public static void Tip(Control control, string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            ToolTip.SetTip(control, text);
        }
    }

    /// <summary>
    /// Run a model change after the current input event finishes. A settings
    /// change rebuilds the sidebar, and rebuilding a container from inside its
    /// own click handler is a needless hazard.
    /// </summary>
    public static void Act(Action action) => Dispatcher.UIThread.Post(action);

    // ------------------------------------------------------------- icons

    public static Control Icon(IconName name, double size, Colour colour)
    {
        (string data, bool filled) = GeometryFor(name);
        var path = new Path
        {
            Data = Avalonia.Media.Geometry.Parse(data),
            Stretch = Stretch.Uniform,
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            StrokeThickness = 1.6,
            StrokeJoin = PenLineJoin.Round,
            StrokeLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        };
        if (filled)
        {
            path.Fill = Brush(colour);
        }
        else
        {
            path.Stroke = Brush(colour);
        }
        return path;
    }

    /// <summary>Simple line icons on a 24x24 grid, standing in for Segoe MDL2.</summary>
    private static (string Data, bool Filled) GeometryFor(IconName name) => name switch
    {
        IconName.Close => ("M6,6 L18,18 M18,6 L6,18", false),
        IconName.Pause => ("M8,5 L8,19 M16,5 L16,19", false),
        IconName.Play => ("M8,5 L19,12 L8,19 Z", true),
        IconName.ChevronLeft => ("M15,4 L8,12 L15,20", false),
        IconName.ChevronRight => ("M9,4 L16,12 L9,20", false),
        IconName.ChevronDown => ("M4,9 L12,17 L20,9", false),
        IconName.FullScreen => ("M4,9 L4,4 L9,4 M15,4 L20,4 L20,9 M20,15 L20,20 L15,20 M9,20 L4,20 L4,15", false),
        IconName.Widget => ("M4,5 H20 V19 H4 Z M4,9 H20", false),
        IconName.Home => ("M3,12 L12,4 L21,12 M6,11 V20 H18 V11", false),
        IconName.FullData => ("M4,6 H20 M4,12 H14 M4,18 H20", false),
        IconName.Settings => ("M12,9 A3,3 0 1 0 12,15 A3,3 0 1 0 12,9 Z M12,3 V6 M12,18 V21 M3,12 H6 M18,12 H21 M5.6,5.6 L7.7,7.7 M16.3,16.3 L18.4,18.4 M18.4,5.6 L16.3,7.7 M7.7,16.3 L5.6,18.4", false),
        IconName.Manual => ("M5,4 H11 V20 H5 Z M11,4 H19 V20 H11 Z M8,8 H9 M14,8 H17 M14,11 H17", false),
        IconName.About => ("M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 Z M12,11 V17 M12,7.5 V8", false),
        IconName.Dismiss => ("M7,7 L17,17 M17,7 L7,17", false),
        IconName.Delete => ("M5,7 H19 M9,7 V4 H15 V7 M7,7 L8,20 H16 L17,7", false),
        IconName.Lock => ("M7,11 H17 V20 H7 Z M9,11 V8 A3,3 0 0 1 15,8 V11", false),
        IconName.Restart => ("M20,12 A8,8 0 1 1 12,4 M12,4 L8,1 M12,4 L8,7", false),
        IconName.Stop => ("M6,6 H18 V18 H6 Z", true),
        IconName.Link => ("M10,14 L14,10 M8,12 L5,15 A3.5,3.5 0 0 0 10,20 L13,17 M16,12 L19,9 A3.5,3.5 0 0 0 14,4 L11,7", false),
        IconName.Power => ("M13,2 L5,14 H11 L10,22 L19,10 H13 Z", true),
        _ => ("", false),
    };

    // ----------------------------------------------------------- buttons

    /// <summary>The header icon button: 26x26, transparent until hovered.</summary>
    public static Border IconButton(
        IconName icon,
        double box,
        double glyph,
        Colour colour,
        Action onClick,
        string tooltip = "",
        bool small = false)
    {
        var button = new Border
        {
            Width = box,
            Height = box,
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = Icon(icon, glyph, colour),
        };
        button.Classes.Add("nodrag");
        Tip(button, tooltip);
        button.PointerEntered += (_, _) => button.Background = Brush(Palette.Hover);
        button.PointerExited += (_, _) => button.Background = Brushes.Transparent;
        button.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
            {
                return;
            }
            button.Background = Brush(Palette.Pressed);
            e.Handled = true;
            onClick();
        };
        return button;
    }

    /// <summary>
    /// The Widget's close and pause buttons: 18x18, dimmed at rest, fully
    /// visible while the pointer is over the window (spec section 3).
    /// </summary>
    public static Border WindowIcon(
        IconName icon, double glyph, Colour colour, Action onClick, string tooltip)
    {
        var button = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = Icon(icon, glyph, colour),
        };
        button.Classes.Add("nodrag");
        Tip(button, tooltip);
        button.PointerEntered += (_, _) => button.Background = Brush(Palette.Hover);
        button.PointerExited += (_, _) => button.Background = Brushes.Transparent;
        button.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
            {
                return;
            }
            e.Handled = true;
            onClick();
        };
        return button;
    }

    /// <summary>The Widget's prev/next: 20x20 over the gutter, chip-coloured.</summary>
    public static Border StepButton(
        IconName icon, Palette palette, double glyph, Action onClick, string tooltip)
    {
        var button = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(6),
            Background = Brush(palette.Chip),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = Icon(icon, glyph, palette.Muted),
        };
        button.Classes.Add("nodrag");
        Tip(button, tooltip);
        button.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
            {
                return;
            }
            e.Handled = true;
            onClick();
        };
        return button;
    }

    /// <summary>The sidebar's blue outline button (spec section 4).</summary>
    public static Border SidebarButton(
        string label, Palette palette, double fontSize, Action onClick, bool enabled = true)
    {
        Colour blue = Colour.FromHex("#3b82f6");
        var text = Ui.Text(Thai(label), fontSize, blue, wrap: true);
        var button = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = Brush(Colour.FromHex("#553b82f6")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = text,
        };
        button.Classes.Add("nodrag");
        if (!enabled)
        {
            button.Opacity = 0.5;
            button.Cursor = new Cursor(StandardCursorType.Arrow);
            return button;
        }
        button.PointerEntered += (_, _) => button.Background = Brush(Colour.FromHex("#223b82f6"));
        button.PointerExited += (_, _) => button.Background = Brushes.Transparent;
        button.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
            {
                return;
            }
            e.Handled = true;
            onClick();
        };
        return button;
    }

    /// <summary>A nav row: glyph and label, left aligned and wrapping.</summary>
    public static Border NavRow(
        IconName icon, string label, Palette palette, TypeRamp ramp, Action onClick, string tooltip = "")
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
            },
        };
        if (icon != IconName.None)
        {
            Control glyph = Icon(icon, ramp.NavIcon, palette.Label);
            glyph.Margin = new Thickness(0, 0, 8, 0);
            grid.Children.Add(glyph);
        }
        var text = Text(Thai(label), ramp.Nav, palette.Text, wrap: true);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var button = new Border
        {
            Padding = new Thickness(4, 5, 4, 5),
            CornerRadius = new CornerRadius(7),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = grid,
        };
        button.Classes.Add("nodrag");
        Tip(button, tooltip);
        button.PointerEntered += (_, _) => button.Background = Brush(Colour.FromHex("#22808080"));
        button.PointerExited += (_, _) => button.Background = Brushes.Transparent;
        button.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
            {
                return;
            }
            e.Handled = true;
            onClick();
        };
        return button;
    }

    /// <summary>The 10 px usage bar: empty track, rounded fill at a star ratio.</summary>
    public static Control Bar(int percent, Colour fill, Colour empty)
    {
        int clamped = Math.Clamp(percent, 0, 100);
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(clamped, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(100 - clamped, GridUnitType.Star)),
            },
        };
        var filled = new Border
        {
            Background = Brush(fill),
            CornerRadius = new CornerRadius(5),
        };
        grid.Children.Add(filled);
        return new Border
        {
            Height = 10,
            CornerRadius = new CornerRadius(5),
            Background = Brush(empty),
            ClipToBounds = true,
            Child = grid,
        };
    }

    /// <summary>A heading: uppercase, label colour, semi-bold.</summary>
    public static TextBlock Heading(string text, Palette palette, TypeRamp ramp) =>
        Head(Text(Thai(text).ToUpperInvariant(), ramp.Label, palette.Label, FontWeight.SemiBold, wrap: true));

    private static TextBlock Head(TextBlock block)
    {
        block.Margin = new Thickness(0, 10, 0, 6);
        block.VerticalAlignment = VerticalAlignment.Top;
        return block;
    }

    /// <summary>A paragraph: body size, muted, wrapping, small bottom margin.</summary>
    public static TextBlock Paragraph(string text, Palette palette, TypeRamp ramp, bool muted = true)
    {
        var block = Text(
            Thai(text),
            ramp.Body,
            muted ? palette.Muted : palette.Text,
            wrap: true);
        block.Margin = new Thickness(0, 0, 0, 4);
        block.VerticalAlignment = VerticalAlignment.Top;
        return block;
    }

    public static Control Separator(Palette palette) => new Border
    {
        Height = 1,
        Background = Brush(palette.Border),
        Margin = new Thickness(0, 6, 0, 6),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    // ------------------------------------------------------------ badges

    /// <summary>
    /// The thermometer glyph temperature badges and the legend share (spec
    /// section 4). It is a path, not a font glyph, so it renders identically
    /// without Segoe MDL2.
    /// </summary>
    public static Shape Thermometer(double width, double height, Colour colour)
    {
        return new Path
        {
            Data = Avalonia.Media.Geometry.Parse(
                "M 3.1,0.6 A 1.4,1.4 0 0 1 5.9,0.6 L 5.9,6.2 A 2.6,2.6 0 1 1 3.1,6.2 Z"),
            Width = width,
            Height = height,
            Stretch = Stretch.Uniform,
            Fill = Brush(colour),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>
    /// One badge pill. Temperature badges carry the thermometer glyph and their
    /// reading; power badges carry the lightning icon and no text, so the
    /// charging marker never grows a thermometer (R-030 fix 3).
    /// </summary>
    public static Control Badge(TempBadge badge, TypeRamp ramp) =>
        badge.Kind == BadgeKind.Power
            ? PowerBadge(badge)
            : TemperatureBadge(badge, ramp);

    /// <summary>
    /// Fill a badge slot (an empty border) and keep it in step with the model.
    /// A data tick hands the views a fresh model four times a second, so the
    /// pill is rebuilt only when the badge itself changes — the battery's
    /// power icon then follows the AC state without rebuilding the row it
    /// sits in (R-030 fix 4).
    /// </summary>
    public static void FillBadge(Border slot, TempBadge? badge, TypeRamp ramp)
    {
        // An empty slot must take no space at all: a StackPanel still applies
        // its spacing to a visible child of zero width.
        slot.IsVisible = badge is not null;
        TempBadge? current = slot.Tag is TempBadge tag ? tag : null;
        if (Nullable.Equals(current, badge))
        {
            return;
        }
        slot.Tag = badge;
        slot.Child = badge is { } value ? Badge(value, ramp) : null;
    }

    /// <summary>The temperature badge, with the spec's pulse when it is hot.</summary>
    private static Control TemperatureBadge(TempBadge badge, TypeRamp ramp)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Shape thermometer = Thermometer(9, 11, badge.Fore);
        thermometer.Margin = new Thickness(0, 0, 4, 0);
        row.Children.Add(thermometer);
        row.Children.Add(Text(badge.Text, 10, badge.Fore, FontWeight.SemiBold));

        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(5, 1, 6, 2),
            Background = Brush(badge.Back),
            Child = row,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (badge.Hot)
        {
            var animation = new Animation
            {
                Duration = TimeSpan.FromSeconds(0.6),
                IterationCount = IterationCount.Infinite,
                PlaybackDirection = PlaybackDirection.Alternate,
                Easing = new LinearEasing(),
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0),
                        Setters = { new Setter(Visual.OpacityProperty, 1.0) },
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1),
                        Setters = { new Setter(Visual.OpacityProperty, 0.35) },
                    },
                },
            };
            var style = new Style(selector => selector.OfType<Border>().Class("hot"));
            style.Animations.Add(animation);
            border.Classes.Add("hot");
            border.Styles.Add(style);
        }

        return border;
    }

    /// <summary>
    /// The power badge: the pill's fill and the lightning glyph in the badge
    /// foreground, with no text at all. Slightly tighter padding than the
    /// temperature pill because there is no label beside the icon.
    /// </summary>
    private static Control PowerBadge(TempBadge badge)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4, 2, 4, 3),
            Background = Brush(badge.Back),
            Child = Icon(IconName.Power, 10, badge.Fore),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }
}
