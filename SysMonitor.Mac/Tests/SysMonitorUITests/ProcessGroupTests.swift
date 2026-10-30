import Darwin
import Foundation
import SysMonitorCore
import Testing
@testable import SysMonitorUI

struct ProcessGroupTests {
    private let mb: UInt64 = 1_048_576
    private var sample: [ProcessEntry] {
        [
            ProcessEntry(pid: 100, name: "Browser Helper", uid: getuid(), footprint: 300 * mb),
            ProcessEntry(pid: 101, name: "Browser Helper", uid: getuid(), footprint: 500 * mb),
            ProcessEntry(pid: 102, name: "Editor", uid: getuid(), footprint: 600 * mb),
            ProcessEntry(pid: 1, name: "launchd", uid: 0, footprint: nil),
            ProcessEntry(pid: 50, name: "coreaudiod", uid: 202, footprint: nil),
        ]
    }

    @Test func sameNameIsOneLineLargestFirst() {
        let groups = ProcessGroups.build(sample)
        #expect(groups.map(\.name) == ["Browser Helper", "Editor", "coreaudiod", "launchd"])
        #expect(groups[0].footprint == 800 * mb)
        #expect(groups[0].members.map(\.pid) == [101, 100])
    }

    @Test func unreadableGroupsGoLastAndAreProtected() {
        let groups = ProcessGroups.build(sample)
        #expect(!groups[2].readable && groups[2].protected)
        #expect(groups[0].readable && !groups[0].protected)
    }

    @Test func searchMatchesAnywhereIgnoringCase() {
        #expect(ProcessGroups.build(sample, filter: "help").map(\.name) == ["Browser Helper"])
        #expect(ProcessGroups.build(sample, filter: "  ").count == 4)
        #expect(ProcessGroups.build(sample, filter: "zzz").isEmpty)
    }

    @Test func topLine() {
        #expect(ProcessGroups.topLine(sample, Strings("en")) == "Top RAM: Browser Helper 800 MB")
        #expect(ProcessGroups.topLine(sample, Strings("th")) == "ใช้ RAM สูงสุด: Browser Helper 800 MB")
        #expect(ProcessGroups.topLine([], Strings("en")) == nil)
        #expect(ProcessGroups.megabytes(2048) == "2.0 GB")
    }

    @Test func widgetRamPageNamesTheLargest() {
        var s = sampleSnapshot()
        s.processes = sample
        let ram = WidgetPages.build(s, AppConfig(), Strings("en")).first { $0.kind == .ram }!
        #expect(ram.detail.hasSuffix("·  Top RAM: Browser Helper 800 MB"))
    }

    @Test func onlyTheLargestAndThePickedAreGraphed() {
        var s = sampleSnapshot()
        s.processes = (0..<10).map {
            ProcessEntry(pid: Int32(200 + $0), name: "p\($0)", uid: getuid(), footprint: UInt64($0 + 1) * mb)
        }
        let store = HistoryStore()
        store.push(s, alsoProcess: "p0")
        let graphed = store.keys.filter { $0.hasPrefix("proc.") }
        #expect(graphed == ["proc.p9", "proc.p8", "proc.p7", "proc.p6", "proc.p5", "proc.p0"])
        #expect(store.history("proc.p9")?.latest == 10)
    }

    @Test func temperaturesReachThePagesAndTabs() {
        let pages = WidgetPages.build(sampleSnapshot(), AppConfig(), Strings("en"))
        #expect(pages.first { $0.kind == .gpu(0) }?.temp == .reading(59))
        #expect(pages.first { $0.kind == .cpuBlock(0) }?.temp == .reading(82))
        let cpu = FullTabs.build(sampleSnapshot(), Strings("th"))[0]
        #expect(cpu.facts.contains { $0.name == "อุณหภูมิ" && $0.value == "82°C" })
        #expect(cpu.facts.contains { $0.name == "สถานะความร้อน" && $0.value == "ปกติ" })
        #expect(AppConfig().readTemperatures)
    }

    @Test func memoryGraphsScaleToTheirPeak() {
        #expect(ChartMath.ceiling(.megabytes, peak: 800) == 800)
        #expect(ChartMath.ceilingLabel(.megabytes, peak: 2048) == "2.0 GB")
    }
}
