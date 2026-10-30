import SwiftUI

/// A colour as data, so tests can compare it and views can draw it.
public struct Rgb: Equatable, Sendable {
    public var r: UInt8
    public var g: UInt8
    public var b: UInt8

    public init(_ hex: UInt32) {
        r = UInt8((hex >> 16) & 0xff)
        g = UInt8((hex >> 8) & 0xff)
        b = UInt8(hex & 0xff)
    }

    public var color: Color {
        Color(.sRGB, red: Double(r) / 255, green: Double(g) / 255, blue: Double(b) / 255, opacity: 1)
    }
}

/// The panel colours for one theme. The same values as the Windows build's
/// `Palette.cs`, so the two editions look like one product.
public struct Theme: Equatable, Sendable {
    public var panel: Rgb
    public var sidebar: Rgb
    public var control: Rgb
    public var chip: Rgb
    public var barEmpty: Rgb
    public var text: Rgb
    public var muted: Rgb
    public var label: Rgb
    public var border: Rgb
    public var tag: Rgb
    public var badgeOk: Rgb
    public var badgeWarm: Rgb
    public var badgeHot: Rgb

    public static let light = Theme(
        panel: Rgb(0xffffff), sidebar: Rgb(0xf3f5f9), control: Rgb(0xeef1f6), chip: Rgb(0xe6eaf1),
        barEmpty: Rgb(0xdfe4ec), text: Rgb(0x1e293b), muted: Rgb(0x64748b), label: Rgb(0x475569),
        border: Rgb(0xd5dae1), tag: Rgb(0xe8ecf1),
        badgeOk: Rgb(0xdcfce7), badgeWarm: Rgb(0xfef3c7), badgeHot: Rgb(0xfee2e2))

    public static let dark = Theme(
        panel: Rgb(0x0f172a), sidebar: Rgb(0x0b1220), control: Rgb(0x1a2438), chip: Rgb(0x1e293b),
        barEmpty: Rgb(0x070c17), text: Rgb(0xffffff), muted: Rgb(0xcbd5e1), label: Rgb(0x94a3b8),
        border: Rgb(0x2b374b), tag: Rgb(0x243044),
        badgeOk: Rgb(0x16351f), badgeWarm: Rgb(0x3a3212), badgeHot: Rgb(0x3d1717))

    public static func named(dark: Bool) -> Theme { dark ? .dark : .light }
}

public enum Accent: Sendable {
    case cpu, ram, disk, gpu, net

    public var rgb: Rgb {
        switch self {
        case .cpu: return Rgb(0x3b82f6)
        case .ram: return Rgb(0xa855f7)
        case .disk: return Rgb(0x10b981)
        // The Windows build's Overall view draws GPU bars in the CPU blue:
        // its own amber is too close to the traffic light's warning colour.
        case .gpu: return Rgb(0x3b82f6)
        case .net: return Rgb(0x06b6d4)
        }
    }
}

/// Two traffic lights that must not be confused (see CLAUDE.md upstream):
/// the thermometer badge turns at 65/80 °C, the usage bars at 70/90 %.
public enum Palette {
    public static let warmAt = 65
    public static let hotAt = 80
    public static let loadWarmAt = 70
    public static let loadHotAt = 90

    public static let ok = Rgb(0x22c55e)
    public static let warm = Rgb(0xeab308)
    public static let hot = Rgb(0xef4444)

    /// A meter keeps its device colour until it is busy, then amber, then red.
    public static func loadColor(_ percent: Int, _ accent: Accent) -> Rgb {
        percent >= loadHotAt ? hot : percent >= loadWarmAt ? warm : accent.rgb
    }

    /// Nil when there is no reading: the badge then shows `n/a` in label
    /// colours rather than a traffic light nobody measured.
    public static func tempColor(_ celsius: Int?) -> Rgb? {
        guard let celsius else { return nil }
        return celsius >= hotAt ? hot : celsius >= warmAt ? warm : ok
    }

    public static func tempBadge(_ celsius: Int?, _ theme: Theme) -> Rgb {
        guard let celsius else { return theme.tag }
        return celsius >= hotAt ? theme.badgeHot : celsius >= warmAt ? theme.badgeWarm : theme.badgeOk
    }
}
