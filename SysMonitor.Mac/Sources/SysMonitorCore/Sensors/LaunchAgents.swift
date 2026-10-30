import Foundation

/// A user LaunchAgent, read from its plist. Shown, never changed (R-024).
public struct LaunchAgent: Sendable, Equatable {
    public var label: String
    public var file: String
    /// The executable: `Program`, or the first of `ProgramArguments`.
    public var program = ""
    public var runAtLoad = false
    /// `KeepAlive` can be a bool or a dictionary of conditions; either way,
    /// launchd restarts it.
    public var keepAlive = false
    public var disabled = false
    /// False for a file that would not parse.
    public var readable = true

    public init(label: String, file: String) {
        self.label = label
        self.file = file
    }
}

/// Reads `~/Library/LaunchAgents` directly rather than asking `launchctl`:
/// no subprocess, and nothing that could change launchd's state.
package enum LaunchAgents {
    package static var userDirectory: URL {
        FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/LaunchAgents")
    }

    package static func read(_ directory: URL = userDirectory) -> [LaunchAgent] {
        let files = (try? FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil)) ?? []
        return files
            .filter { $0.pathExtension == "plist" }
            .map { url in
                guard let data = try? Data(contentsOf: url),
                      let plist = try? PropertyListSerialization.propertyList(from: data, format: nil),
                      let dict = plist as? [String: Any] else {
                    var broken = LaunchAgent(label: url.deletingPathExtension().lastPathComponent, file: url.path)
                    broken.readable = false
                    return broken
                }
                return parse(dict, file: url.path)
            }
            .sorted { $0.label.localizedCaseInsensitiveCompare($1.label) == .orderedAscending }
    }

    package static func parse(_ dict: [String: Any], file: String) -> LaunchAgent {
        let fallback = URL(fileURLWithPath: file).deletingPathExtension().lastPathComponent
        var agent = LaunchAgent(label: dict["Label"] as? String ?? fallback, file: file)
        agent.program = dict["Program"] as? String ?? (dict["ProgramArguments"] as? [String])?.first ?? ""
        agent.runAtLoad = dict["RunAtLoad"] as? Bool ?? false
        agent.keepAlive = (dict["KeepAlive"] as? Bool) ?? (dict["KeepAlive"] is [String: Any])
        agent.disabled = dict["Disabled"] as? Bool ?? false
        return agent
    }
}
