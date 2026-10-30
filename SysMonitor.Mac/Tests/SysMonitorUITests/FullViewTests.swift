import AppKit
import Darwin
import Foundation
import SwiftUI
import SysMonitorCore
import Testing
@testable import SysMonitorUI

struct HistoryStoreTests {
    @Test func nothingIsKeptBeforeTheFirstRealSample() {
        let store = HistoryStore()
        store.push(Snapshot())
        #expect(store.keys.isEmpty)
    }

    @Test func everySeriesIsFiledAndDeviceIoOnlyOnce() {
        let store = HistoryStore()
        store.push(sampleSnapshot())
        #expect(store.history("cpu")?.latest == 42)
        #expect(store.history("core7")?.latest == 70)
        #expect(store.history("ram")?.latest == 61)
        #expect(store.history("used./")?.latest == 50)
        #expect(store.history("io.read.disk0")?.latest == 3)
        #expect(store.history("gpu0.tiler")?.latest == 8)
        #expect(store.history("net.down.en0")?.latest == 5)
        // Two volumes on disk0, one I/O series pair.
        #expect(store.keys.filter { $0.hasPrefix("io.read.") } == ["io.read.disk0"])
    }

    @Test func aDeviceThatGoesTakesItsHistory() {
        let store = HistoryStore()
        store.push(sampleSnapshot())
        var s = sampleSnapshot()
        s.adapters = []
        store.push(s)
        #expect(store.history("net.down.en0") == nil)
        #expect(store.history("cpu")?.count == 2)
    }

    @Test func historiesAreBounded() {
        let store = HistoryStore()
        for _ in 0..<200 {
            store.push(sampleSnapshot())
        }
        #expect(store.history("cpu")?.count == HistoryStore.points)
    }
}

struct FullTabsTests {
    private let en = Strings("en")

    @Test func tabsComeInTheOverallOrder() {
        let tabs = FullTabs.build(sampleSnapshot(), en)
        #expect(tabs.map(\.id) == ["cpu", "ram", "disk.disk0", "share./Volumes/Share", "gpu0", "net.en0"])
        #expect(FullTabs.build(Snapshot(), en).isEmpty)
    }

    @Test func volumesOnOnePhysicalDiskShareATab() {
        let disk = FullTabs.build(sampleSnapshot(), en).first { $0.id == "disk.disk0" }!
        #expect(disk.title == "Disk 0")
        #expect(disk.detail == "SSD · Apple Fabric · Macintosh HD · Data")
        #expect(disk.smallCharts.count == 2)
        #expect(disk.charts[0].series.map(\.key) == ["io.read.disk0", "io.write.disk0"])
        #expect(disk.facts.contains { $0.name == "Macintosh HD" && $0.value.hasPrefix("250.0 / 500.0 GB (50%)") })
    }

    @Test func noGpuNoAdapterNoTabs() {
        var s = sampleSnapshot()
        s.gpus = []
        s.adapters = []
        let ids = FullTabs.build(s, en).map(\.id)
        #expect(!ids.contains("gpu0") && !ids.contains("net.en0"))
    }

    @Test func cpuTabHasAGraphPerCore() {
        let cpu = FullTabs.build(sampleSnapshot(cores: 6), en)[0]
        #expect(cpu.smallCharts.map(\.title) == ["C0", "C1", "C2", "C3", "C4", "C5"])
        #expect(cpu.summary == "42%")
    }

    @Test func thaiLabelsAreThai() {
        let ram = FullTabs.build(sampleSnapshot(), Strings("th"))[1]
        #expect(ram.facts.map(\.name).contains("แรงกดดันหน่วยความจำ"))
    }

    @Test func lifetimeTotals() {
        #expect(FullTabs.total(1_500_000_000) == "1.50 GB")
        #expect(FullTabs.total(25_300_000) == "25.3 MB")
    }
}

struct ChartMathTests {
    @Test func percentGraphsTopOutAtOneHundred() {
        #expect(ChartMath.ceiling(.percent, peak: 12) == 100)
        #expect(ChartMath.ceilingLabel(.percent, peak: 12) == "100%")
    }

    @Test func throughputScalesToItsOwnPeak() {
        #expect(ChartMath.ceiling(.megabytesPerSecond, peak: 12.5) == 12.5)
        #expect(ChartMath.ceilingLabel(.megabytesPerSecond, peak: 12.5) == "12.5 MB/s")
        // An idle link does not stretch its noise to fill the box.
        #expect(ChartMath.ceiling(.megabytesPerSecond, peak: 0.001) == ChartMath.minimumRate)
    }

    @Test func newestReadingIsAtTheRightEdge() {
        let size = CGSize(width: 710, height: 100)
        let points = ChartMath.points([0, 50, 100][...], capacity: 72, ceiling: 100, size: size)
        #expect(points.last == CGPoint(x: 710, y: 0))
        #expect(points[1].y == 50)
        #expect(points[0].y == 100)
        #expect(points[0].x == 690)
    }

    @Test func valuesOutsideTheAxisAreClamped() {
        let points = ChartMath.points([-5, 250][...], capacity: 2, ceiling: 100, size: CGSize(width: 10, height: 10))
        #expect(points.map(\.y) == [10, 0])
    }
}

struct NavigationTests {
    @Test func doubleClickGoesRoundAndEscapeStepsBack() {
        #expect(ViewMode.widget.next == .overall)
        #expect(ViewMode.overall.next == .full)
        #expect(ViewMode.full.next == .widget)
        #expect(ViewMode.full.back == .overall)
        #expect(ViewMode.overall.back == .widget)
    }

    @Test func fullViewSizeIsUnsetUntilOpenedAndClampedAfter() throws {
        #expect(AppConfig().fullW == 0)
        let c = try JSONDecoder().decode(AppConfig.self, from: Data(#"{"fullW":100,"fullH":99999,"fullTab":"ram"}"#.utf8))
        #expect(c.fullW == 640 && c.fullH == 5000)
        #expect(c.fullTab == "ram")
    }
}

@MainActor
struct ChartRenderTests {
    private func render<V: View>(_ view: V, _ size: CGSize) throws -> CGImage {
        let renderer = ImageRenderer(content: view.frame(width: size.width, height: size.height))
        renderer.scale = 1
        return try #require(renderer.cgImage)
    }

    private func count(_ rgb: Rgb, in image: CGImage) -> Int {
        let w = image.width, h = image.height
        var pixels = [UInt8](repeating: 0, count: w * h * 4)
        let context = CGContext(data: &pixels, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w * 4,
                                space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        context.draw(image, in: CGRect(x: 0, y: 0, width: w, height: h))
        var hits = 0
        for i in stride(from: 0, to: pixels.count, by: 4)
        where abs(Int(pixels[i]) - Int(rgb.r)) <= 3 && abs(Int(pixels[i + 1]) - Int(rgb.g)) <= 3
            && abs(Int(pixels[i + 2]) - Int(rgb.b)) <= 3 {
            hits += 1
        }
        return hits
    }

    /// The tint under a CPU line on the light panel: a quarter blue.
    private var tint: Rgb {
        let a = Accent.cpu.rgb, b = Theme.light.panel
        func mix(_ x: UInt8, _ y: UInt8) -> UInt32 { UInt32((Double(x) * 0.25 + Double(y) * 0.75).rounded()) }
        return Rgb(mix(a.r, b.r) << 16 | mix(a.g, b.g) << 8 | mix(a.b, b.b))
    }

    private func chart(_ value: Double, points: Int) -> some View {
        let store = HistoryStore()
        var s = sampleSnapshot()
        s.cpuTotal = Int(value)
        for _ in 0..<points { store.push(s) }
        let spec = ChartSpec(title: "CPU", unit: .percent,
                             series: [SeriesSpec(key: "cpu", label: "CPU", color: Accent.cpu.rgb)])
        return ChartView(spec: spec, histories: store, revision: points, theme: .light,
                         metrics: Metrics(), height: 100)
            .background(Theme.light.panel.color)
    }

    @Test func aBusySeriesFillsMoreThanAQuietOne() throws {
        let busy = count(tint, in: try render(chart(80, points: 72), CGSize(width: 300, height: 130)))
        let quiet = count(tint, in: try render(chart(20, points: 72), CGSize(width: 300, height: 130)))
        #expect(busy > quiet * 2)
        #expect(quiet > 0)
    }

    @Test func oneReadingIsNotALineButTheGridStillDraws() throws {
        let image = try render(chart(80, points: 1), CGSize(width: 300, height: 130))
        #expect(count(tint, in: image) == 0)
        #expect(count(Theme.light.barEmpty, in: image) > 100)
    }

    @Test func memoryTabListsProcessesAndAgents() throws {
        var c = AppConfig()
        c.fullTab = "ram"
        let model = AppModel(config: c)
        var s = sampleSnapshot()
        let mb: UInt64 = 1_048_576
        s.processes = [
            ProcessEntry(pid: 100, name: "Browser Helper", uid: getuid(), footprint: 300 * mb),
            ProcessEntry(pid: 101, name: "Browser Helper", uid: getuid(), footprint: 500 * mb),
            ProcessEntry(pid: 102, name: "Editor", uid: getuid(), footprint: 600 * mb),
            ProcessEntry(pid: 1, name: "launchd", uid: 0, footprint: nil),
        ]
        var agent = LaunchAgent(label: "com.example.helper", file: "/x.plist")
        agent.program = "/Applications/Example.app/Contents/MacOS/helper"
        agent.runAtLoad = true
        s.launchAgents = [agent]
        model.pickedProcess = "Editor"
        model.expandedProcesses = ["Browser Helper"]
        for _ in 0..<20 { model.receive(s) }

        var view = FullView(model: model, actions: nil)
        view.scrollable = false
        let image = try render(view, CGSize(width: 900, height: 1100))
        if let dir = ProcessInfo.processInfo.environment["SYSMON_RENDER_DIR"] {
            let png = NSBitmapImageRep(cgImage: image).representation(using: .png, properties: [:])
            try? png?.write(to: URL(fileURLWithPath: dir).appendingPathComponent("full-memory.png"))
        }
        // The RAM graph and the picked process's graph are both purple.
        #expect(count(Accent.ram.rgb, in: image) > 300)
    }

    @Test(arguments: [false, true])
    func fullViewDrawsRailAndGraphs(dark: Bool) throws {
        var c = AppConfig()
        c.theme = dark ? "dark" : "light"
        let model = AppModel(config: c)
        for _ in 0..<30 { model.receive(sampleSnapshot()) }
        var view = FullView(model: model, actions: nil)
        view.scrollable = false
        let size = CGSize(width: 900, height: 640)
        let image = try render(view, size)
        if let dir = ProcessInfo.processInfo.environment["SYSMON_RENDER_DIR"] {
            let png = NSBitmapImageRep(cgImage: image).representation(using: .png, properties: [:])
            try? png?.write(to: URL(fileURLWithPath: dir).appendingPathComponent("full-\(dark ? "dark" : "light").png"))
        }
        let theme = dark ? Theme.dark : Theme.light
        // The selected tab's highlight and the facts box are control-coloured.
        #expect(count(theme.control, in: image) > 2000)
        #expect(count(Accent.cpu.rgb, in: image) > 200)
    }
}
