// swift-tools-version:5.9
import PackageDescription

// No package dependencies: the same rule the Windows build keeps. Everything
// comes from the system frameworks.
let package = Package(
    name: "SysMonitor",
    platforms: [.macOS(.v13)],
    products: [
        .library(name: "SysMonitorCore", targets: ["SysMonitorCore"]),
        .executable(name: "sysmon-probe", targets: ["sysmon-probe"]),
    ],
    targets: [
        .target(
            name: "SysMonitorCore",
            linkerSettings: [
                .linkedFramework("IOKit"),
                .linkedFramework("SystemConfiguration"),
            ]
        ),
        .executableTarget(
            name: "sysmon-probe",
            dependencies: ["SysMonitorCore"]
        ),
        .testTarget(
            name: "SysMonitorCoreTests",
            dependencies: ["SysMonitorCore"]
        ),
    ]
)
