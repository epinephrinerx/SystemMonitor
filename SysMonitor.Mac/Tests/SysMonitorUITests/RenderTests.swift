import AppKit
import CoreGraphics
import SwiftUI
import Testing
@testable import SysMonitorUI

/// The views drawn offscreen and read back, so the look can be checked
/// without putting a window over anyone's desktop.
@MainActor
struct RenderTests {
    /// Set `SYSMON_RENDER_DIR` to keep a PNG of every render, so the look can
    /// be judged by eye.
    private func render<V: View>(_ view: V, _ size: CGSize, name: String = #function) throws -> CGImage {
        let renderer = ImageRenderer(content: view.frame(width: size.width, height: size.height))
        renderer.scale = 1
        let image = try #require(renderer.cgImage)
        if let dir = ProcessInfo.processInfo.environment["SYSMON_RENDER_DIR"] {
            renderer.scale = 2
            if let large = renderer.cgImage,
               let png = NSBitmapImageRep(cgImage: large).representation(using: .png, properties: [:]) {
                let file = name.prefix { $0 != "(" } + "-\(Int(size.width))x\(Int(size.height))-\(UUID().uuidString.prefix(4)).png"
                try? png.write(to: URL(fileURLWithPath: dir).appendingPathComponent(String(file)))
            }
        }
        return image
    }

    /// The share of pixels within a couple of levels of `rgb`. Tight on
    /// purpose: the light theme's sidebar and card colours are five levels apart.
    private func share(of rgb: Rgb, in image: CGImage, tolerance: Int = 2) -> Double {
        let w = image.width, h = image.height
        var pixels = [UInt8](repeating: 0, count: w * h * 4)
        let context = CGContext(data: &pixels, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w * 4,
                                space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        context.draw(image, in: CGRect(x: 0, y: 0, width: w, height: h))
        var hits = 0
        for i in stride(from: 0, to: pixels.count, by: 4) {
            if abs(Int(pixels[i]) - Int(rgb.r)) <= tolerance, abs(Int(pixels[i + 1]) - Int(rgb.g)) <= tolerance,
               abs(Int(pixels[i + 2]) - Int(rgb.b)) <= tolerance, pixels[i + 3] > 240 {
                hits += 1
            }
        }
        return Double(hits) / Double(w * h)
    }

    private func flat(_ view: OverallView) -> OverallView {
        var view = view
        view.scrollable = false
        return view
    }

    private func model(dark: Bool, lang: String = "en", font: Double = 1) -> AppModel {
        var c = AppConfig()
        c.theme = dark ? "dark" : "light"
        c.lang = lang
        c.fontScale = font
        let m = AppModel(config: c)
        m.receive(sampleSnapshot())
        return m
    }

    @Test(arguments: [false, true])
    func widgetDrawsOnItsThemePanel(dark: Bool) throws {
        let image = try render(WidgetView(model: model(dark: dark), actions: nil), AppConfig.widgetSize)
        #expect(image.width == 270 && image.height == 104)
        let panel = dark ? Theme.dark.panel : Theme.light.panel
        #expect(share(of: panel, in: image) > 0.5)
        // The CPU meter's blue is on the first page.
        #expect(share(of: Accent.cpu.rgb, in: image) > 0.005)
    }

    @Test(arguments: [false, true])
    func overallDrawsSidebarAndCards(dark: Bool) throws {
        let theme = dark ? Theme.dark : Theme.light
        let image = try render(flat(OverallView(model: model(dark: dark), actions: nil)), AppConfig.overallSize)
        #expect(share(of: theme.sidebar, in: image) > 0.1)
        #expect(share(of: theme.control, in: image) > 0.1)
    }

    @Test func aBiggerWidgetDrawsTheSamePageBigger() throws {
        let m = model(dark: false)
        let small = try render(WidgetView(model: m, actions: nil), AppConfig.widgetSize)
        let large = try render(WidgetView(model: m, actions: nil), CGSize(width: 540, height: 208))
        let a = share(of: Accent.cpu.rgb, in: small)
        let b = share(of: Accent.cpu.rgb, in: large)
        // Same proportion of the panel is meter, within rendering noise.
        #expect(abs(a - b) < a * 0.5)
    }

    @Test func thaiAtTheLargestFontStillRenders() throws {
        let image = try render(flat(OverallView(model: model(dark: false, lang: "th", font: 1.6), actions: nil)),
                               AppConfig.overallMin)
        #expect(image.width == 470)
    }
}
