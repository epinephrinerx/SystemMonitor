# Handoff — R-030 phase A2: module property readers (plan §2b)

From: Coding Owner (Claude Code) · To: Writing Owner 2 (opencode, `opencode-go/deepseek-v4.1-flash`)
Branch: `linux-native` at `681c0c2` (clean tree). Date: 2026-11-21.
Requirement: **R-030**; plan `Docs/PLAN-R030-main-window.md` §2b (gap table).

**Scope: readers, model and probe only.** No UI: do **not** touch
`SysMonitor.Linux.App/` and do not edit `SysMonitor.Linux.Tests/`. The "now ·
Highest" peaks are a UI concern (phase B) — do not add them here.

**Golden rule:** every new field is nullable (or an empty string) and a value
that cannot be read stays null. **Never guess.** The UI shows N/A.

Values measured on this machine are given so you can check the probe.

## 1. CPU

- `Core` gains `int? Mhz` — `/sys/devices/system/cpu/cpu<N>/cpufreq/scaling_cur_freq`
  (kHz → MHz), every sample. (This machine: ~1202 MHz.)
- `CpuInfo` gains (read once at start):
  - `int? MaxMhz` — `cpuinfo_max_freq` of cpu0 (kHz → MHz; here 3600);
  - `string VirtualizationType` — `vmx` flag → `VT-x`, `svm` → `AMD-V`, else
    empty. Keep the existing `bool? Virtualization`;
  - `string Architecture` — `RuntimeInformation.OSArchitecture` mapped to
    `x86_64`/`aarch64`/… (here `x86_64`).
- `Snapshot` gains `TimeSpan? Uptime` — first field of `/proc/uptime`, every sample.

## 2. GPU (each card) — extend `Gpu`

| Field | Source |
|---|---|
| `string Manufacturer` | PCI vendor name from the existing `PciDatabase` (vendor id) |
| `string PciSlot` | the card's PCI address, normalised like `GpuReader.NormalisePci` (`0000:2b:00.0`) |
| `int? CoreClockMhz` | Intel: `card*/gt_act_freq_mhz`; AMD: the `*` line of `device/pp_dpm_sclk`; NVIDIA: `clocks.gr` |
| `int? MemoryClockMhz` | AMD: `*` line of `device/pp_dpm_mclk`; NVIDIA: `clocks.mem`; Intel: null |
| `double? PowerW` | AMD: hwmon `power1_average` (µW); NVIDIA: `power.draw`; Intel: null |
| `double? PowerCapW` | AMD: hwmon `power1_cap`; NVIDIA: `power.limit`; Intel: null |
| `string LinkCurrent`, `string LinkMax` | `device/current_link_speed`+`current_link_width`, `max_link_speed`+`max_link_width`; NVIDIA may use `pcie.link.gen.*`/`pcie.link.width.*` |
| `int? EncoderUsage`, `int? DecoderUsage` | NVIDIA `utilization.encoder`/`.decoder`; others null (their video engines already show in `Engines`) |

- **Link text:** GT/s → PCIe generation: 2.5→1.0, 5→2.0, 8→3.0, 16→4.0,
  32→5.0, 64→6.0. Format `PCIe 3.0 ×4`. `Unknown`, width 0 or 255, or a
  missing file → empty. (Here: Intel card1 → empty; card2 → current
  `PCIe 1.0 ×4`, max `PCIe 3.0 ×4`.)
- **NVIDIA:** add the extra fields to the **existing single** `nvidia-smi` query
  in `GpuReader` (same call, same cadence, only for awake cards — keep the
  runtime-PM rule). `[N/A]`, `[Not Supported]`, empty → null. Here power is
  `[N/A]` → null; clocks 139/405 MHz.
- Static fields (manufacturer, slot, max link) read once per card; dynamic ones
  every sample.

## 3. Disk (each physical drive) — extend `PhysicalDisk`

| Field | Source |
|---|---|
| `string DeviceName` | block device name (`nvme0n1`, `sda`) |
| `string Kind` | `NVMe` when the name starts with `nvme`; else `queue/rotational` 1 → `HDD`, 0 → `SSD`; unreadable → empty |
| `bool? Removable` | `removable` |
| `bool? Writable` | `ro` = 0 → true, 1 → false |
| `string Link` | NVMe: the PCI device's link, same text as GPU (`PCIe 3.0 ×4`); USB: walk up the device path to the first dir with `version` and `speed` → `USB 2.1 (480 Mb/s)` (trim the version's leading space/trailing zeros: `2.10` → `2.1`); else empty |
| `double? ActivityPercent` | Δ`io_ticks` (field 10 of `/sys/block/X/stat`, ms) ÷ Δt ms × 100, clamped 0–100; first sample null |
| `double ReadBytesPerSec`, `WriteBytesPerSec` | Δ sectors read (field 3) / written (field 7) × 512 ÷ Δt, per **physical** drive |
| `long? TotalReadBytes`, `TotalWrittenBytes` | sectors × 512 since boot |

- Reuse `DiskStats`/`BlockDevices` and the existing physical-disk mapping; do not
  add a second `/sys/block` scan per sample. The capacity already exists
  (`Bytes`).
- Here: nvme0n1 → NVMe, `PCIe 3.0 ×4`, removable false, writable true; sda →
  SSD, `USB 2.1 (480 Mb/s)`.

## 4. Network (each adapter) — extend `Adapter`

| Field | Source |
|---|---|
| `string Interface` | kernel name (`wlp0s20f3`) |
| `string Kind` | `Wi-Fi` when wireless, `Ethernet` for `type` 1 non-wireless, else empty |
| `string Driver` | if not already present: basename of `device/driver` link |
| `IReadOnlyList<string> Ipv4`, `Ipv6` | `System.Net.NetworkInformation.NetworkInterface` unicast addresses with prefix (`192.168.0.139/24`); link-local IPv6 included; no `ip` command |
| `double? LinkSpeedMbps` | wired: `/sys/class/net/X/speed`; `-1`/missing → null |
| `string Ssid` | Wi-Fi: `iw dev <if> link` (`SSID:` line) via `ICommandRunner`, 2 s timeout, every **10 s**, cached |
| `int? SignalDbm` | `iw` `signal:`; fallback `/proc/net/wireless` level column |
| `int? FrequencyMhz` | `iw` `freq:` (here 5220) |
| `double? TxBitrateMbps` | `iw` `tx bitrate:` (here 576.4) |

- **Privacy:** the MAC address is never read into the model. The **SSID is
  model-only**: the probe prints `SSID: (hidden)` when one exists, never its
  text. IPs may be printed by the probe.
- No `iw` (not installed / fails) → SSID/frequency/bitrate null, signal from
  `/proc/net/wireless` if present; log once to Diagnostics.

## 5. Probe

`SysMonitor.Linux.Probe` text and `--json` gain a **Properties** block per
module with every new field, printing `N/A` for null/empty. Keep the existing
output lines; append only. `--json` must also omit the SSID text (serialise
`"Ssid": "(hidden)"` or omit the property) and never a MAC.

## Acceptance

1. `dotnet build SysMonitor.Linux.App -c Release` — 0 warnings, 0 errors.
2. `dotnet test SysMonitor.Linux.Tests` — the existing 524 still pass.
3. The probe on this machine shows the values quoted above (CPU max 3600 MHz,
   VT-x, x86_64, uptime; card2 PCIe 1.0 ×4 / 3.0 ×4, clocks, power N/A; nvme
   NVMe/PCIe 3.0 ×4, sda SSD/USB 2.1 (480 Mb/s), activity %, totals; Wi-Fi
   5220 MHz, signal −65 dBm, IPv4 192.168.0.139/24, SSID hidden).
4. No new process spawned per sample except the 10 s `iw` call and the existing
   `nvidia-smi` call.

## Report back

Summary, files changed, commands run with results, deviations, risks. No commit.

## Amendment 1 (Coding Owner, 2026-11-21) — Reviewer2 round 1

1. **MAC inside interface names.** systemd's `enx<12 hex>` names embed the MAC
   (`enx00e04c68221a`). The model keeps the real name (the GUI on the user's own
   machine shows it, as Resources does), but **probe text and `--json`** must
   mask it: a name matching `^enx[0-9a-f]{12}$` (case-insensitive) is printed as
   `enx…` plus the last 0 characters, i.e. exactly `enx…`; when two adapters
   would collide, append `#1`, `#2`. Apply to every probe field that carries the
   name (`Id`, `Name`, `Interface`, and any description that contains it).
   Add a small pure helper, e.g. `Privacy.MaskInterface(string)` in
   `SysMonitor.Linux` so tests can reach it.
2. **Missing `io_ticks` is unknown.** `DiskCounters.IoMs` becomes `long?`
   (null when the field is absent/invalid); `ActivityPercent` is null unless both
   samples have it. Likewise a counter that goes backwards (reset) → null for
   that sample, not 0 or negative.
