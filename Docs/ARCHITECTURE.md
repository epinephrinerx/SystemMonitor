# Architecture

เอกสารนี้อธิบายโครงสร้างและขอบเขตของ **System Monitor** (`SystemMonitor4.0`) ให้อัปเดตเมื่อมีการเปลี่ยน service boundary, data flow, storage หรือ dependency direction

> ขอบเขตของเอกสาร: อธิบายสถานะหลัง **R-021** (สัญญา + ตัวโหลด + โฮสต์ส่วนเสริม + SMART เป็นส่วนเสริมแรก + แพ็กเกจ/ตัวติดตั้ง) และ **R-022** (ติดตั้งแบบทั้งเครื่อง / Program Files / Scheduled Task — ดู `Docs/adr/0002-machine-wide-install.md`) ซึ่ง **ลงมือและทดสอบด้วยโฟลเดอร์จำลองแล้ว แต่ยังไม่ได้ตรวจบนเครื่องจริง**

## 1. System overview

System Monitor เป็น Windows desktop widget แบบ native (C#/.NET 8 WPF) ที่แสดงสถานะ CPU/RAM/Disk/GPU/Network จากตัวนับของ Windows และ ACPI thermal zone ตั้งแต่ R-021 ตัวโฮสต์ถูกแยกเป็น "core" ที่คงที่ บวก "ส่วนเสริม" ที่โหลดเป็น .NET assembly ตอนรันจากโฟลเดอร์ `extensions` ใต้โฟลเดอร์ exe โดยไม่ต้อง build โฮสต์ใหม่ โมดูล SMART ถูกย้ายออกมาเป็นส่วนเสริม first-party ตัวแรก (`SysMonitor.Ext.Smart`) และเป็นผู้ให้อุณหภูมิดิสก์ผ่านสัญญา `IDiskTemperatureSource` ตัวติดตั้งมีสองโหมด: per-user (ไม่ต้องใช้สิทธิ์ผู้ดูแลระบบ และแอปไม่ยกระดับสิทธิ์เองตอนเปิด) และแบบทั้งเครื่อง (R-022: ลง Program Files และแอปเปิดซ้ำตัวเองแบบยกระดับตอนเริ่ม)

### Goals

- คงพฤติกรรมเดิมของ widget (หน้าตา ค่า config ภาษา ปุ่ม) ไว้ทั้งชุด — การแยก core เป็นการจัดโครงสร้างใหม่ ไม่ใช่การเปลี่ยนพฤติกรรม (`Requirements.md` R-021)
- ใช้สัญญา (contract) เดียวที่บันทึกไว้ที่เดียว และให้ส่วนเสริมที่เขียนตามสัญญาเสียบได้ (`SysMonitor.Extensibility`)
- โหลด/ถอดส่วนเสริมได้โดยไม่ต้อง build โฮสต์ใหม่ และส่วนเสริมที่เสียต้องถูกข้ามพร้อมข้อความ ไม่ทำให้โฮสต์ crash
- ตรวจ SHA-256 ของทุกไฟล์ก่อนรันโค้ดส่วนเสริม และแยกความเชื่อถือเป็น first-party / อนุมัติโดยผู้ใช้ / ไม่เชื่อถือ

### Non-goals

จาก `Requirements.md` R-021 หัวข้อ "ไม่รวม":

- ไม่ทำตลาด/ดาวน์โหลดส่วนเสริมจากอินเทอร์เน็ต และไม่ทำตัวอัปเดตส่วนเสริมอัตโนมัติ
- ไม่เพิ่ม package ของบุคคลที่สามในผลิตภัณฑ์ (ใช้เฉพาะสิ่งที่มากับ .NET)
- ไม่แก้ GUI MFC ของ CrystalDiskInfo และไม่ทำบน Linux
- ไม่ยกระดับสิทธิ์อัตโนมัติ (ส่วนเสริมไม่ได้สิทธิ์เพิ่มเอง)
- ไม่ตรวจจับ/กำจัดมัลแวร์ (หน้าที่ของ Windows Defender/แอนตี้ไวรัส)
- ชั้นที่ 3 ของโมเดลความเชื่อถือ (ติดตั้งใต้ `Program Files`) ทำใน R-022 เป็นทางเลือกของตัวติดตั้ง: แอปที่ติดตั้งแบบนั้นเปิดแบบยกระดับทุกครั้ง ส่วนติดตั้งแบบ per-user ยังเป็นค่าเดิม

## 2. Context diagram

```text
                          ┌───────────────┐
                          │    ผู้ใช้      │
                          └───────┬───────┘
        หน้าต่าง WPF / tray / checkbox / ปุ่ม "อนุมัติ..." / UAC prompt
                                  │
┌─────────────────────────────────▼──────────────────────────────────────┐
│ TB1 · โปรเซสปกติ (ไม่ elevated) — SystemMonitor.exe                     │
│                                                                       │
│   MainWindow ◄── Snapshot (Volatile) ── Sampler (background task)      │
│        │                                     │ kernel32 / IOCTL / WMI   │
│        │                                     └─────────► Windows APIs   │
│        └──► ExtensionHost ──► ExtensionLoader ──► ExtensionLoadContext │
│                  │                  │                                  │
│                  │                  └── อ่าน <โฟลเดอร์ exe>\extensions\<id>\
│                  └── CollectViews / GetDiskTemperatures                │
│                                                                       │
│   AppConfig ── %APPDATA%\SystemMonitor\config.wpf.json                 │
│   Diag ────── %APPDATA%\SysMonitor\sysmonitor-wpf.log                  │
│   Updater ─── GitHub Releases (HTTPS)                                  │
└────────────┬─────────────────────────────────────┬────────────────────┘
             │ runas (โปรเซสเดียว จบแล้วปิด)          │ อ่านไฟล์ส่วนเสริม
             ▼                                     ▼
┌──────────────────────────────────┐   ┌──────────────────────────────────┐
│ TB1' · โปรเซส elevated (ชั่วคราว) │   │ <โฟลเดอร์ exe>\extensions\<id>\  │
│  --approve-extension <id> <sha>  │   │  extension.json + ไฟล์ที่ระบุ     │
│  --elevated-op …                 │   └──────────────────────────────────┘
└──────────────┬───────────────────┘
               ▼
  %ProgramData%\SystemMonitor\approved-extensions.json
  (ACL: เขียนได้เฉพาะ Administrators/SYSTEM)
```

**Trust boundaries**

| ขอบเขต | ความหมาย | การบังคับใช้ |
|---|---|---|
| TB1 โปรเซสปกติ vs TB1' โปรเซส elevated | แอปไม่มีทางยกระดับตัวเองตอนเปิด มีเพียงโปรเซสลูกที่ผู้ใช้ยินยอมผ่าน UAC และทำงานงานเดียวแล้วจบ | `SysMonitor.Wpf/App.xaml.cs:App.OnStartup` แยกสองสวิตช์ (`ExtensionApproval.Switch`, `MemoryTools.ElevatedSwitch`) ออกก่อนโหลด config |
| first-party | SHA-256 ของ `extension.json` อยู่ใน allowlist ที่ฝังใน `SystemMonitor.exe` | `SysMonitor.Wpf/Extensions/FirstPartyAllowlist.cs:FirstPartyAllowlist.FromAssembly` |
| approved | SHA-256 ตรงกับรายการใน `%ProgramData%\SystemMonitor\approved-extensions.json` ที่ ACL อนุญาตให้เฉพาะ admin เขียน | `SysMonitor.Wpf/Extensions/ApprovalStore.cs:ApprovalStore.IsApproved`, `AclPolicy.IsAdminOnlyWritable` |
| untrusted | ไม่อยู่ในสองกลุ่มข้างต้น | โฮสต์ปกติ → `NotApproved`; โฮสต์ elevated → `NotTrustedWhenElevated` |

## 3. Repository map

```text
SystemMonitor4.0/
├── SysMonitor.Wpf/              # โฮสต์ WPF (AssemblyName SystemMonitor, net8.0-windows)
│   ├── Extensions/              # ExtensionHost, ExtensionLoader, manifest, trust, approval
│   ├── Sensors/                 # Sampler และตัวอ่านค่า (CPU/RAM/Disk/GPU/Network/Thermal)
│   ├── Model/                   # Snapshot และ record ของข้อมูล
│   ├── Native/                  # Win32/IOCTL/registry interop
│   ├── ViewModels/              # WidgetViewModel (+ ส่วนขยายของส่วนเสริม)
│   ├── Controls/                # วิดเจ็ตที่วาดเอง (chart ฯลฯ)
│   ├── Converters/
│   ├── app.manifest             # per-monitor-v2 DPI
│   └── first-party-extensions.json   # สร้างโดย PackTool (gitignored)
├── SysMonitor.Extensibility/    # สัญญาส่วนเสริม (ไม่มี dependency)
├── SysMonitor.Ext.Smart/        # ส่วนเสริม first-party: SMART + disk temperature
├── SysMonitor.Ext.Sample/       # ส่วนเสริมตัวอย่าง id "sample" (ไม่อยู่ใน allowlist)
├── SysMonitor.PackTool/         # แพ็กส่วนเสริม + SHA-256 + เติม allowlist
├── SysMonitor.Setup/            # ตัวติดตั้ง (per-user และแบบทั้งเครื่อง) + payload/ ที่ฝังเป็น resource
├── SysMonitor.Tests/            # MSTest (+ Extensions/ สำหรับเทสต์ส่วนเสริม)
├── SysMonitor.Tests.DummyExtension/
├── Docs/                        # AI_WORKFLOW.md, ARCHITECTURE.md, EXTENSION_CONTRACT.md, TESTING.md, adr/, Requirements-upstream.md และเอกสารรีวิว/handoff (Requirements.md อยู่ที่ root)
├── build-wpf.cmd                # build 5 ขั้น → dist-wpf\
├── Requirements.md
├── build-ext/                   # staging ของ PackTool (gitignored)
└── dist-wpf/                    # ผล build (gitignored)
```

## 4. Components and ownership

| Component | Responsibility | Public interface |
|---|---|---|
| `SysMonitor.Wpf` (`SystemMonitor.exe`) | โฮสต์: ลูปอ่านค่า, snapshot, หน้าต่าง 3 โหมด, sidebar, tray, config, i18n, diag, updater, ตัวโหลดและโฮสต์ส่วนเสริม | `SysMonitor.Wpf/Sensors/Sampler.cs:Sampler` (`Start`, `Current`, `Nudge`, `RefreshDrives`, `RefreshMemory`), `SysMonitor.Wpf/MainWindow.xaml.cs:MainWindow`, `SysMonitor.Wpf/AppConfig.cs:AppConfig`, `SysMonitor.Wpf/Extensions/ExtensionHost.cs:ExtensionHost` (`StartAsync`, `Entries`, `SetEnabled`, `Reload`, `CollectViews`, `GetDiskTemperatures`, `SetDemand`), `SysMonitor.Wpf/Extensions/ExtensionLoader.cs:ExtensionLoader` (`LoadAll`, `LoadFolder`, `Inspect`), `SysMonitor.Wpf/Extensions/ApprovalStore.cs:ApprovalStore`, `SysMonitor.Wpf/Extensions/AclPolicy.cs:AclPolicy`, `SysMonitor.Wpf/Extensions/ExtensionApproval.cs:ExtensionApproval`, `SysMonitor.Wpf/Diag.cs:Diag`, `SysMonitor.Wpf/Updater.cs:Updater` |
| `SysMonitor.Extensibility` | สัญญาที่โฮสต์และส่วนเสริมใช้ร่วมกัน (type identity เดียว) | `IExtension`, `IExtensionContext`, `IViewProvider`, `IDiskTemperatureSource`, `IDemandAware`, `ExtensionView` + records, `ExtensionDemand`, `Severity`, `ContractInfo.Version` (= 1.1) |
| `SysMonitor.Ext.Smart` | ส่วนเสริม first-party: สแกน SMART ผ่าน `DiskInfoCore.dll` และให้อุณหภูมิดิสก์; สร้างหน้า Widget/Overview/Tab ของ SMART | `SmartExtension` (`IExtension`, `IViewProvider`, `IDemandAware`, `IDiskTemperatureSource`, `IDisposable`), `SmartStrings`, `SmartViewBuilder`; native: `DiskInfoCore.dll` (C ABI v1, `di_abi_version`/`di_open`/`di_scan`/`di_disk_count`/`di_disk_info`/`di_close`) |
| `SysMonitor.Ext.Sample` | ตัวอย่างส่วนเสริมที่ต้องผ่านขั้นอนุมัติ (id `sample`) | `SampleExtension` |
| `SysMonitor.PackTool` | แพ็กโฟลเดอร์ส่วนเสริมจาก `extension.template.json`: คัดลอกเฉพาะไฟล์ที่ระบุ, คำนวณ SHA-256, สร้าง `extension.json`, เติม allowlist | CLI `--template --out --version [--file name=path] [--allowlist file]`; `ExtensionPacker.Pack`, `ExtensionPacker.UpdateAllowlist` |
| `SysMonitor.Setup` (`SystemMonitor-Setup.exe`) | ติดตั้ง/ถอนสองโหมด: per-user (`%LOCALAPPDATA%`, HKCU, Run key) และ admin (`Program Files`, HKLM, Scheduled Task, `install-mode.json`); ขอ UAC ครั้งเดียวด้วยการเปิดตัวเองซ้ำ | `InstallLayout`/`InstallScope`, `Installer.Install/Uninstall/MigrateFromPerUser/OwnedPaths`, `IInstallHost`/`WindowsInstallHost`, `ScheduledTasks`, `SetupArguments`, `PrivateDirectory`, `Elevation`, `Legacy`, `Shortcuts` |
| `SysMonitor.Tests` | ชุดทดสอบ MSTest (โฮสต์, ส่วนเสริม, ตัวติดตั้ง, config, palette ฯลฯ) | `dotnet test SysMonitor.Tests` |
| `SysMonitor.Tests.DummyExtension` | ส่วนเสริมจำลองสำหรับเทสต์ตัวโหลด/โฮสต์ | assembly ที่ implement `IExtension` |

## 5. Dependency rules

| โปรเจกต์ | ProjectReference | หมายเหตุ |
|---|---|---|
| `SysMonitor.Core` | ไม่มี | โมเดลกลาง (`Model/`, `Sensors/IoRate.cs`): ไม่มี `ProjectReference`, ไม่มี package, ไม่เรียก OS API; `InternalsVisibleTo` ให้ `SysMonitor.Linux`, `SysMonitor.Linux.Tests` |
| `SysMonitor.Linux` | `SysMonitor.Core` | ตัวอ่านค่า Linux (R-027): `/proc`, `/sys`, `iw`, `nvidia-smi` ผ่าน `ICommandRunner`; ไม่มี package; ทุก reader รับ root (ค่าเริ่มต้น `/`) เพื่อเทสต์ด้วย tree จำลอง; `InternalsVisibleTo` ให้ `SysMonitor.Linux.Tests` |
| `SysMonitor.Linux.Probe` | `SysMonitor.Linux` | console probe (R-027): เก็บสอง sample ห่างกัน 1 วินาที แล้วพิมพ์รายงานอ่านง่าย หรือ JSON (`--json`); ไม่มี package และไม่เปิดหน้าต่าง |

Branch `linux-native` is Linux-only (R-027); the Windows projects live on `main`.

- **ไม่มี `PackageReference` ในโปรเจกต์ผลิตภัณฑ์ทั้งหมด** (`SysMonitor.Wpf.csproj` ระบุไว้ตรง ๆ ว่า "No package references") ข้อยกเว้นคือ `SysMonitor.Tests` ที่ใช้ `Microsoft.NET.Test.Sdk`, `MSTest.TestAdapter`, `MSTest.TestFramework`
- ไม่มี circular dependency; ทิศทางเดียวคือ extension → contract ← host
- ความเข้ากันได้ของสัญญา: manifest `contract` ต้อง major เท่ากับ `ContractInfo.Version` และ minor ≤ ของโฮสต์; `min_host` ต้อง ≤ รุ่นโฮสต์ (`ExtensionLoader.LoadSingleFolder`)

## 6. Runtime data flow

### (a) Sampling loop → snapshot → UI

1. `App.OnStartup` โหลด `AppConfig`, เริ่ม `Diag`, สร้าง `ExtensionHost` (background) และ `Sampler` แล้วเปิดหน้าต่าง (`SysMonitor.Wpf/App.xaml.cs`)
2. `Sampler.RunAsync` เรียก `Collect()` ตามจังหวะ `AppConfig.Intervals.Metrics` (eco 5 s / balanced 2.5 s / fast 1 s) ส่วนงานหนักมีความถี่ของตัวเอง: พื้นที่ว่างตาม `Intervals.Space`, อุณหภูมิตาม `Intervals.Temps`, รายการไดรฟ์ทุก 60 s, process ทุก 3 s, service ทุก 10 s
3. `Collect()` ประกอบ `Snapshot` ก้อนใหม่ (cores, RAM, disks, adapters, GPU, modules, `Extensions = ExtensionHost.CollectViews(config.Lang)`) แล้ว `Volatile.Write` ทับ reference เดิม
4. UI ใช้ `DispatcherTimer` ทุก 400 ms อ่าน `Sampler.Current` แล้ววาด (`MainWindow.OnTick`); heartbeat ทุก 10 นาทีเขียน `Diag.Heartbeat`

Failure behaviour (ตรวจในโค้ด):

- probe ใดโยน exception → `Sampler.Guard` เขียน `Diag.ReportException("Probe <key>")` (จำกัด 1 ครั้ง/นาที/context) แล้วพัก probe นั้น 5 นาที และคืนค่าล่าสุดที่ดีอยู่แล้ว ไม่ publish ค่า 0
- probe ที่ช้ากว่าบัดเจ็ต (250 ms ปกติ, 2–3 s สำหรับ WMI/enumeration) → พัก 5 นาทีและเขียน log บรรทัด `Slow probe ...`
- `Collect()` ทั้งก้อนโยน exception → `Diag.ReportException("Sampler")` และลูปไปต่อ (tick นั้นไม่ publish snapshot ใหม่)
- ส่วนเสริมโยน exception ตอนเก็บ view → ข้ามส่วนเสริมนั้นในรอบนั้นและ log ครั้งเดียวต่อชนิด exception (`ExtensionHost.SafeLogOnce`)

### (b) Extension discovery / load / enable / disable

1. `ExtensionHost.StartAsync` ทำงานบน `Task.Run` ไม่บล็อก UI เธรด
2. `ExtensionLoader.LoadAll` ไล่โฟลเดอร์ย่อยใต้ `<โฟลเดอร์ exe>\extensions` เรียงตามชื่อ (ordinal, ไม่สนตัวพิมพ์) แล้วเรียก `LoadSingleFolder`
3. ลำดับตรวจของ `LoadSingleFolder`: โฟลเดอร์/โฟลเดอร์ย่อยห้ามเป็น reparse point → อ่านและ SHA-256 ของ `extension.json` → ชื่อโฟลเดอร์ต้องเท่ากับ `id` → `contract` เข้ากัน → `min_host` → path ของทุกไฟล์ปลอดภัยและไม่มีไฟล์นอก manifest → ไฟล์ครบ → ความเชื่อถือ (elevated/normal) → ถ้าปิดใช้งานใน config ให้ `Disabled` เว้นแต่ `KeepLoaded` → อ่านทุกไฟล์และตรวจ SHA-256 ก่อนโหลดอะไร (ไฟล์ native ถือ `FileShare.Read` ไว้จนโหลดเสร็จ) → entry assembly ต้องอยู่ในรายการและไม่ใช่ native → กัน id ซ้ำในรอบเดียวกัน → preload native ด้วย `LoadLibraryEx` → โหลด managed เข้า `ExtensionLoadContext` (collectible, แชร์ `SysMonitor.Extensibility` กับโฮสต์) → `Initialize(context)`
4. ผลลัพธ์ถูกแปลงเป็น `ExtensionEntry` (สถานะ + ข้อความ) ให้ sidebar; checkbox เปิด/ปิดเรียก `ExtensionHost.SetEnabled` แล้วผู้เรียกบันทึก `AppConfig.Save()` และ `Sampler.Nudge()`
5. ปิด = slot ถูกปลดระวาง (`RetireAndDispose`) แล้ว `Dispose()`; เปิด = โหลดใหม่ผ่าน `ExtensionLoader.LoadFolder`

Failure behaviour:

- `ExtensionLoader` ไม่โยน exception ออกมา (ยกเว้น argument เป็น null) ทุกความล้มเหลวกลายเป็น `ExtensionStatus` (`ManifestInvalid`, `ContractMismatch`, `HostTooOld`, `FileMissing`, `HashMismatch`, `UnsafePath`, `NotApproved`, `NotTrustedWhenElevated`, `DuplicateId`, `EntryNotFound`, `LoadFailed`, `Disabled`) พร้อมข้อความ ซึ่ง UI แสดงผ่าน `ExtensionStatusText.KeyFor`
- ส่วนเสริมที่เปิดอยู่ปิดได้เสมอ; ส่วนเสริมที่ปิดอยู่จะเปิดได้เฉพาะเมื่อสถานะเป็น `Loaded`/`Disabled` (`ExtensionStatusText.CanToggle`)
- การปิดส่วนเสริมระหว่างที่มีผู้เรียกค้าง: รอไม่เกิน 5 วินาทีแล้ว `Dispose` ต่อพร้อมเขียน log
- ส่วนเสริม first-party ที่ `provides` มี `disk-temperature` ถูกโหลดค้างแม้ผู้ใช้ปิดสวิตช์ (สวิตช์คุมแค่การแสดงหน้าจอ) — `ExtensionHost.IsKeptLoadedManifest`
- ถ้าไม่มีโฟลเดอร์ `extensions` เลย `LoadAll` คืนลิสต์ว่าง โฮสต์เริ่มได้ตามปกติ

### (c) Approval flow (UAC)

1. ผู้ใช้กด "อนุมัติ..." ใน sidebar ซึ่งแสดงเฉพาะเมื่อสถานะเป็น `NotApproved` และโฮสต์ไม่ elevated (`ExtensionStatusText.CanApprove`)
2. โฮสต์เปิด `SystemMonitor.exe --approve-extension <id> <manifest-sha256>` ด้วย `Verb = "runas"` (`ExtensionApproval.CreateStartInfo`) แล้วรอ exit code
3. โปรเซสลูกตรวจ argument (id ตาม regex, sha 64 hex) → ต้อง elevated → `ExtensionLoader.Inspect` ตรวจโครงสร้างและ hash ทุกไฟล์โดยไม่โหลดโค้ด → sha ที่คำนวณใหม่ต้องตรงกับ argument → ถ้าเป็น first-party จบเป็นอนุมัติโดยไม่เขียน store → แสดงกล่องยืนยัน (มีคำเตือนถ้าแพ็กมีไฟล์ native) → เขียน `ApprovalStore.Approve` (lock file, ACL admin-only, เขียน temp แล้ว `File.Move` ทับ)
4. exit code: `0` อนุมัติสำเร็จ, `1` argument ผิด, `2` ผู้ใช้ปฏิเสธ, `3` ตรวจแพ็กเกจไม่ผ่าน, `4` เขียน store ไม่สำเร็จ, `5` ไม่ได้ elevated
5. `0` → หน้าต่างหลักเรียก `ExtensionHost.Reload(id)` แล้ว `Sampler.Nudge()`

Failure behaviour:

- ผู้ใช้กด "No" ที่ UAC → `Win32Exception` 1223 ถูกกลืนอย่างตั้งใจ ไม่มีอะไรเกิดขึ้น (`AdminRestart.IsDeclined`)
- store ถูกผู้อื่น lock เกิน 10 วินาที → `ApprovalResult.Failed("Approval store is busy")` → exit 4
- store/โฟลเดอร์/ancestor เป็น reparse point หรือ ACL ไม่ผ่าน → ปฏิเสธก่อนเขียน (fail closed) → exit 4
- อ่าน store เดิมไม่ผ่าน ACL/parse ไม่ได้ → เริ่มจากรายการว่างและเขียน log (ไม่กลืนเงียบ)

### (d) Disk temperature flow

1. `Sampler.RefreshTemperatures` (จังหวะ `Intervals.Temps`) เรียก `ExtensionHost.GetDiskTemperatures()` หนึ่งครั้งต่อรอบ
2. ต่อไดรฟ์: อ่านอุณหภูมิจาก storage stack ก่อน (`Win32.DriveTemperature` → property 52 = `StorageDeviceTemperatureProperty`)
3. `DiskTemperature.Merge` เลือกค่า: ค่าจาก storage ถ้า "เป็นไปได้" (`> 0 and < 150`) มิฉะนั้นใช้ค่าจากส่วนเสริม (แหล่ง `Smart`) มิฉะนั้น `null` + `TempSource.None`
4. ถ้ามีไดรฟ์ที่ยังไม่ได้ค่าที่เป็นไปได้ → `wantSmart = true` → `ExtensionHost.SetDemand(true)` ส่งต่อไปยัง `IDemandAware.SetDemand` (เฉพาะส่วนเสริมที่ implement)
5. ส่วนเสริม SMART สแกนเฉพาะเมื่อโฮสต์ elevated (สร้าง scan thread ใน `Initialize`) ทุก 2 นาที และคืนเฉพาะอุณหภูมิที่ `> 0 and < 150`

Failure behaviour:

- ไม่มีส่วนเสริม/enable ไม่ได้ → `extTemps` ว่าง ไดรฟ์ที่ storage ตอบไม่ได้แสดง `n/a` ไม่มีการประมาณค่า (`DiskTemperature` ไม่มี fallback เป็นตัวเลข)
- ไม่มี `DiskInfoCore.dll` หรือ ABI major ไม่ตรง → `SmartCore.IsAvailable` = false → สถานะ `Unavailable` และไม่มีอุณหภูมิ
- โฮสต์ไม่ elevated → ส่วนเสริมสร้างสถานะ `NeedAdmin` ทันทีและไม่สแกน (`SmartExtension.Initialize`)
- ค่าที่ส่วนเสริมส่งมาเกินขอบเขต (drive 0..255, °C 1..149, ≤ 64 รายการ/ส่วนเสริม) ถูกทิ้ง; ถ้าหลายส่วนเสริมตอบไดรฟ์เดียวกัน ตัวแรกตามลำดับ id ชนะ (`ExtensionHost.GetDiskTemperatures`)
- storage stack ตอบ 0 °C → ถือว่า "เซ็นเซอร์บอกว่าไม่มีค่า" ไม่ใช่ดิสก์เย็น 0 องศา (`DiskTemperature.Plausible`)

## 7. Data and storage

| Entity/file | ที่อยู่จริง | Owner | หมายเหตุ |
|---|---|---|---|
| `config.wpf.json` | `%APPDATA%\SystemMonitor\config.wpf.json` (`AppConfig.Path`) | โฮสต์ (`AppConfig`) | JSON snake_case; เขียนผ่าน `path + ".tmp"` แล้ว `File.Move(overwrite)`; ไฟล์หาย/เสีย = ใช้ค่า default ไม่ทำให้เริ่มไม่ได้ |
| config รุ่นก่อน 4.0 | `%APPDATA%\SysMonitor\config.wpf.json` (`AppConfig.LegacyPath`) | โฮสต์ | คัดลอกครั้งเดียวเมื่อยังไม่มีไฟล์ใหม่; key เก่า `mini_w/mini_h`, `exp_w/exp_h/exp_sized` ถูกอ่านแล้วเขียนใหม่ภายใต้ชื่อใหม่ |
| `approved-extensions.json` + `.lock` | `%ProgramData%\SystemMonitor\` (`ApprovalStore.DefaultPath`) | โปรเซส elevated (`ApprovalStore`) | array ของ `{id, name, manifest_sha256, approved_at}`; ต้องผ่าน ACL/owner check และไม่มี reparse point ใน path |
| `first-party-extensions.json` | embedded resource ใน `SystemMonitor.exe` (`LogicalName="first-party-extensions.json"`) | build (`PackTool`) | ไฟล์ต้นทาง `SysMonitor.Wpf\first-party-extensions.json` ถูก gitignore และสร้างใหม่ทุกครั้งที่รัน `build-wpf.cmd`; ปัจจุบันมี 1 hash |
| `extensions\<id>\extension.json` + ไฟล์ใน `files[]` | `<โฟลเดอร์ exe>\extensions\<id>\` (`App.OnStartup` ประกอบ path เอง) | `SysMonitor.Setup` เขียน, `ExtensionLoader` อ่าน | ไฟล์นอก manifest ทำให้ `UnsafePath`; `extension.json` ถูกเขียนเป็นไฟล์สุดท้ายตอนติดตั้ง |
| `sysmonitor-wpf.log` | `%APPDATA%\SysMonitor\sysmonitor-wpf.log` (`Diag.Path`) | `Diag` | ตัดครึ่งเมื่อเกิน 256 KiB; timestamp invariant culture |
| โฟลเดอร์ติดตั้ง | per-user: `%LOCALAPPDATA%\Programs\SystemMonitor\` · admin: `%ProgramFiles%\SystemMonitor\` (`InstallLayout`, path มาจาก known folder ของ Windows) | `SysMonitor.Setup` | ถอนไม่ไล่ลบทั้งโฟลเดอร์ ใช้รายการไฟล์ที่ตัวเองเป็นเจ้าของ; แบบ admin มี `install-mode.json` ที่แอปอ่านเพื่อรู้ว่าต้องยกระดับ (เชื่อเฉพาะเมื่ออยู่ใน Program Files) |
| ไฟล์ติดตั้งชั่วคราวของ Updater | แอปปกติ: `%TEMP%\SystemMonitor-Setup-<version>.exe` · แอปที่ยกระดับ: `%ProgramData%\SysMonitor-update-<guid>\` (ACL เฉพาะ Administrators/SYSTEM) | `Updater.DownloadAsync` | ตั้งชื่อเอง ไม่ใช้ชื่อจาก response; ลบเมื่อดาวน์โหลดล้มเหลว; ไม่ตรวจแฮช/ลายเซ็น |

- Source of truth: ไฟล์ JSON ในโปรไฟล์ผู้ใช้; ไม่มีฐานข้อมูล ไม่มี schema migration และไม่มี backup/restore อัตโนมัติ
- Identifier: extension ใช้ `id` จาก manifest (regex `^[a-z0-9][a-z0-9._-]{0,63}$`, ต้องเท่ากับชื่อโฟลเดอร์); ความเชื่อถือผูกกับ SHA-256 ของ `extension.json` ไม่ใช่ชื่อ
- Transaction boundary: การเขียน config ใช้ temp + rename; การเขียน approval store ใช้ lock file + temp + rename
- ข้อสังเกตที่ยังไม่แก้: `Installer.SettingsFiles` ระบุ `sysmonitor-wpf.log` อยู่ใต้ `%APPDATA%\SystemMonitor\` แต่ `Diag` เขียนจริงที่ `%APPDATA%\SysMonitor\` — รายการไฟล์ที่จะลบตอน "ลบ settings" จึงไม่ตรงกับ log จริง

## 8. Interfaces

### Extension contract

- ตำแหน่ง: `SysMonitor.Extensibility` (ไม่มี dependency, `AssemblyVersion` ตรึง `1.0.0.0`)
- รูปแบบ: managed .NET interface, โหลด in-process ด้วย `AssemblyLoadContext` แบบ collectible ไม่มี HTTP/API ภายนอก
- เวอร์ชันสัญญา: `ContractInfo.Version` = `1.1`; เพิ่มความสามารถแบบ optional interface (`IDemandAware`, `IDiskTemperatureSource`) โดยไม่เปลี่ยน major
- compatibility policy: `contract` major ต้องเท่ากัน, minor ของ manifest ≤ โฮสต์, `min_host` ≤ รุ่นโฮสต์; ไม่ผ่าน = `ContractMismatch`/`HostTooOld`
- contract ของ manifest (`SysMonitor.Wpf/Extensions/ExtensionManifest.cs:ExtensionManifest.Validate`): `id`, `name`, `version`, `contract`, `min_host`, `entry{assembly,type}`, `provides[]` (≤ 8, ไม่ซ้ำ, regex `^[a-z0-9][a-z0-9-]{0,31}$`), `place_after` (cpu|ram|disk|gpu|network), `files[]{path,sha256,native}`
- ข้อจำกัดที่โฮสต์บังคับกับ view: `WidgetPages` ≤ 32, `OverviewRows` ≤ 64, `DetailTabs` ≤ 32, `Fact`/แท็บ ≤ 64, ข้อความ ≤ 512 ตัวอักษร, `Percent` ถูก clamp 0–100, อุณหภูมิในหน้า widget รับ −50..150 (`ExtensionHost.SanitizeView`)

### External dependencies

| Dependency | Purpose | Timeout/retry | Failure impact |
|---|---|---|---|
| `DiskInfoCore.dll` (C ABI v1, มาจากโปรเจกต์ DiskInfo) | อ่าน SMART ของดิสก์ที่ไม่ตอบผ่าน storage stack; ต้องมีสิทธิ์ admin | ไม่มี retry; probe `di_abi_version` ครั้งเดียวต่อโปรเซส | ส่วนเสริมรายงาน `Unavailable`/`NeedAdmin`, ดิสก์นั้นแสดง `n/a` |
| Windows APIs (kernel32/IOCTL, WMI ผ่าน COM late binding, performance counters) | CPU, RAM, ดิสก์, GPU, network, thermal zone | probe ที่ช้าเกินบัดเจ็ตถูกพัก 5 นาที | ค่าเดิมคงอยู่ ไม่ publish ค่า 0; thermal zone พลาด 3 ครั้งแล้วหยุดถาม |
| GitHub Releases REST (`api.github.com/repos/epinephrinerx/SystemMonitor/releases/latest`) | ตรวจรุ่นใหม่และดาวน์โหลดตัวติดตั้ง | `HttpClient.Timeout` 30 s; ดาวน์โหลดเฉพาะ host `github.com`, `objects.githubusercontent.com`, `release-assets.githubusercontent.com` | ตรวจไม่ได้ = รายงานว่า "ตรวจไม่ได้" ไม่ใช่ "ล่าสุดแล้ว"; ไม่มีการติดตั้งเอง |
| .NET 8 Desktop Runtime | รันแอปแบบ framework-dependent | — | ตัวติดตั้งตรวจและแจ้งถ้าไม่มี ไม่ติดตั้งแทน |

## 9. Cross-cutting concerns

- **Configuration** — ไฟล์เดียว `config.wpf.json` โหลดครั้งเดียวตอนเริ่มและส่งเป็น object เดียวให้ `Sampler`, `MainWindow`, `ExtensionHost`; การบันทึกเป็นความรับผิดชอบของผู้เรียก (UI) ไม่ใช่ของโฮสต์ส่วนเสริม; ค่าที่เพิ่มมาทีหลังได้ default โดยไม่ทำให้ config เก่าใช้ไม่ได้
- **ส่วนเสริมใน config** — `extensions` เป็น dictionary `{id: {enabled}}`; `show_smart` รุ่นเก่าย้ายเป็น `extensions.smart`; `FullTab` ค่า `smart`/`smartN` ย้ายเป็น `ext:smart:status`/`ext:smart:diskN`
- **Logging (Diag)** — ไฟล์ข้อความบรรทัดเดียวต่อเหตุการณ์: start banner, heartbeat ทุก 10 นาที (rss + heap + GC), exception ที่ดักได้ (จำกัด 1 ครั้ง/นาที/context), ข้อความจากส่วนเสริมผ่าน `IExtensionContext.Log`; diagnostics ปิดได้ด้วย `AppConfig.Diagnostics`; ตัวช่วย elevated เขียน log แม้ผู้ใช้ปิด diagnostics
- **Localization** — `Lang` ใน `SysMonitor.Wpf/I18n.cs` เก็บ dictionary `th`/`en` และ fallback ไป `en` แล้วคืน key ถ้าไม่พบ; ภาษาที่เลือกถูกส่งต่อให้ส่วนเสริมทุกครั้งผ่านพารามิเตอร์ `language` ของ `IViewProvider.GetDisplayName`/`GetView`; `IExtensionContext.Language` เป็นค่าภาษา ณ ตอนโหลดเท่านั้น
- **Elevation** — แอปที่ติดตั้งแบบ per-user ไม่ยกระดับตอนเปิด; มีโปรเซส elevated ชั่วคราวสองแบบเท่านั้น: `--approve-extension <id> <sha256>` และ `--elevated-op kill|service …` (แท็บ memory) ทั้งคู่จบแล้วปิด · แอปที่ติดตั้งแบบทั้งเครื่อง (R-022) เปิดซ้ำตัวเองแบบยกระดับตอนเริ่มก่อนจับ lock single-instance (`ElevatedLaunch.TryRelaunch`; ข้ามได้ด้วย `--no-elevate`; ผู้ใช้ปฏิเสธ UAC = ทำงานต่อแบบไม่ยกระดับ) และตัวติดตั้งยกระดับตัวเองด้วย `runas` ครั้งเดียวเมื่องานเป็นแบบทั้งเครื่อง · โหมด elevated โหลดได้เฉพาะส่วนเสริม first-party
- **DPI / theme / typography** — per-monitor-v2 DPI ประกาศใน `SysMonitor.Wpf/app.manifest`; ธีม light/dark มาจาก `AppConfig.Theme` + `Palette`; ขนาดตัวอักษรมาจาก `AppConfig.FontScale` ผ่าน `FontRamp`
- **Single instance** — mutex ต่อ session (`Local\SystemMonitor.SingleInstance`); `--allow-multiple` ใช้สำหรับเทียบ build เท่านั้น
- ไม่มี authentication, authorization แบบผู้ใช้, secrets, metrics/tracing, caching ชั้น network หรือ rate limiting ในระบบนี้

## 10. Quality attributes

| Attribute | Target | How verified |
|---|---|---|
| Test suite | เขียวทุกครั้งก่อนแพ็ก | `build-wpf.cmd` ขั้น `[1/5]` รัน `dotnet test SysMonitor.Tests` และหยุดทันทีถ้าไม่ผ่าน; ผลล่าสุด 2026-11-16 หลัง R-022 เฟส 3: 679 ผ่าน, 2 ข้าม (สร้าง file symlink ไม่ได้), 0 ล้ม (บันทึกใน `Requirements.md` R-022) |
| เริ่มได้โดยไม่มีส่วนเสริม | โฮสต์ไม่ล้ม | `ExtensionLoader.LoadAll` คืนลิสต์ว่างเมื่อไม่มีโฟลเดอร์ และ `ExtensionHost` แสดงเฉพาะ entry ที่มี |
| ส่วนเสริมเสียไม่ทำโฮสต์ crash | คืนสถานะแทน throw | `ExtensionLoader` กลืน exception เป็น `ExtensionStatus`; เทสต์ใน `SysMonitor.Tests/Extensions` |
| ความถูกต้องของ hash/ความเชื่อถือ | ไม่โหลดเมื่อไม่ตรง/ไม่อนุมัติ | unit tests ของ loader/host/approval (`ExtensionLoaderTests`, `ExtensionHostTests`, `ApprovalStoreTests`, `ExtensionApprovalTests`) ด้วยโฟลเดอร์ชั่วคราว |
| ACL ของ approval store | เขียนได้เฉพาะ Administrators/SYSTEM | unit tests ใช้ checker/securer ที่ฉีดเข้าไป; ACL ของ `%ProgramData%` บนเครื่องจริง **ยังไม่ยืนยัน** |
| กัน reparse point/junction | ปฏิเสธ path ที่เป็นลิงก์ | เทสต์ junction ใน `ExtensionLoaderTests`/`ApprovalStoreTests`/`InstallerExtensionTests`; การสร้าง file symlink ทำไม่ได้บนเครื่องพัฒนา จึงมี 2 เทสต์ถูกข้าม |
| Latency / availability / throughput | ไม่ได้กำหนดเป้าหมาย | — |

## 11. Architecture decisions

| ADR | Decision | Status |
|---|---|---|
| `Docs/adr/0001-extension-architecture.md` — Extension architecture | แยก core/ส่วนเสริมด้วยสัญญา managed .NET เดียว (`SysMonitor.Extensibility`), โหลดจากโฟลเดอร์ใต้ exe ด้วย collectible `AssemblyLoadContext`, ตรวจ SHA-256 ของ manifest และทุกไฟล์ก่อนรันโค้ด, ความเชื่อถือสามชั้น (first-party ที่ฝังใน exe / อนุมัติโดยผู้ใช้ใน `%ProgramData%` / ไม่เชื่อถือ), elevated โหลดเฉพาะ first-party, ติดตั้ง per-user และบันทึกช่องโหว่ที่เหลือ (exe เขียนทับได้) ว่าเป็นงานแยก R-022 | Accepted |
| `Docs/adr/0002-machine-wide-install.md` — Machine-wide install | ติดตั้งแบบ admin เป็นทางเลือก: `Program Files`, HKLM, Scheduled Task ระดับสูงสุด, ยกระดับด้วยการเปิดตัวเองซ้ำ (ไม่ใช้ manifest), ไม่ลบ/รัน/จัดเตรียมไฟล์ในที่ที่ผู้ใช้เขียนได้ด้วยสิทธิ์ผู้ดูแล, per-user ไม่เปลี่ยน | Accepted (ยังไม่ได้ตรวจบนเครื่องจริง) |

- สัญญาฉบับเต็ม (ฟิลด์, วิธีโหลด, ข้อผิดพลาด, สิทธิ์) อยู่ที่ `Docs/EXTENSION_CONTRACT.md`

## 12. Known constraints and technical debt

ข้อจำกัดที่ยอมรับแล้ว (บันทึกแล้วใน `Docs/adr/0001-extension-architecture.md`):

1. ตัวช่วยอนุมัติเขียน Diag log แม้ผู้ใช้ปิด diagnostics — ผลกระทบ: มีร่องรอยการใช้งานเสมอเมื่ออนุมัติส่วนเสริม
2. ถ้า `IViewProvider.GetView` ค้างเกิน 5 วินาที โฮสต์ `Dispose` ส่วนเสริมต่อทั้งที่ยังมีผู้เรียกค้าง — ผลกระทบ: ส่วนเสริมที่ช้าเสี่ยงถูกเรียกหลัง dispose
3. ติดตั้งแบบ per-user: exe หลักอยู่ในโฟลเดอร์ที่ผู้ใช้เขียนได้ มัลแวร์สิทธิ์ผู้ใช้เขียนทับ exe ได้ — ปิดได้เฉพาะสำหรับผู้ที่เลือกติดตั้งแบบ admin (R-022, `Docs/adr/0002-machine-wide-install.md`) ส่วน per-user ยังเป็นค่าเดิม
4. ปุ่ม "รันใหม่แบบผู้ดูแลระบบ" ในหน้า settings ขึ้นเมื่อส่วนเสริม SMART มีสถานะ `Loaded` แม้ไม่มี `DiskInfoCore.dll` (โฮสต์ไม่รู้ว่าไฟล์ขาด) — และปุ่ม restart ใน sidebar หลักใช้ป้าย "รันใหม่แบบผู้ดูแลระบบ" ทุกครั้งที่ยังไม่ elevated โดยไม่ดูส่วนเสริมเลย
5. `ExtensionHost.Reload` ล้าง cached display name — ผลกระทบ: ชื่อที่แสดงอาจเปลี่ยนกลับเป็นชื่อใน manifest ชั่วขณะก่อน `GetView` รอบถัดไป
6. เทสต์ `ExtensionHost_SetEnabled_LeavesRealConfigUntouched` อ่าน `%APPDATA%\SystemMonitor\config.wpf.json` จริง (อ่านอย่างเดียว) — ผลกระทบ: เทสต์แตะโปรไฟล์ของผู้รัน
7. build ตรงด้วย IDE หรือ `dotnet build` โดยไม่ผ่าน `build-wpf.cmd` อาจฝัง `first-party-extensions.json` รุ่นเก่า — ผลกระทบ: exe ที่ได้อาจไม่รู้จักส่วนเสริม first-party รุ่นปัจจุบัน
8. รัน `SysMonitor.PackTool` สองตัวพร้อมกันด้วย allowlist ไฟล์เดียวกันอาจทำให้ hash หายหนึ่งรายการ (read–modify–write ไม่มี lock ข้ามโปรเซส) — สคริปต์จึงรันเรียงลำดับ
9. ไฟล์ในแพ็กเกจถูกล็อกตอนอัปเกรด → ส่วนเสริมนั้นแสดง `HashMismatch`/`UnsafePath` จนกว่าจะรันตัวติดตั้งซ้ำ
10. เทสต์ 2 ตัวถูกข้ามเพราะสร้าง file symlink ไม่ได้บนเครื่องพัฒนา — การป้องกัน file link จึงยังไม่ถูกพิสูจน์ด้วยของจริง
11. ไม่ทำตลาด/ดาวน์โหลด/อัปเดตส่วนเสริมอัตโนมัติ ไม่มี package ของบุคคลที่สาม และไม่ตรวจมัลแวร์ (หน้าที่ของแอนตี้ไวรัส)

หนี้ทางเอกสารและความไม่สอดคล้องที่ตรวจพบ:

- `Docs/TESTING.md` ยังเป็นแม่แบบที่เต็มไปด้วย `[PLACEHOLDER]` จึงยังไม่ได้เป็นแหล่งคำสั่งตรวจสอบจริง
- `Diag` เขียน log ที่ `%APPDATA%\SysMonitor\` แต่ `Installer.SettingsFiles` ระบุไฟล์ log ใต้ `%APPDATA%\SystemMonitor\` (§7)
- `SysMonitor.Wpf/README.md` ยังระบุ config อยู่ที่ `%APPDATA%\SysMonitor\config.wpf.json` ซึ่งไม่ตรงกับ `AppConfig.Path`

งานที่ยังไม่ทำ (ห้ามถือว่าเสร็จ): ตรวจ R-022 บนเครื่องจริง (UAC, Scheduled Task, ACL ของ Program Files/ProgramData, การย้ายและการถอนจริง) · เทียบอุณหภูมิ ±2 °C กับ CrystalDiskInfo (R-019/R-020) · ตรวจ ACL ของ `%ProgramData%\SystemMonitor` จริง · ลองอนุมัติส่วนเสริมผ่าน UAC จริง
