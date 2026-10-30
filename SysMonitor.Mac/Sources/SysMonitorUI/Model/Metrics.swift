import CoreGraphics

/// Sizes for the current window and font settings.
///
/// The Windows build scaled a whole view with a layout transform. Here every
/// size is multiplied out instead: a scale transform would draw the text at
/// its base size and stretch the pixels, and the type would go soft exactly
/// when someone made the window bigger to read it.
public struct Metrics: Equatable, Sendable {
    /// The type ramp, before any scaling. The Windows build's values.
    public enum Font {
        public static let tiny: CGFloat = 9
        public static let small: CGFloat = 10
        public static let label: CGFloat = 10
        public static let body: CGFloat = 11
        public static let value: CGFloat = 14
        public static let heading: CGFloat = 15
        public static let nav: CGFloat = 12
    }

    /// The window's size over its design size.
    public var window: CGFloat
    /// The user's font size setting, 0.8 to 1.6.
    public var font: CGFloat
    /// The view's own type size against the ramp, before the user's setting:
    /// the Overall view reads smaller than Settings (R-031).
    public var text: CGFloat

    public init(window: CGFloat = 1, font: CGFloat = 1, text: CGFloat = 1) {
        self.window = window.isFinite && window > 0 ? window : 1
        self.font = min(max(font.isFinite ? font : 1, 0.8), 1.6)
        self.text = text.isFinite && text > 0 ? text : 1
    }

    /// The Overall view's type against the ramp (R-031).
    public static let overallText: CGFloat = 0.8

    /// A text size: the view's, the user's setting and the window all apply.
    public func type(_ base: CGFloat) -> CGFloat { base * text * font * window }

    /// Any other length: padding, bar height, corner radius.
    public func len(_ base: CGFloat) -> CGFloat { base * window }

    public static func widgetScale(_ size: CGSize) -> CGFloat {
        min(size.width / AppConfig.widgetSize.width, size.height / AppConfig.widgetSize.height)
    }

    public static func overallScale(_ size: CGSize) -> CGFloat {
        min(size.width / AppConfig.overallSize.width, size.height / AppConfig.overallSize.height)
    }
}
