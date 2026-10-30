// Draws the app icon at 1024 px: the Windows build's SysMonitor.ico, redrawn
// rather than scaled up, because that file stops at 256 px and macOS wants
// 1024. Same design and palette: a dark rounded tile, three rising bars in
// the CPU, RAM and Disk accents.
//
//   swift tools/make-icon.swift <out.png>
import AppKit

let size = 1024.0
let image = NSImage(size: NSSize(width: size, height: size), flipped: false) { _ in
    func color(_ hex: UInt32) -> NSColor {
        NSColor(srgbRed: CGFloat((hex >> 16) & 0xff) / 255, green: CGFloat((hex >> 8) & 0xff) / 255,
                blue: CGFloat(hex & 0xff) / 255, alpha: 1)
    }
    // Apple's icon grid: the tile sits inside a 100 px margin.
    let tile = NSRect(x: 100, y: 100, width: 824, height: 824)
    let shape = NSBezierPath(roundedRect: tile, xRadius: 185, yRadius: 185)
    color(0x0f172a).setFill()
    shape.fill()
    color(0x2b374b).setStroke()
    shape.lineWidth = 14
    shape.stroke()

    // Bars at the ico's proportions: a quarter, a half and three quarters up.
    let bars: [(x: Double, height: Double, color: UInt32)] = [
        (262, 170, 0x3b82f6), (442, 300, 0xa855f7), (622, 470, 0x10b981),
    ]
    for bar in bars {
        let rect = NSRect(x: bar.x, y: 250, width: 140, height: bar.height)
        color(bar.color).setFill()
        NSBezierPath(roundedRect: rect, xRadius: 70, yRadius: 70).fill()
    }
    return true
}

let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: Int(size), pixelsHigh: Int(size), bitsPerSample: 8,
                           samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
                           bytesPerRow: 0, bitsPerPixel: 0)!
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
image.draw(in: NSRect(x: 0, y: 0, width: size, height: size))
NSGraphicsContext.restoreGraphicsState()
try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: CommandLine.arguments[1]))
