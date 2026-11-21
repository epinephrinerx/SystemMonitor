# Handoff — R-030 phase A2 tests

From: Coding Owner · To: Test Author (agy, Gemini 3.8 Flash)

Add MSTest tests for `Docs/HANDOFF-R030-phaseA2.md` (read its "Deviations"
as reported by WO2: sda with `rotational=1` is HDD; io_ticks comes from
`/proc/diskstats` field 13 (index 12); `PciSlot` keeps the 4-digit domain;
`WifiSpeed` unchanged, `Wifi(name, stamp)` is the 10 s shared call; JSON is flat).
Read first: `SysMonitor.Core/Model/Snapshot.cs`, `SysMonitor.Linux/Sensors/CpuInfoReader.cs`,
`GpuReader.cs`, `BlockDevices.cs`, `DiskStats.cs`, `DiskReader.cs`,
`NetworkReader.cs`, `SysMonitor.Linux/Sampling/LinuxSampler.cs`, and the existing
tests for those classes (extend with **new test files** only, e.g.
`CpuPropertiesTests.cs`, `GpuPropertiesTests.cs`, `DiskPropertiesTests.cs`,
`NetworkPropertiesTests.cs`). Use `FakeRoot` and `FakeCommandRunner`.
You have no shell; the Coding Owner runs `dotnet test`.

Cover:
- **CPU:** scaling_cur_freq kHz → MHz per core, missing → null; cpuinfo_max_freq;
  `vmx` → VT-x, `svm` → AMD-V, neither → empty; `/proc/uptime` parse, garbage → null.
- **GPU link text:** 2.5/5/8/16/32/64 GT/s → PCIe 1.0…6.0; `Unknown`, width 0,
  width 255, missing file → empty; format `PCIe 3.0 ×4`.
- **AMD:** `pp_dpm_sclk`/`pp_dpm_mclk` with the `*` line (and none starred →
  null); hwmon `power1_average`/`power1_cap` µW → W.
- **Intel:** `gt_act_freq_mhz`; memory clock/power null.
- **NVIDIA:** a `nvidia-smi` fixture with the extended columns, including
  `[N/A]` and `[Not Supported]` → null; still **one** call per tick; an asleep
  card is not queried (existing runtime-PM rule).
- **Disk:** Kind (nvme name → NVMe; rotational 1 → HDD, 0 → SSD, missing →
  empty); removable/ro flags; NVMe PCIe link; USB link by walking ancestry
  (`2.10` → `USB 2.1 (480 Mb/s)`, `3.00`/`5000` → `USB 3 (5000 Mb/s)` or whatever
  `TrimVersion` defines — assert the handoff rule: trailing zeros trimmed);
  activity % from two diskstats samples with a fake clock, clamped at 100, first
  sample null; read/write bytes/s and lifetime totals (sectors × 512).
- **Network:** `iw dev X link` fixture → SSID, freq, signal, tx bitrate;
  `Not connected.` → nulls; `iw` failing → nulls + `/proc/net/wireless` level
  fallback; 10 s cache (second call within 10 s does not run `iw`); wired
  `speed` `-1` → null; driver basename.
- **Privacy:** the probe's JSON/text masking: SSID text never appears (assert on
  the masking helper if the probe code is reachable from tests; otherwise assert
  that no model property holds a MAC-formatted string for a fixture whose sysfs
  `address` file contains `aa:bb:cc:dd:ee:ff`).

Report files added, test count, mismatches.

## Round 2 (after Amendment 1 of the A2 handoff)

- `Privacy.MaskInterface` (or the helper WO2 names): `enx00e04c68221a` → `enx…`;
  `ENX00E04C68221A` → `enx…`; `enx123` (too short) and `wlp0s20f3`, `eth0`
  unchanged.
- `/proc/diskstats` lines without field 13 (only 11 fields) → `IoMs` null and
  `ActivityPercent` null after two samples; an io_ticks counter that goes
  backwards → null for that sample. Update any earlier test that assumed 0.
