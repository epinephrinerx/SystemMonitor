using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace SysMonitor;

/// <summary>
/// What a mouse event actually hit, as far as the window's own drag and resize
/// are concerned.
/// </summary>
internal static class HitSource
{
    /// <summary>
    /// Is the pointer on something that wants the click itself? Walks up from
    /// whatever was hit, because the source is usually a piece of a control's
    /// template rather than the control.
    ///
    /// The source is not always a visual. Over text made of inline pieces
    /// (a <see cref="System.Windows.Documents.Run"/> in a TextBlock) it is the
    /// piece itself, which has no visual parent to ask: asking threw, and an
    /// exception out of a mouse-move handler ends the program. Those climb the
    /// content tree instead.
    /// </summary>
    public static bool IsControl(object? source)
    {
        for (DependencyObject? node = source as DependencyObject;
             node is not null;
             node = ParentOf(node))
        {
            if (node is ButtonBase or Slider or ScrollBar or Thumb)
            {
                return true;
            }
        }
        return false;
    }

    private static DependencyObject? ParentOf(DependencyObject node) => node switch
    {
        // A visual that is not in a rendered tree yet has no visual parent but
        // does have a logical one.
        Visual or Visual3D => VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node),
        ContentElement content => ContentOperations.GetParent(content)
                                  ?? LogicalTreeHelper.GetParent(content),
        _ => LogicalTreeHelper.GetParent(node),
    };
}
