import Foundation
import IOKit

/// Graphics processors, from each `IOAccelerator`'s `PerformanceStatistics`
/// in the IOKit registry. Public registry data: no elevation, no private API.
///
/// Apple Silicon reports device, renderer and tiler utilisation and the
/// unified memory the GPU has in use. AMD and Intel drivers on Intel Macs
/// report device utilisation and, for a discrete card, VRAM in use; older AMD
/// drivers call utilisation `GPU Activity(%)`.
package enum GpuSensor {
    package static func read() -> [Gpu] {
        var iterator: io_iterator_t = 0
        guard IOServiceGetMatchingServices(kIOMainPortDefault, IOServiceMatching("IOAccelerator"),
                                           &iterator) == KERN_SUCCESS else { return [] }
        defer { IOObjectRelease(iterator) }

        var gpus: [Gpu] = []
        while case let entry = IOIteratorNext(iterator), entry != 0 {
            defer { IOObjectRelease(entry) }
            guard let stats = property(entry, "PerformanceStatistics") as? [String: Any] else { continue }
            let name = text(property(entry, "model")) ?? parentModel(entry) ?? ""
            let cores = (property(entry, "gpu-core-count") as? NSNumber)?.intValue ?? 0
            if let gpu = parse(stats, name: name, cores: cores) {
                gpus.append(gpu)
            }
        }
        return gpus
    }

    /// Nil when the statistics carry no utilisation at all: a GPU we cannot
    /// read is left out rather than drawn at a made-up zero.
    package static func parse(_ stats: [String: Any], name: String, cores: Int) -> Gpu? {
        func number(_ key: String) -> Double? { (stats[key] as? NSNumber)?.doubleValue }
        guard let usage = number("Device Utilization %") ?? number("GPU Activity(%)") else { return nil }

        var gpu = Gpu()
        gpu.name = name
        gpu.cores = cores
        gpu.usage = percent(usage)
        gpu.rendererUsage = number("Renderer Utilization %").map(percent)
        gpu.tilerUsage = number("Tiler Utilization %").map(percent)
        let gb = 1024.0 * 1024.0 * 1024.0
        if let used = number("In use system memory") ?? number("vramUsedBytes") {
            gpu.memoryUsedGb = used / gb
        }
        return gpu
    }

    private static func percent(_ value: Double) -> Int {
        Int(min(max(value.rounded(), 0), 100))
    }

    /// On an Intel Mac the accelerator has no model of its own; the PCI device
    /// above it does, as bytes.
    private static func parentModel(_ entry: io_registry_entry_t) -> String? {
        var parent: io_registry_entry_t = 0
        guard IORegistryEntryGetParentEntry(entry, kIOServicePlane, &parent) == KERN_SUCCESS else { return nil }
        defer { IOObjectRelease(parent) }
        return text(property(parent, "model"))
    }

    private static func text(_ value: Any?) -> String? {
        if let string = value as? String {
            return string
        }
        if let data = value as? Data {
            return String(decoding: data.prefix { $0 != 0 }, as: UTF8.self)
        }
        return nil
    }

    private static func property(_ entry: io_registry_entry_t, _ key: String) -> Any? {
        IORegistryEntryCreateCFProperty(entry, key as CFString, kCFAllocatorDefault, 0)?
            .takeRetainedValue()
    }
}
