# SysMonitor for macOS

The macOS edition of the widget, in Swift, on the `Mac` branch. It is a
rewrite, not a port: WPF does not run on macOS, and the Windows build is bound
to Win32, WMI, PDH and IOCTLs at nearly every layer. What carried over is the
knowledge -- the snapshot shape, the history ring, per-device I/O baselines,
the sampling cadences, the slow-probe backoff.

Same rule as the Windows build: **no package dependencies.** Everything comes
from the system frameworks -- Darwin/Mach, IOKit, SystemConfiguration, and
SwiftUI/AppKit from phase 2.

Plan and decisions: `Requirements.md`, R-020 to R-025.

## Status

Phase 1 (R-021): the core. Sensors, snapshot, sampler, tests, and a probe that
prints what the sampler sees. No window yet.

## Building

Needs Xcode, not just the Command Line Tools: the tools alone ship neither
XCTest nor the swift-testing macro plugin, so `swift test` cannot build.

```
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

**Temperatures are nil** until phase 5 (R-023), never estimated from load.
