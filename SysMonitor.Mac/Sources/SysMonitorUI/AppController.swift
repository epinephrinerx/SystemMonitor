import AppKit
import Combine
import SysMonitorCore

/// Wires the sampler, the model and the windows together for the life of
/// the app.
@MainActor
public final class AppController: NSObject, NSApplicationDelegate {
    /// How often the log records memory, so a leak would show as a trend.
    private static let heartbeatEvery: TimeInterval = 10 * 60

    private let store = ConfigStore()
    private var model: AppModel!
    private var sampler: Sampler!
    private var windows: WindowManager!
    private var timers: [Timer] = []
    private var observers = Set<AnyCancellable>()

    public override init() {
        super.init()
    }

    public func applicationDidFinishLaunching(_ notification: Notification) {
        let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "dev"
        Diag.write("Start \(version) on macOS \(ProcessInfo.processInfo.operatingSystemVersionString)")

        model = AppModel(config: store.load(), store: store)
        windows = WindowManager(model: model)
        sampler = Sampler(speed: model.config.refreshSpeed)
        sampler.onPublish = { [weak self] snapshot in
            Task { @MainActor in self?.receive(snapshot) }
        }

        model.$config
            .map(\.refreshSpeed)
            .removeDuplicates()
            .sink { [weak self] speed in self?.sampler.speed = speed }
            .store(in: &observers)

        windows.start()
        sampler.start()

        // The widget's rotation is checked twice a second; nothing redraws
        // unless the page actually changes.
        timers.append(Timer.scheduledTimer(withTimeInterval: 0.5, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.model.rotateIfDue() }
        })
        timers.append(Timer.scheduledTimer(withTimeInterval: Self.heartbeatEvery, repeats: true) { _ in
            Diag.write("Heartbeat footprint \(Self.footprintMb()) MB")
        })
    }

    public func applicationWillTerminate(_ notification: Notification) {
        timers.forEach { $0.invalidate() }
        sampler.stop()
        windows.remember()
        model.saveNow()
        Diag.write("Exit")
    }

    /// The app keeps running with every window hidden: the menu bar item is
    /// how it comes back.
    public func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        false
    }

    private func receive(_ snapshot: Snapshot) {
        model.receive(snapshot)
        windows.updateTooltip()
    }

    /// What Activity Monitor calls Memory: the process's physical footprint.
    nonisolated private static func footprintMb() -> String {
        var info = task_vm_info_data_t()
        var count = mach_msg_type_number_t(MemoryLayout<task_vm_info_data_t>.size / MemoryLayout<integer_t>.size)
        let result = withUnsafeMutablePointer(to: &info) {
            $0.withMemoryRebound(to: integer_t.self, capacity: Int(count)) {
                task_info(mach_task_self_, task_flavor_t(TASK_VM_INFO), $0, &count)
            }
        }
        guard result == KERN_SUCCESS else { return "?" }
        return String(format: "%.1f", Double(info.phys_footprint) / 1_048_576)
    }
}
