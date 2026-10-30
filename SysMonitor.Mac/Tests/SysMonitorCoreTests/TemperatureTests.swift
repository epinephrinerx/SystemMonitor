import Foundation
import Testing
@testable import SysMonitorCore

struct TemperaturePickTests {
    /// Trimmed from the M1 this was written on, including its broken sensors.
    private let m1: [(name: String, celsius: Double)] = [
        ("ANE MTR Temp Sensor1", 30.0), ("GPU MTR Temp Sensor1", 30.0), ("GPU MTR Temp Sensor4", 59.5),
        ("NAND CH0 temp", 48.0), ("PMU tdev3", -22.1), ("PMU tdie1", 59.7), ("PMU tdie7", 60.7),
        ("SOC MTR Temp Sensor1", 61.9), ("eACC MTR Temp Sensor0", 60.3), ("pACC MTR Temp Sensor3", 62.6),
    ]

    @Test func hottestOfEachKind() {
        #expect(TemperatureSensor.pick(m1) == Temperatures(cpu: 63, gpu: 60, nand: 48))
    }

    @Test func cpuFallsBackToDieThenSoc() {
        let noClusters = m1.filter { !$0.name.contains("ACC") }
        #expect(TemperatureSensor.pick(noClusters).cpu == 61)
        let socOnly = m1.filter { !$0.name.contains("ACC") && !$0.name.contains("tdie") }
        #expect(TemperatureSensor.pick(socOnly).cpu == 62)
    }

    @Test func implausibleReadingsAreDropped() {
        let broken: [(name: String, celsius: Double)] = [("pACC MTR Temp Sensor0", -22), ("GPU MTR Temp Sensor0", 400)]
        #expect(TemperatureSensor.pick(broken) == Temperatures())
        #expect(TemperatureSensor.pick([]) == Temperatures())
    }

    #if arch(arm64)
    @Test func thisMachinesSensorsAreRead() throws {
        let t = TemperatureSensor.read()
        let cpu = try #require(t.cpu)
        #expect((20...110).contains(cpu))
        #expect(t.gpu != nil && t.nand != nil)
    }
    #endif

    @Test func switchedOffMeansNoReading() {
        let sampler = Sampler(speed: .fast)
        sampler.readTemperatures = false
        #expect(sampler.sampleNow().cpuTemp == nil)
    }

    @Test func thermalStateIsAlwaysThere() {
        #expect(ThermalState(rawValue: ThermalState.current.rawValue) != nil)
    }
}
