import Foundation

/// One reading of the machine. Built by the sampler, published whole, and
/// never changed afterwards.
///
/// The C# build had to copy every list on the way in, because an
/// `IReadOnlyList` can be cast back to the `List` behind it. Swift arrays are
/// values, so a published snapshot is already a copy nobody else can write to.
public struct Snapshot: Sendable {
    public var cores: [Core] = []
    public var cpuTotal = 0
    public var cpuInfo = CpuInfo()
    public var ram = Ram()
    public var disks: [Disk] = []
    public var adapters: [Adapter] = []

    /// Every graphics processor whose statistics could be read.
    public var gpus: [Gpu] = []

    /// Every process, refreshed every few seconds rather than every tick.
    public var processes: [ProcessEntry] = []

    /// The user's LaunchAgents, refreshed on the slow cadence.
    public var launchAgents: [LaunchAgent] = []

    /// Nil until a real sensor is read. Never estimated from load (R-023).
    public var cpuTemp: Int?

    /// False until the first sample with a CPU delta has landed: the first
    /// pass only takes the baseline, so every core would read zero.
    public var ready = false

    public init() {}
}

public struct Core: Sendable, Equatable {
    public var usage: Int
    public var temp: Int?

    public init(usage: Int, temp: Int? = nil) {
        self.usage = usage
        self.temp = temp
    }
}

/// What the processor is, rather than what it is doing. Read once.
public struct CpuInfo: Sendable, Equatable {
    public var name = ""
    public var architecture = ""
    public var physicalCores = 0
    public var logical = 0

    /// Apple Silicon splits its cores into performance levels. Zero on an
    /// Intel Mac, which has one kind.
    public var performanceCores = 0
    public var efficiencyCores = 0

    public var known: Bool { !name.isEmpty || logical > 0 }

    public init() {}
}

public enum MemoryPressure: String, Sendable {
    case normal, warning, critical, unknown
}

/// Memory as Activity Monitor counts it, in binary gigabytes.
public struct Ram: Sendable, Equatable {
    public var usage = 0
    public var usedGb = 0.0
    public var totalGb = 0.0

    /// The parts of `usedGb`: app memory, wired, and compressed.
    public var appGb = 0.0
    public var wiredGb = 0.0
    public var compressedGb = 0.0

    public var swapUsedGb = 0.0
    public var swapTotalGb = 0.0
    public var pressure = MemoryPressure.unknown

    public init() {}
}

/// The physical device a volume lives on, as IOKit describes it. Static for
/// as long as the device is attached, so it is read once and cached.
public struct PhysicalDevice: Sendable, Equatable {
    /// The whole-disk BSD name, `disk0`. Volumes that share it share a device.
    public var id: String
    public var model = ""
    public var vendor = ""
    public var serial = ""
    public var revision = ""

    /// "SSD", "HDD", or "Disk" when the device will not say.
    public var media = "Disk"

    /// The interconnect: "Apple Fabric", "PCIe", "SATA", "USB", "Thunderbolt",
    /// "Disk Image". Empty when unknown.
    public var bus = ""
    public var isInternal = false

    /// Where to read the I/O counters. Not part of the public description.
    package var driverID: UInt64 = 0

    public init(id: String) {
        self.id = id
    }
}

/// A mounted volume, in decimal gigabytes as Finder counts them.
public struct Disk: Sendable, Equatable {
    /// The mount point, which is unique while mounted.
    public var id: String { mountPoint }
    public var mountPoint: String
    public var name = ""
    public var bsdName = ""
    public var fileSystem = ""
    public var isNetwork = false
    public var isRemovable = false
    public var isReadOnly = false

    public var usage = 0
    public var usedGb = 0.0
    public var totalGb = 0.0

    /// I/O of the physical device behind this volume. macOS counts I/O per
    /// device, not per volume, so two volumes on one disk show the same rate.
    public var readMb = 0.0
    public var writeMb = 0.0

    public var temp: Int?

    /// Nil for a network share, and until the device has been read.
    public var device: PhysicalDevice?

    public var title: String { name.isEmpty ? mountPoint : name }

    public init(mountPoint: String) {
        self.mountPoint = mountPoint
    }
}

/// A graphics processor, as its driver reports it.
public struct Gpu: Sendable, Equatable {
    public var name = ""
    public var cores = 0
    public var usage = 0

    /// Apple Silicon splits the work into a renderer and a tiler; other
    /// drivers do not say, and these stay nil.
    public var rendererUsage: Int?
    public var tilerUsage: Int?

    /// Unified memory in use on Apple Silicon, VRAM in use on a discrete card.
    public var memoryUsedGb: Double?

    public var temp: Int?

    public init() {}
}

/// A wired or wireless adapter that is connected.
public struct Adapter: Sendable, Equatable {
    /// The BSD name, `en0`.
    public var id: String
    public var name = ""
    public var wireless = false

    /// Link speed, or 0 when the adapter reports none.
    public var speedMbps = 0.0

    public var downMb = 0.0
    public var upMb = 0.0
    public var totalReceived: UInt64 = 0
    public var totalSent: UInt64 = 0

    /// Throughput against the link rate, for the bar. Both directions count:
    /// a saturated uplink matters as much as a saturated downlink.
    public var usage: Int {
        guard speedMbps > 0 else { return 0 }
        let percent = ((downMb + upMb) * 8 / speedMbps * 100).rounded()
        return Int(min(max(percent, 0), 100))
    }

    public init(id: String) {
        self.id = id
    }
}
