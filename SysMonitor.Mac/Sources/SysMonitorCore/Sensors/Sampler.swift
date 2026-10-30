import Foundation

/// How often to sample. The same three cadences as the Windows build.
public enum RefreshSpeed: String, Sendable, CaseIterable {
    case eco, balanced, fast

    /// CPU, memory, disk and network rates.
    public var metrics: TimeInterval {
        switch self {
        case .eco: return 5
        case .balanced: return 2.5
        case .fast: return 1
        }
    }

    /// Free space and the list of mounted volumes.
    public var space: TimeInterval {
        switch self {
        case .eco: return 30
        case .balanced: return 15
        case .fast: return 10
        }
    }

    /// Temperatures, from phase 5 on.
    public var temps: TimeInterval {
        switch self {
        case .eco: return 60
        case .balanced, .fast: return 30
        }
    }
}

/// Reads the machine on a background queue and publishes whole snapshots for
/// the UI to pick up.
///
/// Cheap counters (CPU, memory, I/O) are read every tick; slow-moving ones
/// (free space, the volume list, device identity, adapters) get their own
/// longer cadences. That split is where most of the CPU saving comes from.
///
/// Everything below `queue` is touched only on that queue. The published
/// snapshot and the speed are the two things other threads read, and they
/// sit behind `lock`.
public final class Sampler: @unchecked Sendable {
    /// A probe slower than this marks the device sluggish and shelves it.
    /// This is backoff, not a timeout: a stalled network mount is skipped on
    /// later ticks rather than being allowed to stall every future sample.
    private static let slowProbeMs: UInt64 = 250
    private static let slowBackoffMs: UInt64 = 5 * 60 * 1000
    private static let hardwareEveryMs: UInt64 = 60 * 1000

    private let queue = DispatchQueue(label: "SysMonitor.Sampler", qos: .utility)
    private let lock = NSLock()
    private var published = Snapshot()
    private var currentSpeed: RefreshSpeed
    private var publishHandler: (@Sendable (Snapshot) -> Void)?

    private var timer: DispatchSourceTimer?
    private var previousCpu: [CpuTicks]?
    private var previousIo: [UInt64: (counters: (read: UInt64, written: UInt64), stamp: UInt64)] = [:]
    private var previousNet: [String: (counters: (read: UInt64, written: UInt64), stamp: UInt64)] = [:]
    private var lastRam = Ram()
    private var volumes: [Disk] = []
    private var devices: [String: PhysicalDevice] = [:]
    private var noDevice = Set<String>()
    private var adapters: [NetworkSensor.Hardware] = []
    private var cpuInfo = CpuInfo()
    private var nextSpace: UInt64 = 0
    private var nextHardware: UInt64 = 0
    private var slowUntil: [String: UInt64] = [:]

    public init(speed: RefreshSpeed = .balanced) {
        currentSpeed = speed
    }

    deinit {
        timer?.cancel()
    }

    /// The most recent complete reading. Safe to call from any thread.
    public var current: Snapshot {
        lock.withLock { published }
    }

    public var speed: RefreshSpeed {
        get { lock.withLock { currentSpeed } }
        set {
            lock.withLock { currentSpeed = newValue }
            queue.async { [weak self] in self?.schedule() }
        }
    }

    /// Called on the sampler's queue after each snapshot is published.
    public var onPublish: (@Sendable (Snapshot) -> Void)? {
        get { lock.withLock { publishHandler } }
        set { lock.withLock { publishHandler = newValue } }
    }

    public func start() {
        queue.async { [weak self] in
            guard let self, timer == nil else { return }
            let timer = DispatchSource.makeTimerSource(queue: queue)
            timer.setEventHandler { [weak self] in self?.tick() }
            self.timer = timer
            schedule()
            timer.resume()
        }
    }

    public func stop() {
        queue.sync {
            timer?.cancel()
            timer = nil
        }
    }

    /// Take one sample now, on the sampler's queue, and return it.
    @discardableResult
    public func sampleNow() -> Snapshot {
        queue.sync { tick() }
    }

    private func schedule() {
        let interval = speed.metrics
        // Leeway lets the system coalesce our wake-ups with others'.
        timer?.schedule(deadline: .now(), repeating: interval,
                        leeway: .milliseconds(Int(interval * 100)))
    }

    @discardableResult
    private func tick() -> Snapshot {
        let now = Clock.ms()
        let speed = self.speed

        if now >= nextHardware {
            if !cpuInfo.known {
                cpuInfo = CpuSensor.info()
            }
            adapters = guarded("network-hardware", budgetMs: 2000) { NetworkSensor.hardware() } ?? adapters
            nextHardware = now + Self.hardwareEveryMs
        }
        if now >= nextSpace || volumes.isEmpty {
            if let found = guarded("volumes", budgetMs: 2000, { DiskSensor.volumes() }) {
                volumes = found
                identifyDevices()
            }
            nextSpace = now + UInt64(speed.space * 1000)
        }

        var snapshot = Snapshot()
        snapshot.cpuInfo = cpuInfo

        if let ticks = CpuSensor.readTicks() {
            if let previous = previousCpu, previous.count == ticks.count {
                snapshot.cores = zip(previous, ticks).map { Core(usage: CpuMath.usage(from: $0, to: $1)) }
                snapshot.cpuTotal = CpuMath.total(from: previous, to: ticks)
                snapshot.ready = true
            }
            previousCpu = ticks
        }

        if let ram = MemorySensor.read() {
            lastRam = ram
        }
        snapshot.ram = lastRam
        snapshot.disks = disksWithRates()
        snapshot.adapters = connectedAdapters()

        lock.withLock { published = snapshot }
        onPublish?(snapshot)
        return snapshot
    }

    /// Look up the physical device for each newly mounted volume. BSD names
    /// are reused once a disk is ejected, so the caches keep only what is
    /// mounted now.
    private func identifyDevices() {
        let mounted = Set(volumes.map(\.bsdName))
        devices = devices.filter { mounted.contains($0.key) }
        noDevice = noDevice.intersection(mounted)

        for volume in volumes where !volume.isNetwork && !volume.bsdName.isEmpty {
            let bsd = volume.bsdName
            guard devices[bsd] == nil, !noDevice.contains(bsd) else { continue }
            if let device = guarded("device:\(bsd)", budgetMs: 1000, { BlockStorage.device(forBSDName: bsd) }) {
                devices[bsd] = device
            } else {
                noDevice.insert(bsd)
            }
        }
    }

    private func disksWithRates() -> [Disk] {
        let drivers = Set(devices.values.map(\.driverID)).subtracting([0])
        previousIo = previousIo.filter { drivers.contains($0.key) }

        var rates: [UInt64: (readMb: Double, writeMb: Double)] = [:]
        for driver in drivers {
            guard let counters = guarded("io:\(driver)", { BlockStorage.counters(driverID: driver) }) else {
                continue
            }
            let stamp = Clock.ms()
            if let previous = previousIo[driver] {
                rates[driver] = IoRate.perSecond(previous: previous.counters, previousStamp: previous.stamp,
                                                 current: counters, stamp: stamp)
            }
            previousIo[driver] = (counters, stamp)
        }

        return volumes.map { volume in
            var disk = volume
            disk.device = devices[volume.bsdName]
            if let driver = disk.device?.driverID, let rate = rates[driver] {
                disk.readMb = rate.readMb
                disk.writeMb = rate.writeMb
            }
            return disk
        }
    }

    private func connectedAdapters() -> [Adapter] {
        guard !adapters.isEmpty,
              let counters = guarded("network", { NetworkSensor.counters() }) else { return [] }
        let connected = NetworkSensor.connected()
        let stamp = Clock.ms()

        var result: [Adapter] = []
        for hardware in adapters {
            guard connected.contains(hardware.bsdName),
                  let reading = counters[hardware.bsdName], reading.running else { continue }
            var adapter = Adapter(id: hardware.bsdName)
            adapter.name = hardware.name
            adapter.wireless = hardware.wireless
            adapter.speedMbps = Double(reading.baudRate) / 1_000_000
            adapter.totalReceived = reading.received
            adapter.totalSent = reading.sent

            let current = (read: reading.received, written: reading.sent)
            if let previous = previousNet[hardware.bsdName] {
                let rate = IoRate.perSecond(previous: previous.counters, previousStamp: previous.stamp,
                                            current: current, stamp: stamp)
                adapter.downMb = rate.readMb
                adapter.upMb = rate.writeMb
            }
            previousNet[hardware.bsdName] = (current, stamp)
            result.append(adapter)
        }
        let present = Set(result.map(\.id))
        previousNet = previousNet.filter { present.contains($0.key) }
        return result
    }

    /// Run a probe unless it has been shelved, and shelve it if it was slow.
    /// A slow probe's result is still used: it is late, not wrong.
    private func guarded<T>(_ key: String, budgetMs: UInt64 = slowProbeMs, _ probe: () -> T?) -> T? {
        let started = Clock.ms()
        if let until = slowUntil[key], started < until {
            return nil
        }
        let result = probe()
        let finished = Clock.ms()
        if finished - started > budgetMs {
            slowUntil[key] = finished + Self.slowBackoffMs
        }
        return result
    }
}
