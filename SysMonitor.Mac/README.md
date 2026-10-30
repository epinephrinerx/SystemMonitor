# SysMonitor for macOS

The macOS edition of the widget, in Swift, on the `Mac` branch. It is a
rewrite, not a port: WPF does not run on macOS, and the Windows build is bound
to Win32, WMI, PDH and IOCTLs at nearly every layer. What carried over is the
knowledge -- the snapshot shape, the history ring, per-device I/O baselines,
the sampling cadences, the slow-probe backoff.

Same rule as the Windows build: **no package dependencies.** Everything comes
from the system frameworks -- Darwin/Mach, IOKit, SystemConfiguration, and
SwiftUI/AppKit from phase 2.

Plan and decisions: `Requirements.md`, R-020 to R-030.

## Status

- Phase 1 (R-021): the core. Sensors, snapshot, sampler, and a probe that
  prints what the sampler sees.
- Phase 2 (R-026): the mini widget, the Overall view with its sidebar, the
  Settings window, the menu bar item, config and log, and `build-app.sh`.
- GPU (R-027): `IOAccelerator` statistics -- device, renderer and tiler use and
  memory in use -- on the widget, the Overall view and the Full view.
- Phase 3 (R-028): the Full view. A rail of devices down the left, each with
  its facts and history graphs; three minutes of history kept for every
  series whatever view is showing.
- Phase 4 (R-029): the Memory tab's process list -- grouped by name, searchable,
  with SIGTERM behind a confirmation -- the user's LaunchAgents, read only, and
  Open at login through `SMAppService`.
- Phase 5 (R-030): real temperatures on Apple Silicon, the app icon, and the
  `.dmg`.

## Building

Needs Xcode, not just the Command Line Tools: the tools alone ship neither
XCTest nor the swift-testing macro plugin, so `swift test` cannot build.

```
./build-app.sh                               # tests, then dist/SysMonitor.app and .dmg
open dist/SysMonitor.app
swift build
swift test                                   # passive: reads counters, makes no load
swift run sysmon-probe                       # one snapshot
swift run sysmon-probe --seconds 60          # the sampler for a minute, a line a tick
swift build -c release --arch arm64 --arch x86_64    # universal, macOS 13+
```

The universal build warns that x86_64 is deprecated: macOS 27 is the last
release for Intel Macs. It still builds and runs; R-022 asked for both.

## Where the numbers come from

| | |
|---|---|
| CPU per core | `host_processor_info(PROCESSOR_CPU_LOAD_INFO)` tick deltas |
| CPU identity | `machdep.cpu.brand_string`, `hw.perflevel{0,1}.logicalcpu` |
| Memory | `host_statistics64(HOST_VM_INFO64)`, `hw.memsize`, `vm.swapusage`, `kern.memorystatus_vm_pressure_level` |
| Volumes | `mountedVolumeURLs(.skipHiddenVolumes)`, `statfs` for the BSD name and filesystem |
| Physical disk, I/O | IOKit: up the service plane to `IOBlockStorageDriver` |
| Network | SystemConfiguration for which interfaces are adapters, `NET_RT_IFLIST2` for counters |

## Things that are easy to get wrong

**Memory used is app + wired + compressed**, the way Activity Monitor adds it.
File cache is left out: the kernel hands it back the moment anything asks, so
counting it would make every Mac look full. App memory is anonymous pages less
purgeable ones.

**Units follow the Mac, not the Windows build.** Memory is in binary gigabytes,
which is what Activity Monitor shows. Disks are in decimal gigabytes, which is
what Finder shows, and free space includes purgeable space for the same
reason. A 256 GB SSD reads 245 GB here and in Finder, not 228.

**Disk I/O is per physical device, not per volume.** macOS keeps the counters
on the block storage driver. Two volumes on one disk show the same rate, and
`Disk.device.id` says which disk that is.

**An APFS volume is several layers above its disk.** The walk runs volume ->
synthesized container disk (`disk3`) -> container scheme -> physical partition
-> whole physical disk (`disk0`) -> driver. The last whole disk passed on the
way up is the physical one; stopping at the first gives the synthesized
container, which has no model, no media type, and no counters.

**Network counters must be 64-bit.** `getifaddrs` hands out `if_data` with
32-bit byte counts, which wrap at 4 GB -- minutes on a fast link. The routing
socket's `if_data64` does not.

**Up-and-running is not connected.** The Thunderbolt ports sit up as members
of `bridge0` with nothing on the other end. An adapter is shown only when it
holds an IPv4 or a non-link-local IPv6 address.

**CPU tick counters are 32-bit** and wrap; the deltas use wrapping subtraction.
The core order is the kernel's -- on M1 that puts the efficiency cores first --
so the snapshot reports how many of each kind there are but does not label
individual cores.

**Temperatures come from private API, by the user's choice (R-023).** Apple
Silicon's sensors are read through the IOHID event system, declared in
`TemperatureSensor` by symbol name. CPU is the hottest core-cluster sensor
(`pACC`/`eACC MTR`), falling back to the die sensors and then the SoC's; GPU
the hottest `GPU MTR`; the internal SSD the hottest `NAND`. Readings outside
1-130 °C are dropped -- the development M1 has several at -22 °C. If a later
macOS closes the interface, every reading is nil and the UI says `n/a`.
Nothing is ever estimated from load. Intel Macs keep their sensors behind the
SMC and get `n/a`. The sidebar's "Read real temperatures" switch stops the
private calls entirely. The system's thermal state is public API and always
shown.

## The app

`SysMonitorUI` holds everything with a window in it and the logic behind it;
the `SysMonitor` executable is only the entry point.

| | |
|---|---|
| Config | `~/Library/Application Support/SysMonitor/config.json` |
| Log | `~/Library/Logs/SysMonitor/sysmonitor.log`, one megabyte then rolled to `.1` |
| Bundle | `dist/SysMonitor.app`, `LSUIElement`, ad-hoc signed |

**No Dock icon, and a menu bar item that is always there.** A widget that sat
in the Dock as well would be two of everything. The menu bar item is how a
hidden widget comes back, and its Quit always quits, whatever the close button
is set to do.

**The panels are borderless, and move and resize themselves.** AppKit's
`contentAspectRatio` keeps a shape but decides for itself which side moves,
and the Windows build's rules (a side drag grows downward, a top drag grows
about the centre, nothing leaves the screen) are specific. So grab zones
along the edges call `WindowGeometry`, which computes every frame from the one the drag started
on. Moving follows the pointer's screen position rather than the gesture's
translation, because the view the gesture is measured in is the thing
moving. `NSHostingView.sizingOptions` is emptied, or the hosting view sizes
the window to its content and fights every resize.

**Sizes are multiplied out, not scaled.** The Windows build scaled whole views
with a layout transform. A SwiftUI scale effect stretches the rendered pixels,
which would soften the text exactly when someone enlarges the window to read
it. `Metrics` carries
the window's scale and the font setting, and every font size and length is
computed from them.

**`ImageRenderer` draws nothing inside a `ScrollView`.** The render tests set
`scrollable = false` on the Overall view to lay its content out flat. Set
`SYSMON_RENDER_DIR` to keep a PNG of every render:

```
SYSMON_RENDER_DIR=/tmp/renders swift test --filter RenderTests
```

**The light theme's sidebar and card colours are five levels apart.** A pixel
test with a looser tolerance counted the sidebar as cards and passed with no
cards drawn at all.

**History is pushed on every snapshot, whatever view is open.** Opening the
Full view shows the last three minutes rather than an empty box. A device that
disappears takes its history with it, so an ejected drive does not leave a
series growing nowhere.

**Graphs are one `Canvas` each.** Seventy-two points as seventy-two views would
cost more than the rest of the window. The canvas reads `History` objects that
do not change identity, so each chart is also handed the model's
`historyRevision`; without it SwiftUI sees nothing new and keeps the old
drawing.

**A string table with a duplicated key crashes at launch.** Swift traps on a
dictionary literal with a repeated key. `wired` meant both Ethernet and wired
memory for one build; the compiler warned, and the memory one is now
`wired_memory`. Treat that warning as an error.

**Only this user's processes can be read.** `proc_pid_rusage` refuses root's
and other users' processes to a normal user -- 194 of 552 on the development
machine. They are listed by name from `KERN_PROC_ALL`, marked as unreadable,
and padlocked. There is no elevation: macOS has no one-job equivalent of the
Windows build's UAC copy without installing a privileged helper.

**A pid is checked again before it is ended.** Pids are reused, so the name
is read once more at the moment of `kill` and the process is left alone if it
no longer matches what the user was shown. SIGTERM, never SIGKILL.

**LaunchAgents are read from their plists, not from `launchctl`.** No
subprocess, and nothing that could change launchd's state. Running means a
process whose executable path is the agent's program.

**Open at login lives in the system, not the config.** The user can switch it
off in System Settings, and a copy in our config would then be wrong.

**The icon is drawn, not converted.** The Windows `SysMonitor.ico` stops at
256 px; macOS wants 1024. `tools/make-icon.swift` draws the same tile and bars
at 1024 and `build-app.sh` cuts the `.icns` sizes from it.

**`hdiutil create` is deprecated on macOS 27.** The build uses
`diskutil image create from` where it exists and falls back to `hdiutil`
where it does not.
