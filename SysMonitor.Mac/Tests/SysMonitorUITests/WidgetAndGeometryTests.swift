import CoreGraphics
import Foundation
import SysMonitorCore
import Testing
@testable import SysMonitorUI

struct WidgetPagesTests {
    private let en = Strings("en")

    @Test func beforeTheFirstSampleItSaysSo() {
        let pages = WidgetPages.build(Snapshot(), AppConfig(), en)
        #expect(pages.map(\.kind) == [.message])
        #expect(pages[0].title == "Reading hardware...")
    }

    @Test func pagesComeInTheWindowsOrder() {
        let pages = WidgetPages.build(sampleSnapshot(), AppConfig(), en)
        #expect(pages.map(\.kind) == [
            .cpuBlock(0), .cpuBlock(4), .ram,
            .disk("/"), .disk("/Volumes/Data"), .disk("/Volumes/Share"),
            .net("en0"),
        ])
    }

    @Test func coresComeFourToAPageInTwoColumns() {
        let pages = WidgetPages.build(sampleSnapshot(cores: 6), AppConfig(), en)
        #expect(pages[0].title == "CPU  C0-3")
        #expect(pages[0].bars.map(\.label) == ["C0", "C1", "C2", "C3"])
        #expect(pages[0].columns == 2)
        #expect(pages[1].title == "CPU  C4-5")
        #expect(pages[1].columns == 1)
    }

    @Test func combinedModesMakeOnePageEach() {
        var c = AppConfig()
        c.cpuMode = "total"
        c.diskMode = "total"
        c.networkMode = "total"
        let pages = WidgetPages.build(sampleSnapshot(), c, en)
        #expect(pages.map(\.kind) == [.cpuTotal, .ram, .diskAll, .netAll])
        #expect(pages[0].detail == "8 cores")
        // 1250 GB used of 2000 GB
        #expect(pages[2].headline == 63)
    }

    @Test func volumesOnOneDeviceAreNotCountedTwice() {
        let disks = sampleSnapshot().disks
        let rate = WidgetPages.combinedRate(disks)
        // "/" and "/Volumes/Data" are both on disk0; the share has no device.
        #expect(rate.read == 3 && rate.write == 1)
    }

    @Test func networkDrivesCanBeLeftOut() {
        var c = AppConfig()
        c.includeNetworkDrives = false
        let kinds = WidgetPages.build(sampleSnapshot(), c, en).map(\.kind)
        #expect(!kinds.contains(.disk("/Volumes/Share")))
    }

    @Test func nothingSelectedSaysSo() {
        var c = AppConfig()
        c.showCpu = false
        c.showRam = false
        c.showDisk = false
        c.showNetwork = false
        let pages = WidgetPages.build(sampleSnapshot(), c, en)
        #expect(pages.map(\.kind) == [.message])
        #expect(pages[0].title == "Select something to display")
    }

    @Test func networkPageOnlyWhenThereIsAnAdapter() {
        var s = sampleSnapshot()
        s.adapters = []
        var c = AppConfig()
        c.networkMode = "total"
        #expect(!WidgetPages.build(s, c, en).map(\.kind).contains(.netAll))
    }

    @Test func aDriveRunningOutOfRoomSaysSoInstead() {
        let pages = WidgetPages.build(sampleSnapshot(diskUsage: 92), AppConfig(), Strings("th"),
                                      alerts: ["/": .hot])
        let page = pages.first { $0.kind == .disk("/") }!
        #expect(page.alert == .hot)
        #expect(page.detail.hasPrefix("⚠ Macintosh HD เกือบเต็ม (ใช้ 92%)"))
    }

    @Test func rates() {
        #expect(Format.rate(0.04) == "0.0")
        #expect(Format.rate(99.95) == "100.0")
        #expect(Format.rate(100) == "100")
        #expect(Format.rate(1234.5) == "1234")
        #expect(Format.linkSpeed(467) == "467 Mbps")
        #expect(Format.linkSpeed(2500) == "2.5 Gbps")
    }
}

struct DiskAlertTests {
    @Test func levelsWithHysteresis() {
        #expect(DiskAlertTracker.classify(69, previous: .ok) == .ok)
        #expect(DiskAlertTracker.classify(70, previous: .ok) == .warm)
        #expect(DiskAlertTracker.classify(68, previous: .warm) == .warm)
        #expect(DiskAlertTracker.classify(67, previous: .warm) == .ok)
        #expect(DiskAlertTracker.classify(88, previous: .hot) == .hot)
        #expect(DiskAlertTracker.classify(87, previous: .hot) == .warm)
    }

    @Test func dismissedStaysDismissedUntilItGetsWorse() {
        let t = DiskAlertTracker()
        t.update([("/", 75, true)])
        #expect(t.pending == ["/": .warm])
        t.dismiss("/")
        t.update([("/", 80, true)])
        #expect(t.pending.isEmpty)
        t.update([("/", 91, true)])
        #expect(t.pending == ["/": .hot])
    }

    @Test func recoveringClearsAndNetworkSharesAreIgnored() {
        let t = DiskAlertTracker()
        t.update([("/", 95, true), ("/Volumes/Share", 99, false)])
        #expect(t.pending == ["/": .hot])
        t.update([("/", 50, true)])
        #expect(t.pending.isEmpty)
    }

    @Test func anEjectedDriveTakesItsAlertWithIt() {
        let t = DiskAlertTracker()
        t.update([("/Volumes/USB", 95, true)])
        t.update([])
        #expect(t.pending.isEmpty)
    }
}

struct GeometryTests {
    private let screen = CGRect(x: 0, y: 0, width: 1440, height: 875)
    private let widget = CGRect(x: 500, y: 400, width: 270, height: 104)

    @Test func snapsToNearbyEdgesOnly() {
        let near = WindowGeometry.snap(CGRect(x: 20, y: 860 - 104, width: 270, height: 104), to: screen)
        #expect(near.minX == 0)
        #expect(near.maxY == 875)
        let far = WindowGeometry.snap(widget, to: screen)
        #expect(far == widget)
        let right = WindowGeometry.snap(CGRect(x: 1440 - 270 - 10, y: 10, width: 270, height: 104), to: screen)
        #expect(right.maxX == 1440 && right.minY == 0)
    }

    @Test func rightEdgeKeepsTheShapeAndTheLeftSide() {
        let f = resize(.right, dx: 270, dy: 0)
        #expect(f.width == 540 && f.height == 208)
        #expect(f.minX == widget.minX)
        #expect(f.maxY == widget.maxY)   // grows downward
    }

    @Test func leftEdgePinsTheRightSide() {
        let f = resize(.left, dx: 135, dy: 0)
        #expect(f.width == 200 * 1 || f.width == 135)
        #expect(f.maxX == widget.maxX)
        #expect(abs(f.width / f.height - 270.0 / 104.0) < 0.0001)
    }

    @Test func cornerFollowsTheAxisThatMovedFurther() {
        let f = resize([.bottom, .right], dx: 27, dy: -52)
        // dy grows height by 50%, dx width by 10%: height wins.
        #expect(abs(f.height - 156) < 0.0001)
        #expect(abs(f.width - 405) < 0.0001)
        #expect(f.minX == widget.minX && f.maxY == widget.maxY)
    }

    @Test func limitsHoldAndTheFarSideStaysPut() {
        let big = resize(.right, dx: 5000, dy: 0)
        #expect(big.height <= 400 && big.width <= 900)
        #expect(big.minX == widget.minX)
        let small = resize(.left, dx: 500, dy: 0)
        #expect(small.width >= 200 && small.height >= 64)
        #expect(small.maxX == widget.maxX)
    }

    @Test func cornerStopsAtTheScreenEdge() {
        let low = CGRect(x: 500, y: 30, width: 270, height: 104)
        let f = WindowGeometry.resizeProportional(origin: low, edges: [.bottom, .right],
                                                  delta: CGVector(dx: 0, dy: -500),
                                                  min: AppConfig.widgetMin, max: AppConfig.widgetMax,
                                                  visible: screen)
        #expect(f.minY >= screen.minY - 0.0001)
    }

    @Test func sideDragMovesUpWhenItMeetsTheBottom() {
        let low = CGRect(x: 500, y: 10, width: 270, height: 104)
        let f = WindowGeometry.resizeProportional(origin: low, edges: .right, delta: CGVector(dx: 270, dy: 0),
                                                  min: AppConfig.widgetMin, max: AppConfig.widgetMax,
                                                  visible: screen)
        #expect(f.minY == 0)
        #expect(f.height == 208)
    }

    @Test func topEdgeGrowsAboutTheCentre() {
        let f = resize(.top, dx: 0, dy: 52)
        #expect(f.minY == widget.minY)
        #expect(abs(f.midX - widget.midX) < 0.0001)
    }

    @Test func overallSizesFreelyWithinItsLimits() {
        let o = CGRect(x: 100, y: 100, width: 630, height: 480)
        let f = WindowGeometry.resizeFree(origin: o, edges: [.top, .left], delta: CGVector(dx: 500, dy: -500),
                                          min: AppConfig.overallMin, max: AppConfig.overallMax)
        #expect(f.width == 470 && f.height == 300)
        #expect(f.maxX == o.maxX && f.minY == o.minY)
    }

    @Test func aLostWindowComesBackOnScreen() {
        let f = WindowGeometry.keepOnScreen(CGRect(x: 3000, y: -400, width: 270, height: 104), screen)
        #expect(screen.contains(f))
        let first = WindowGeometry.firstPlacement(size: AppConfig.widgetSize, visible: screen)
        #expect(first.maxX == 1440 - 24 && first.minY == 24)
    }

    private func resize(_ edges: Edges, dx: CGFloat, dy: CGFloat) -> CGRect {
        WindowGeometry.resizeProportional(origin: widget, edges: edges, delta: CGVector(dx: dx, dy: dy),
                                          min: AppConfig.widgetMin, max: AppConfig.widgetMax, visible: screen)
    }
}

@MainActor
struct AppModelTests {
    @Test func stepWrapsBothWays() {
        let model = AppModel()
        model.receive(sampleSnapshot())
        let count = model.pages.count
        model.step(-1)
        #expect(model.pageIndex == count - 1)
        model.step(1)
        #expect(model.pageIndex == 0)
    }

    @Test func rotationWaitsFiveSecondsAndRespectsPause() {
        let model = AppModel()
        model.receive(sampleSnapshot())
        let now = Date()
        model.step(0)
        model.rotateIfDue(now: now.addingTimeInterval(4))
        #expect(model.pageIndex == 0)
        model.rotateIfDue(now: now.addingTimeInterval(5.1))
        #expect(model.pageIndex == 1)

        model.paused = true
        model.rotateIfDue(now: now.addingTimeInterval(60))
        #expect(model.pageIndex == 1)
        // Stepping by hand while paused keeps it paused.
        model.step(1)
        #expect(model.pageIndex == 2 && model.paused)
    }

    @Test func onlyTheWidgetRotates() {
        let model = AppModel()
        model.receive(sampleSnapshot())
        model.mode = .overall
        model.step(0)
        model.rotateIfDue(now: Date().addingTimeInterval(60))
        #expect(model.pageIndex == 0)
    }

    @Test func fewerPagesDoNotLeaveTheIndexBehind() {
        let model = AppModel()
        model.receive(sampleSnapshot())
        model.step(-1)
        model.config.showDisk = false
        _ = model.currentPage
        #expect(model.pages.count == 4)
    }

    @Test func diskAlertsFollowTheSnapshot() {
        let model = AppModel()
        model.receive(sampleSnapshot(diskUsage: 93))
        #expect(model.alerts["/"] == .hot)
        #expect(model.alerts["/Volumes/Share"] == nil)
        model.dismissAlert("/")
        #expect(model.alerts.isEmpty)
    }
}
