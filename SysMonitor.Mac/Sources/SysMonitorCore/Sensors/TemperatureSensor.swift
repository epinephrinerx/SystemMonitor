import Foundation

/// What the temperature sensors say, picked down to the three figures shown.
public struct Temperatures: Equatable, Sendable {
    public var cpu: Int?
    public var gpu: Int?
    /// The internal SSD's flash.
    public var nand: Int?

    public init(cpu: Int? = nil, gpu: Int? = nil, nand: Int? = nil) {
        self.cpu = cpu
        self.gpu = gpu
        self.nand = nand
    }
}

/// Apple Silicon temperature sensors, through the IOHID event system (R-023).
///
/// **This is private API.** macOS has no public way to read a die
/// temperature; the user chose to read the real one through the same
/// interface the system's own tools use, knowing a later macOS may close it.
/// Every call is declared here by symbol name, and every failure -- a missing
/// symbol's null, no services, no event -- comes back as no reading, which the
/// UI shows as `n/a`. Nothing is ever estimated.
///
/// Intel Macs keep their sensors behind the SMC instead; they get `n/a`.
package enum TemperatureSensor {
    /// A reading outside this is a sensor that is off or not wired up: the
    /// development machine reports several at -22 °C.
    package static let plausible = 1.0...130.0

    package static func read() -> Temperatures {
        #if arch(arm64)
        return pick(readings())
        #else
        return Temperatures()
        #endif
    }

    /// The hottest of each kind, because that is the one worth warning about.
    /// CPU from the core clusters' sensors, falling back to the die sensors,
    /// then the SoC's: names differ between chip generations.
    package static func pick(_ readings: [(name: String, celsius: Double)]) -> Temperatures {
        let valid = readings.filter { plausible.contains($0.celsius) }
        func hottest(_ match: (String) -> Bool) -> Int? {
            valid.filter { match($0.name) }.map(\.celsius).max().map { Int($0.rounded()) }
        }
        let cpu = hottest { $0.contains("ACC MTR") }
            ?? hottest { $0.contains("tdie") }
            ?? hottest { $0.hasPrefix("SOC") }
        return Temperatures(cpu: cpu, gpu: hottest { $0.hasPrefix("GPU") }, nand: hottest { $0.hasPrefix("NAND") })
    }

    #if arch(arm64)
    private typealias Client = OpaquePointer
    private typealias Service = OpaquePointer
    private typealias Event = OpaquePointer

    @_silgen_name("IOHIDEventSystemClientCreate")
    private static func clientCreate(_ allocator: CFAllocator?) -> Client?
    @_silgen_name("IOHIDEventSystemClientSetMatching")
    private static func setMatching(_ client: Client, _ matching: CFDictionary) -> Int32
    @_silgen_name("IOHIDEventSystemClientCopyServices")
    private static func copyServices(_ client: Client) -> Unmanaged<CFArray>?
    @_silgen_name("IOHIDServiceClientCopyProperty")
    private static func copyProperty(_ service: Service, _ key: CFString) -> Unmanaged<CFTypeRef>?
    @_silgen_name("IOHIDServiceClientCopyEvent")
    private static func copyEvent(_ service: Service, _ type: Int64, _ options: Int32, _ timestamp: Int64) -> Unmanaged<CFTypeRef>?
    @_silgen_name("IOHIDEventGetFloatValue")
    private static func floatValue(_ event: Event, _ field: Int32) -> Double

    private static let temperatureEvent: Int64 = 15
    private static let temperatureField: Int32 = 15 << 16

    /// One client for the life of the app: creating it is the expensive part.
    nonisolated(unsafe) private static var client: Client? = {
        guard let client = clientCreate(kCFAllocatorDefault) else { return nil }
        // Apple's vendor usage page, temperature sensors.
        _ = setMatching(client, ["PrimaryUsagePage": 0xff00, "PrimaryUsage": 5] as CFDictionary)
        return client
    }()

    package static func readings() -> [(name: String, celsius: Double)] {
        guard let client, let services = copyServices(client)?.takeRetainedValue() as? [AnyObject] else { return [] }
        var result: [(name: String, celsius: Double)] = []
        for object in services {
            let service = OpaquePointer(Unmanaged.passUnretained(object).toOpaque())
            guard let name = copyProperty(service, "Product" as CFString)?.takeRetainedValue() as? String,
                  let event = copyEvent(service, temperatureEvent, 0, 0)?.takeRetainedValue() else { continue }
            let value = withExtendedLifetime(event) {
                floatValue(OpaquePointer(Unmanaged.passUnretained(event).toOpaque()), temperatureField)
            }
            result.append((name, value))
        }
        return result
    }
    #else
    package static func readings() -> [(name: String, celsius: Double)] { [] }
    #endif
}

public enum ThermalState: String, Sendable {
    case nominal, fair, serious, critical

    /// The system's own judgement, public API, on every Mac.
    public static var current: ThermalState {
        switch ProcessInfo.processInfo.thermalState {
        case .nominal: return .nominal
        case .fair: return .fair
        case .serious: return .serious
        case .critical: return .critical
        @unknown default: return .nominal
        }
    }
}
