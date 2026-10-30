import Foundation
import SysMonitorCore

/// The user's settings. A port of the Windows build's `AppConfig`, without
/// the fields that have no Mac meaning yet (GPU, updates, autostart).
///
/// Every field decodes on its own with its default as the fallback, so a file
/// written by an older build, or edited by hand, loses only the fields that
/// are wrong rather than every setting at once.
public struct AppConfig: Codable, Equatable, Sendable {
    public var lang = "en"
    public var theme = "light"
    public var alwaysOnTop = false
    public var snap = true
    public var opacity = 1.0
    public var fontScale = 1.0

    public var showCpu = true
    public var showRam = true
    public var showDisk = true
    public var showGpu = true
    public var showNetwork = true
    public var cpuMode = "separated"        // separated | total
    public var diskMode = "separated"
    public var networkMode = "separated"
    public var includeNetworkDrives = true
    /// Off means the private temperature interface is never called (R-030).
    public var readTemperatures = true

    public var closeAction = "tray"         // tray | exit
    public var speed = "balanced"           // eco | balanced | fast

    /// Each view keeps its own place on screen (R-010). AppKit coordinates:
    /// the origin is the bottom-left corner.
    public var widgetX: Double?
    public var widgetY: Double?
    public var widgetW = 270.0
    public var widgetH = 104.0
    public var overallX: Double?
    public var overallY: Double?
    public var overallW = 630.0
    public var overallH = 480.0
    /// Zero until the Full view has been opened: it then fills the screen.
    public var fullX: Double?
    public var fullY: Double?
    public var fullW = 0.0
    public var fullH = 0.0
    public var fullTab = ""

    public static let widgetSize = CGSize(width: 270, height: 104)
    public static let widgetMin = CGSize(width: 200, height: 64)
    public static let widgetMax = CGSize(width: 900, height: 400)
    public static let overallSize = CGSize(width: 630, height: 480)
    public static let overallMin = CGSize(width: 470, height: 300)
    public static let overallMax = CGSize(width: 4000, height: 3000)
    public static let fullMin = CGSize(width: 640, height: 480)
    public static let fullMax = CGSize(width: 8000, height: 5000)

    public init() {}

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        func read<T: Decodable>(_ key: CodingKeys, _ fallback: T) -> T {
            ((try? c.decodeIfPresent(T.self, forKey: key)) ?? nil) ?? fallback
        }
        let d = AppConfig()
        lang = read(.lang, d.lang)
        theme = read(.theme, d.theme)
        alwaysOnTop = read(.alwaysOnTop, d.alwaysOnTop)
        snap = read(.snap, d.snap)
        opacity = read(.opacity, d.opacity)
        fontScale = read(.fontScale, d.fontScale)
        showCpu = read(.showCpu, d.showCpu)
        showRam = read(.showRam, d.showRam)
        showDisk = read(.showDisk, d.showDisk)
        showGpu = read(.showGpu, d.showGpu)
        showNetwork = read(.showNetwork, d.showNetwork)
        cpuMode = read(.cpuMode, d.cpuMode)
        diskMode = read(.diskMode, d.diskMode)
        networkMode = read(.networkMode, d.networkMode)
        includeNetworkDrives = read(.includeNetworkDrives, d.includeNetworkDrives)
        readTemperatures = read(.readTemperatures, d.readTemperatures)
        closeAction = read(.closeAction, d.closeAction)
        speed = read(.speed, d.speed)
        widgetX = read(.widgetX, d.widgetX)
        widgetY = read(.widgetY, d.widgetY)
        widgetW = read(.widgetW, d.widgetW)
        widgetH = read(.widgetH, d.widgetH)
        overallX = read(.overallX, d.overallX)
        overallY = read(.overallY, d.overallY)
        overallW = read(.overallW, d.overallW)
        overallH = read(.overallH, d.overallH)
        fullX = read(.fullX, d.fullX)
        fullY = read(.fullY, d.fullY)
        fullW = read(.fullW, d.fullW)
        fullH = read(.fullH, d.fullH)
        fullTab = read(.fullTab, d.fullTab)
        normalize()
    }

    /// Bring every field back inside what the app can show.
    public mutating func normalize() {
        let d = AppConfig()
        func oneOf(_ value: String, _ allowed: [String], _ fallback: String) -> String {
            allowed.contains(value) ? value : fallback
        }
        lang = oneOf(lang, ["en", "th"], d.lang)
        theme = oneOf(theme, ["light", "dark"], d.theme)
        cpuMode = oneOf(cpuMode, ["separated", "total"], d.cpuMode)
        diskMode = oneOf(diskMode, ["separated", "total"], d.diskMode)
        networkMode = oneOf(networkMode, ["separated", "total"], d.networkMode)
        closeAction = oneOf(closeAction, ["tray", "exit"], d.closeAction)
        speed = oneOf(speed, RefreshSpeed.allCases.map(\.rawValue), d.speed)

        opacity = Self.clamp(opacity, 0.35, 1, d.opacity)
        fontScale = Self.clamp(fontScale, 0.8, 1.6, d.fontScale)
        widgetW = Self.clamp(widgetW, Self.widgetMin.width, Self.widgetMax.width, d.widgetW)
        widgetH = Self.clamp(widgetH, Self.widgetMin.height, Self.widgetMax.height, d.widgetH)
        overallW = Self.clamp(overallW, Self.overallMin.width, Self.overallMax.width, d.overallW)
        overallH = Self.clamp(overallH, Self.overallMin.height, Self.overallMax.height, d.overallH)
        if fullW != 0 || fullH != 0 {
            fullW = Self.clamp(fullW, Self.fullMin.width, Self.fullMax.width, d.fullW)
            fullH = Self.clamp(fullH, Self.fullMin.height, Self.fullMax.height, d.fullH)
        }
        for keyPath in [\AppConfig.widgetX, \.widgetY, \.overallX, \.overallY, \.fullX, \.fullY] where !(self[keyPath: keyPath]?.isFinite ?? true) {
            self[keyPath: keyPath] = nil
        }
    }

    public var refreshSpeed: RefreshSpeed { RefreshSpeed(rawValue: speed) ?? .balanced }
    public var isDark: Bool { theme == "dark" }

    private static func clamp(_ value: Double, _ low: Double, _ high: Double, _ fallback: Double) -> Double {
        value.isFinite ? min(max(value, low), high) : fallback
    }
}

/// Where the config lives, and reading and writing it.
public final class ConfigStore: @unchecked Sendable {
    public let url: URL

    public init(url: URL = ConfigStore.defaultURL) {
        self.url = url
    }

    public static var defaultURL: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("SysMonitor", isDirectory: true)
            .appendingPathComponent("config.json")
    }

    /// The saved settings, or the defaults. A file that will not parse is
    /// kept beside the new one as `.bad` rather than silently overwritten.
    public func load() -> AppConfig {
        guard let data = try? Data(contentsOf: url) else { return AppConfig() }
        do {
            return try JSONDecoder().decode(AppConfig.self, from: data)
        } catch {
            Diag.write("Config unreadable, starting from defaults: \(error.localizedDescription)")
            let bad = url.appendingPathExtension("bad")
            try? FileManager.default.removeItem(at: bad)
            try? FileManager.default.copyItem(at: url, to: bad)
            return AppConfig()
        }
    }

    public func save(_ config: AppConfig) {
        do {
            try FileManager.default.createDirectory(at: url.deletingLastPathComponent(),
                                                    withIntermediateDirectories: true)
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            try encoder.encode(config).write(to: url, options: .atomic)
        } catch {
            Diag.write("Config not saved: \(error.localizedDescription)")
        }
    }
}
