// swift-tools-version:5.9
import PackageDescription

// No package dependencies: the same rule the Windows build keeps. Everything
// comes from the system frameworks.
let package = Package(
    name: "SysMonitor",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "SysMonitor", targets: ["SysMonitor"]),
        .executable(name: "sysmon-probe", targets: ["sysmon-probe"]),
        .library(name: "SysMonitorCore", targets: ["SysMonitorCore"]),
    ],
    targets: [
        .target(
            name: "SysMonitorCore",
            linkerSettings: [
                .linkedFramework("IOKit"),
                .linkedFramework("SystemConfiguration"),
            ]
        ),
        // Everything with a window in it, and the logic behind it: config,
        // palette, strings, widget pages, window geometry, the views.
        .target(
            name: "SysMonitorUI",
            dependencies: ["SysMonitorCore"]
        ),
        .executableTarget(
            name: "SysMonitor",
            dependencies: ["SysMonitorUI"]
        ),
        .executableTarget(
            name: "sysmon-probe",
            dependencies: ["SysMonitorCore"]
        ),
        .testTarget(
            name: "SysMonitorCoreTests",
            dependencies: ["SysMonitorCore"]
        ),
        .testTarget(
            name: "SysMonitorUITests",
            dependencies: ["SysMonitorUI"]
        ),
    ]
)
