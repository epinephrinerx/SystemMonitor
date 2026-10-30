import AppKit
import SwiftUI

/// A horizontal meter: a rounded track and a fill in proportion.
struct MeterBar: View {
    var percent: Int
    var color: Color
    var track: Color
    var height: CGFloat

    var body: some View {
        GeometryReader { geo in
            ZStack(alignment: .leading) {
                Capsule().fill(track)
                Capsule().fill(color)
                    .frame(width: geo.size.width * CGFloat(min(max(percent, 0), 100)) / 100)
            }
        }
        .frame(height: height)
    }
}

/// The thermometer badge. `n/a` when there is no sensor; pulses at 80 °C and
/// above, the way the Windows build's does.
struct TempBadge: View {
    var celsius: Int?
    var theme: Theme
    var metrics: Metrics

    @State private var dim = false

    var body: some View {
        let hot = (celsius ?? 0) >= Palette.hotAt
        HStack(spacing: metrics.len(2)) {
            Image(systemName: "thermometer.medium")
            Text(celsius.map { "\($0)°C" } ?? "n/a")
        }
        .font(.system(size: metrics.type(Metrics.Font.small), weight: .semibold))
        .foregroundStyle((Palette.tempColor(celsius) ?? theme.label).color)
        .padding(.horizontal, metrics.len(5))
        .padding(.vertical, metrics.len(1))
        .background(Palette.tempBadge(celsius, theme).color, in: RoundedRectangle(cornerRadius: metrics.len(6)))
        .opacity(hot && dim ? 0.35 : 1)
        .onAppear {
            guard hot else { return }
            withAnimation(.easeInOut(duration: 0.6).repeatForever(autoreverses: true)) { dim = true }
        }
        .fixedSize()
    }
}

/// A small square glyph button with a hover tint.
struct IconButton: View {
    var systemName: String
    var help: String
    var size: CGFloat
    var theme: Theme
    var action: () -> Void

    @State private var hover = false

    var body: some View {
        Button(action: action) {
            Image(systemName: systemName)
                .font(.system(size: size * 0.5, weight: .semibold))
                .frame(width: size, height: size)
                .foregroundStyle(theme.muted.color)
                .background(hover ? theme.chip.color : .clear, in: RoundedRectangle(cornerRadius: 6))
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help(help)
        .onHover { hover = $0 }
    }
}

/// Grab zones along every edge and corner. Corners are larger and sit on top,
/// so the overlap resizes both ways rather than whichever side was tested
/// first.
struct ResizeBands: View {
    var actions: WindowActions?
    var edge: CGFloat = 6
    var corner: CGFloat = 14

    var body: some View {
        ZStack {
            band(.left, width: edge, alignment: .leading)
            band(.right, width: edge, alignment: .trailing)
            band(.top, height: edge, alignment: .top)
            band(.bottom, height: edge, alignment: .bottom)
            band([.top, .left], width: corner, height: corner, alignment: .topLeading)
            band([.top, .right], width: corner, height: corner, alignment: .topTrailing)
            band([.bottom, .left], width: corner, height: corner, alignment: .bottomLeading)
            band([.bottom, .right], width: corner, height: corner, alignment: .bottomTrailing)
        }
    }

    private func band(_ edges: Edges, width: CGFloat? = nil, height: CGFloat? = nil,
                      alignment: Alignment) -> some View {
        // The hover and the drag belong to the band itself. Attached after the
        // positioning frame, they covered the whole window and every click
        // became a resize.
        Color.clear
            .frame(width: width, height: height)
            .contentShape(Rectangle())
            .onHover { inside in
                if inside { Self.cursor(edges).push() } else { NSCursor.pop() }
            }
            .gesture(DragGesture(minimumDistance: 0, coordinateSpace: .global)
                .onChanged { _ in actions?.resizeChanged(edges) }
                .onEnded { _ in actions?.resizeEnded() })
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: alignment)
    }

    static func cursor(_ edges: Edges) -> NSCursor {
        if edges.horizontal && edges.vertical {
            if #available(macOS 15, *) {
                let position: NSCursor.FrameResizePosition =
                    edges.contains(.top) ? (edges.contains(.left) ? .topLeft : .topRight)
                    : (edges.contains(.left) ? .bottomLeft : .bottomRight)
                return NSCursor.frameResize(position: position, directions: .all)
            }
            return .crosshair
        }
        return edges.horizontal ? .resizeLeftRight : .resizeUpDown
    }
}

/// Moving the window from anywhere that is not a control. The pointer's
/// screen position drives it, not the gesture's translation, because the
/// view the gesture is measured in is the thing moving.
struct WindowDrag: ViewModifier {
    var actions: WindowActions?
    var onDoubleClick: () -> Void

    func body(content: Content) -> some View {
        content
            .gesture(DragGesture(minimumDistance: 4, coordinateSpace: .global)
                .onChanged { _ in actions?.dragChanged() }
                .onEnded { _ in actions?.dragEnded() })
            .simultaneousGesture(TapGesture(count: 2).onEnded(onDoubleClick))
    }
}

extension View {
    func windowDrag(_ actions: WindowActions?, onDoubleClick: @escaping () -> Void) -> some View {
        modifier(WindowDrag(actions: actions, onDoubleClick: onDoubleClick))
    }
}

/// The panel behind a view: the theme's panel colour at the chosen opacity,
/// with a hairline border. Only the panel fades; the text stays opaque.
struct PanelBackground: View {
    var theme: Theme
    var opacity: Double
    var radius: CGFloat

    var body: some View {
        RoundedRectangle(cornerRadius: radius, style: .continuous)
            .fill(theme.panel.color.opacity(min(max(opacity, 40.0 / 255), 1)))
            .overlay(RoundedRectangle(cornerRadius: radius, style: .continuous)
                .strokeBorder(theme.border.color, lineWidth: 1))
    }
}

/// A vertical scroll view that can be switched off, for offscreen rendering.
struct Scrolling<Content: View>: View {
    var enabled: Bool
    @ViewBuilder var content: () -> Content

    var body: some View {
        if enabled {
            ScrollView { content() }
        } else {
            VStack(spacing: 0) {
                content()
                Spacer(minLength: 0)
            }
        }
    }
}
