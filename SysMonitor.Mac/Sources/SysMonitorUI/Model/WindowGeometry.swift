import CoreGraphics

/// Which sides of a window a resize drag moves. In AppKit coordinates, so
/// `top` is the side at `maxY`.
public struct Edges: OptionSet, Hashable, Sendable {
    public let rawValue: Int
    public init(rawValue: Int) { self.rawValue = rawValue }

    public static let left = Edges(rawValue: 1)
    public static let right = Edges(rawValue: 2)
    public static let top = Edges(rawValue: 4)
    public static let bottom = Edges(rawValue: 8)

    var horizontal: Bool { contains(.left) || contains(.right) }
    var vertical: Bool { contains(.top) || contains(.bottom) }
}

/// The arithmetic of moving and sizing the windows, apart from AppKit so it
/// can be tested without one. Every resize frame is computed from the frame
/// the drag started on, never from the previous frame: an incremental version
/// drifts, worst of all once a limit is hit and the pointer keeps going.
public enum WindowGeometry {
    public static let snapDistance: CGFloat = 25

    /// Pull a window flush against any screen edge it was dropped near.
    public static func snap(_ frame: CGRect, to visible: CGRect, distance: CGFloat = snapDistance) -> CGRect {
        var f = frame
        if abs(f.minX - visible.minX) <= distance {
            f.origin.x = visible.minX
        } else if abs(f.maxX - visible.maxX) <= distance {
            f.origin.x = visible.maxX - f.width
        }
        if abs(f.minY - visible.minY) <= distance {
            f.origin.y = visible.minY
        } else if abs(f.maxY - visible.maxY) <= distance {
            f.origin.y = visible.maxY - f.height
        }
        return f
    }

    /// Keep a restored window reachable: inside the screen if it fits, and
    /// at least on it if it does not.
    public static func keepOnScreen(_ frame: CGRect, _ visible: CGRect) -> CGRect {
        var f = frame
        f.origin.x = min(max(f.minX, visible.minX), max(visible.minX, visible.maxX - f.width))
        f.origin.y = min(max(f.minY, visible.minY), max(visible.minY, visible.maxY - f.height))
        return f
    }

    /// The widget keeps its shape (R-003). A side drag scales by that axis; a
    /// corner drag by whichever axis moved further. The opposite side or
    /// corner stays where it was.
    public static func resizeProportional(origin: CGRect, edges: Edges, delta: CGVector,
                                          min minSize: CGSize, max maxSize: CGSize,
                                          visible: CGRect) -> CGRect {
        let w0 = origin.width, h0 = origin.height
        guard w0 > 0, h0 > 0, !edges.isEmpty else { return origin }

        // The feasible scale keeps the window inside both limits at once. If
        // none does -- a start already outside them -- keep to whichever end
        // is nearer the current size: the shape matters more than one limit.
        var sMin = max(minSize.width / w0, minSize.height / h0)
        var sMax = min(maxSize.width / w0, maxSize.height / h0)
        if sMin > sMax {
            if abs(1 - sMin) >= abs(1 - sMax) { sMin = sMax } else { sMax = sMin }
        }

        let sX = edges.contains(.right) ? (w0 + delta.dx) / w0
            : edges.contains(.left) ? (w0 - delta.dx) / w0 : 1
        let sY = edges.contains(.top) ? (h0 + delta.dy) / h0
            : edges.contains(.bottom) ? (h0 - delta.dy) / h0 : 1

        let corner = edges.horizontal && edges.vertical
        var s = corner ? (abs(sX - 1) >= abs(sY - 1) ? sX : sY)
            : edges.horizontal ? sX : sY

        if corner {
            // The axis following the scale has no pointer to stop it at the
            // screen edge, so bound the scale by the room there is.
            let roomH = edges.contains(.top) ? visible.maxY - origin.minY : origin.maxY - visible.minY
            let roomW = edges.contains(.left) ? origin.maxX - visible.minX : visible.maxX - origin.minX
            s = min(s, roomH / h0, roomW / w0)
        }
        s = min(max(s, sMin), sMax)

        let w = w0 * s, h = h0 * s
        var x: CGFloat, y: CGFloat
        if edges.contains(.left) {
            x = origin.maxX - w
        } else if edges.contains(.right) {
            x = origin.minX
        } else {
            // A top or bottom drag grows the width about the centre.
            x = min(max(origin.midX - w / 2, visible.minX), max(visible.minX, visible.maxX - w))
        }
        if edges.contains(.bottom) {
            y = origin.maxY - h
        } else if edges.contains(.top) {
            y = origin.minY
        } else {
            // A side drag grows downward, then upward once it meets the
            // bottom of the screen.
            y = max(origin.maxY - h, visible.minY)
        }
        return CGRect(x: x, y: y, width: w, height: h)
    }

    /// The Overall view sizes freely; the opposite side stays put.
    public static func resizeFree(origin: CGRect, edges: Edges, delta: CGVector,
                                  min minSize: CGSize, max maxSize: CGSize) -> CGRect {
        var w = origin.width, h = origin.height
        if edges.contains(.right) { w += delta.dx }
        if edges.contains(.left) { w -= delta.dx }
        if edges.contains(.top) { h += delta.dy }
        if edges.contains(.bottom) { h -= delta.dy }
        w = min(max(w, minSize.width), maxSize.width)
        h = min(max(h, minSize.height), maxSize.height)
        let x = edges.contains(.left) ? origin.maxX - w : origin.minX
        let y = edges.contains(.bottom) ? origin.maxY - h : origin.minY
        return CGRect(x: x, y: y, width: w, height: h)
    }

    /// Where a widget goes the first time: the bottom-right of the screen.
    public static func firstPlacement(size: CGSize, visible: CGRect, inset: CGFloat = 24) -> CGRect {
        CGRect(x: visible.maxX - size.width - inset, y: visible.minY + inset,
               width: size.width, height: size.height)
    }
}
