import Darwin
import Foundation
import Testing
@testable import SysMonitorCore

struct ProcessSensorTests {
    @Test func listsThisProcessWithItsMemory() throws {
        let me = try #require(ProcessSensor.read().first { $0.pid == getpid() })
        #expect((me.footprint ?? 0) > 0)
        #expect(me.uid == getuid())
        #expect(!me.path.isEmpty)
    }

    @Test func launchdIsListedButNotReadable() throws {
        // The tests run as a normal user; root's processes are named, not read.
        let launchd = try #require(ProcessSensor.read().first { $0.pid == 1 })
        #expect(launchd.name == "launchd")
        #expect(launchd.footprint == nil)
    }
}

struct ProcessControlTests {
    private func entry(pid: Int32 = 4242, name: String = "app", uid: UInt32 = getuid()) -> ProcessEntry {
        ProcessEntry(pid: pid, name: name, uid: uid, footprint: 1)
    }

    @Test func theSystemThisAppAndOtherUsersAreProtected() {
        #expect(ProcessControl.isProtected(entry(pid: 0)))
        #expect(ProcessControl.isProtected(entry(pid: 1)))
        #expect(ProcessControl.isProtected(entry(pid: getpid())))
        #expect(ProcessControl.isProtected(entry(uid: getuid() + 1)))
        #expect(ProcessControl.isProtected(entry(name: "WindowServer")))
        #expect(!ProcessControl.isProtected(entry()))
        #expect(ProcessControl.end(entry(pid: 1, name: "launchd")) == .protected)
    }

    @Test func endingAProcessOfOurOwnEndsIt() throws {
        let child = Process()
        child.executableURL = URL(fileURLWithPath: "/bin/sleep")
        child.arguments = ["30"]
        try child.run()
        defer { if child.isRunning { child.terminate() } }

        let seen = try #require(ProcessSensor.read().first { $0.pid == child.processIdentifier })
        #expect(seen.name == "sleep")
        #expect(ProcessControl.end(seen) == .sent)
        child.waitUntilExit()
        #expect(child.terminationReason == .uncaughtSignal)
        #expect(child.terminationStatus == SIGTERM)
    }

    @Test func aReusedPidIsNotEnded() throws {
        let child = Process()
        child.executableURL = URL(fileURLWithPath: "/bin/sleep")
        child.arguments = ["30"]
        try child.run()
        defer { child.terminate(); child.waitUntilExit() }

        // The list said this pid was something else: leave it alone.
        let stale = ProcessEntry(pid: child.processIdentifier, name: "Safari", uid: getuid(), footprint: 1)
        #expect(ProcessControl.end(stale) == .gone)
        #expect(child.isRunning)
    }
}

struct LaunchAgentTests {
    @Test func programOrItsArguments() {
        let a = LaunchAgents.parse(["Label": "com.example.a", "Program": "/usr/bin/a", "RunAtLoad": true],
                                   file: "/x/com.example.a.plist")
        #expect(a.label == "com.example.a" && a.program == "/usr/bin/a" && a.runAtLoad && !a.keepAlive)
        let b = LaunchAgents.parse(["ProgramArguments": ["/usr/bin/b", "--flag"],
                                    "KeepAlive": ["SuccessfulExit": false], "Disabled": true],
                                   file: "/x/com.example.b.plist")
        #expect(b.label == "com.example.b")
        #expect(b.program == "/usr/bin/b" && b.keepAlive && b.disabled)
    }

    @Test func aBrokenFileIsListedNotDropped() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("agents-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let good = try PropertyListSerialization.data(fromPropertyList: ["Label": "b.good", "Program": "/p"],
                                                      format: .xml, options: 0)
        try good.write(to: dir.appendingPathComponent("b.good.plist"))
        try Data("not a plist".utf8).write(to: dir.appendingPathComponent("a.broken.plist"))
        try Data().write(to: dir.appendingPathComponent("notes.txt"))

        let agents = LaunchAgents.read(dir)
        #expect(agents.map(\.label) == ["a.broken", "b.good"])
        #expect(!agents[0].readable && agents[1].readable)
    }
}
