import SwiftUI

/// A history graph drawn the way Task Manager draws one: a framed box, a
/// square grid, a solid tint under the line, an angular stroke, and the axis
/// named above the box -- what it counts on the left, where its top is on
/// the right.
///
/// One `Canvas`, not a view per point: seventy-two points as seventy-two
/// views would cost more than the rest of the window together.
struct ChartView: View {
    var spec: ChartSpec
    var histories: HistoryStore
    /// Changes on every push, so SwiftUI knows the canvas is stale even though
    /// the histories it reads are the same objects.
    var revision: Int
    var theme: Theme
    var metrics: Metrics
    var height: CGFloat
    var showLegend = false

    var body: some View {
        let m = metrics
        let lines = spec.series.map { (spec: $0, values: values($0.key)) }
        let peak = lines.flatMap(\.values).max() ?? 0
        let ceiling = ChartMath.ceiling(spec.unit, peak: peak)

        VStack(alignment: .leading, spacing: m.len(3)) {
            HStack(spacing: m.len(8)) {
                Text(spec.title).lineLimit(1)
                if showLegend && lines.count > 1 {
                    ForEach(lines, id: \.spec.key) { line in
                        HStack(spacing: m.len(3)) {
                            Rectangle().fill(line.spec.color.color).frame(width: m.len(10), height: 2)
                            Text("\(line.spec.label) \(Format.rate(line.values.last ?? 0))")
                        }
                        .fixedSize()
                    }
                }
                Spacer(minLength: 0)
                Text(ChartMath.ceilingLabel(spec.unit, peak: peak)).fixedSize()
            }
            .font(.system(size: m.type(Metrics.Font.small)))
            .foregroundStyle(theme.label.color)

            Canvas { context, size in
                draw(lines, ceiling: ceiling, in: &context, size: size)
            }
            .frame(height: height)
        }
    }

    private func values(_ key: String) -> [Double] {
        guard let history = histories.history(key) else { return [] }
        var buffer = [Double](repeating: 0, count: history.count)
        history.copy(into: &buffer)
        return buffer
    }

    private func draw(_ lines: [(spec: SeriesSpec, values: [Double])], ceiling: Double,
                      in context: inout GraphicsContext, size: CGSize) {
        let box = CGRect(origin: .zero, size: size).insetBy(dx: 0.5, dy: 0.5)
        let grid = theme.barEmpty.color

        // The grid, six by four, on half-pixel offsets so a hairline lands on
        // a pixel instead of straddling two.
        var gridPath = Path()
        for i in 1..<6 {
            let x = (box.minX + box.width * CGFloat(i) / 6).rounded() + 0.5
            gridPath.move(to: CGPoint(x: x, y: box.minY))
            gridPath.addLine(to: CGPoint(x: x, y: box.maxY))
        }
        for i in 1..<4 {
            let y = (box.minY + box.height * CGFloat(i) / 4).rounded() + 0.5
            gridPath.move(to: CGPoint(x: box.minX, y: y))
            gridPath.addLine(to: CGPoint(x: box.maxX, y: y))
        }
        context.stroke(gridPath, with: .color(grid), lineWidth: 1)

        for (index, line) in lines.enumerated() {
            let points = ChartMath.points(line.values[...], capacity: HistoryStore.points,
                                          ceiling: ceiling, size: box.size)
                .map { CGPoint(x: $0.x + box.minX, y: $0.y + box.minY) }
            // One reading is a dot, not a line.
            guard points.count > 1 else { continue }

            var stroke = Path()
            stroke.addLines(points)

            // Only the first series is filled: a second fill would hide the
            // first. The tint is the line mixed toward the panel rather than
            // made transparent, so the grid stays hidden under it.
            if index == 0 {
                var fill = stroke
                fill.addLine(to: CGPoint(x: points.last!.x, y: box.maxY))
                fill.addLine(to: CGPoint(x: points.first!.x, y: box.maxY))
                fill.closeSubpath()
                context.fill(fill, with: .color(mix(line.spec.color, theme.panel, 0.25)))
            }
            context.stroke(stroke, with: .color(line.spec.color.color),
                           style: StrokeStyle(lineWidth: 1.5, lineCap: .butt, lineJoin: .miter))
        }

        // The frame last, so no fill paints over it.
        context.stroke(Path(box), with: .color(line(lines)), lineWidth: 1)
    }

    private func line(_ lines: [(spec: SeriesSpec, values: [Double])]) -> Color {
        (lines.first?.spec.color ?? theme.border).color.opacity(0.8)
    }

    /// `amount` of `a` over `1 - amount` of `b`.
    private func mix(_ a: Rgb, _ b: Rgb, _ amount: Double) -> Color {
        func channel(_ x: UInt8, _ y: UInt8) -> Double {
            (Double(x) * amount + Double(y) * (1 - amount)) / 255
        }
        return Color(.sRGB, red: channel(a.r, b.r), green: channel(a.g, b.g), blue: channel(a.b, b.b))
    }
}
