import Foundation
import Testing
@testable import SysMonitorCore

struct GpuParseTests {
    @Test func appleSiliconStatistics() throws {
        // As an M1 reports them, trimmed.
        let stats: [String: Any] = [
            "Device Utilization %": 48, "Renderer Utilization %": 47, "Tiler Utilization %": 12,
            "In use system memory": 793_903_104, "Alloc system memory": 2_446_524_416,
        ]
        let gpu = try #require(GpuSensor.parse(stats, name: "Apple M1", cores: 7))
        #expect(gpu.usage == 48)
        #expect(gpu.rendererUsage == 47)
        #expect(gpu.tilerUsage == 12)
        #expect(abs(gpu.memoryUsedGb! - 0.739) < 0.001)
        #expect(gpu.name == "Apple M1" && gpu.cores == 7)
    }

    @Test func discreteCardReportsVram() throws {
        let stats: [String: Any] = ["Device Utilization %": 5, "vramUsedBytes": 1_073_741_824]
        let gpu = try #require(GpuSensor.parse(stats, name: "AMD Radeon Pro 5500M", cores: 0))
        #expect(gpu.memoryUsedGb == 1)
        #expect(gpu.rendererUsage == nil && gpu.tilerUsage == nil)
    }

    @Test func olderAmdDriversNameItDifferently() {
        #expect(GpuSensor.parse(["GPU Activity(%)": 30], name: "", cores: 0)?.usage == 30)
    }

    @Test func noUtilisationIsNoGpuNotAZero() {
        #expect(GpuSensor.parse(["Alloc system memory": 1], name: "x", cores: 1) == nil)
    }

    @Test func outOfRangeIsClamped() {
        #expect(GpuSensor.parse(["Device Utilization %": 140], name: "", cores: 0)?.usage == 100)
    }

    #if arch(arm64)
    @Test func thisMachinesGpuIsRead() throws {
        let gpu = try #require(GpuSensor.read().first)
        #expect(gpu.name.hasPrefix("Apple"))
        #expect(gpu.cores > 0)
        #expect(gpu.rendererUsage != nil)
    }
    #endif
}
