# EXTENSION_CONTRACT.md — สัญญาส่วนเสริม version 1.1

เอกสารอ้างอิงเพียงฉบับเดียวของสัญญาส่วนเสริม (extension contract) ตามข้อกำหนด `Requirements.md` R-021 ข้อ "contract บันทึกเป็นเอกสารเดียว (เวอร์ชัน, วิธีโหลด, ข้อผิดพลาด, สิทธิ์)"

---

## 1. วัตถุประสงค์และขอบเขต

| ผู้อ่าน | ใช้ส่วนไหน |
|---|---|
| ผู้เขียนส่วนเสริม (extension author) | §2–§5, §11 — วิธีทำให้แพ็กเกจผ่านตัวโหลด, สมาชิกที่ต้อง implement, ข้อจำกัดที่โฮสต์บังคับ |
| ผู้ดูแลโฮสต์ (host maintainer) | §6–§10, §12 — ลำดับการตรวจตอนโหลด, การแยกโหลด-context, โมเดลความเชื่อถือ, lifecycle |

 contract อยู่ในโปรเจกต์ `SysMonitor.Extensibility` (net8.0, **ไม่มี package reference ใด ๆ** — `SysMonitor.Extensibility/SysMonitor.Extensibility.csproj`) ตัวโหลดและนโยบายความเชื่อถืออยู่ใน `SysMonitor.Wpf/Extensions/`

ขอบเขตของสัญญานี้เป็นของ **SystemMonitor เท่านั้น** โปรเจกต์ DiskInfo **ใช้ร่วมกันเฉพาะ `DiskInfoCore.dll`** (native core อ่าน SMART) และไม่ใช้ตัวโหลดหรือสัญญานี้ของ SystemMonitor แต่อย่างใด (ยืนยันจากข้อความหัวข้อ R-021 ใน `Requirements.md` บรรทัดที่ 111: "ใช้ร่วมกันเฉพาะ `DiskInfoCore.dll`") เอกสาร ABI ของ `DiskInfoCore.dll` อยู่ที่ repo DiskInfo `docs/CORE_ABI.md` ซึ่งอยู่นอก repo นี้ — **ยังไม่ยืนยันรายละเอียดจากโค้ดในเครื่องนี้**

ไฟล์ที่เป็น contract จริง: `SysMonitor.Extensibility/*.cs` · ตัวโหลด: `SysMonitor.Wpf/Extensions/ExtensionLoader.cs` ·โฮสต์: `SysMonitor.Wpf/Extensions/ExtensionHost.cs`

---

## 2. การกำหนดเวอร์ชัน (Versioning)

- `SysMonitor.Extensibility/ContractInfo.cs:Version` = **1.1** (`new Version(1, 1)`), มี alias `ContractInfo.Current`
- manifest ระบุเวอร์ชันที่ส่วนเสริมต้องการในฟิลด์ `contract` (§4)
- **กฎความเข้ากันได้** (บังคับซ้ำสองที่: `ExtensionLoader.LoadSingleFolder` ขั้นที่ 4 และ `ExtensionPacker.Pack`)

```
manifest.contract.Major ต้องเท่ากับ ContractInfo.Version.Major
และ manifest.contract.Minor ต้อง <= ContractInfo.Version.Minor
```

ไม่ผ่าน → `ExtensionStatus.ContractMismatch` (ตอนโหลด) หรือ `PackException` (ตอนแพ็ก) ผลคือ minor ของโฮสต์รองรับส่วนเสริมที่เขียนตาม 1.0 และ 1.1 ทั้งคู่; ส่วนเสริมที่อ้าง 1.2 หรือ 2.x ถูกปฏิเสธทันที

-เวอร์ชันของตัวโฮสต์เอง (`SysMonitor.Wpf.csproj:Version` = 4.2.2) เป็นคนละเรื่องกับเวอร์ชัน contract และถูกตรวจแยกผ่าน `min_host` (§4, §6 ขั้น 5)
- `SysMonitor.Extensibility.csproj` ตั้ง `AssemblyVersion`/`FileVersion` ค้างที่ `1.0.0.0` โดยเจตนา (คอมเมนต์ในไฟล์: เพิ่มเฉพาะ major breaking change) เพื่อให้ assembly identity ของ contract คงที่
- **สิ่งที่เปลี่ยนจาก 1.0 → 1.1** (ยืนยันจากรายงานเฟส 3 ใน `Requirements.md`): เพิ่ม `IDemandAware` + `ExtensionDemand`, เพิ่ม `IDiskTemperatureSource`, เพิ่มฟิลด์ manifest `provides` และ `place_after` · ส่วนที่คงอยู่แต่เดิม (IExtension / IExtensionContext / IViewProvider / ExtensionView / Severity) — **ยังไม่ยืนยัน** เพราะ repo ไม่มี changelog หรือ contract 1.0 ให้เทียบ

---

## 3. โครงสร้างแพ็กเกจ

| เรื่อง | รายละเอียด (ที่มา) |
|---|---|
| ตำแหน่ง root | `<โฟลเดอร์ exe>\extensions\` — `SysMonitor.Wpf/App.xaml.cs:OnStartup` ใช้ `Path.Combine(AppContext.BaseDirectory, "extensions")` |
| โฟลเดอร์ต่อส่วนเสริม | `extensions\<id>\` หนึ่งโฟลเดอร์ = หนึ่งส่วนเสริม |
| ชื่อโฟลเดอร์ | ต้องตรงกับ `id` ใน manifest **แบบ case-sensitive** (`StringComparison.Ordinal`) |
| ไฟล์ที่อนุญาต | `extension.json` + ไฟล์ที่ลงรายการใน `files[]` เท่านั้น ทุกไฟล์ย่อยทั้ง subtree (`ExtensionLoader` ขั้น 6b) |
| ไฟล์ที่ไม่ได้ลงรายการ | ถูกปฏิเสธทั้งแพ็กเกจ → `UnsafePath` (ข้อความ `Extension directory contains unlisted file ...`) |
| link / junction | ห้าม: ตัวโฟลเดอร์, โฟลเดอร์ย่อยทุกชั้น, และไฟล์ทุกไฟล์ ถ้าเป็น reparse point → `UnsafePath` |
| subfolder | ได้ ถ้าไฟล์ข้างในถูกระบุใน `files[]` ด้วย relative path และไม่มีชั้นใดเป็น reparse point |

การตรวจ "ไฟล์ที่ไม่ได้ลงรายการ" เป็นเหตุผลว่าทำไมตัวติดตั้งจึงต้องลบไฟล์เก่าของแพ็กเกจก่อนเขียน และทำไมไฟล์ที่ถูกโปรแกรมอื่นล็อกค้างไว้ทำให้ส่วนเสริมแสดง `HashMismatch`/`UnsafePath` จนกว่าจะรันตัวติดตั้งซ้ำ (§13)

เจ้าของโฟลเดอร์: ตัวติดตั้ง (per-user ที่ `%LOCALAPPDATA%\Programs\SystemMonitor` หรือแบบ admin ที่ `%ProgramFiles%\SystemMonitor`) เป็นผู้เขียน/ถอน `extensions\<id>\` เอง ตรวจความเป็นเจ้าของ โต้แย้ง junction/ลิงก์/ชื่อไฟล์แปลก และเขียน `extension.json` เป็นไฟล์สุดท้าย (`SysMonitor.Setup/Installer.cs:WriteExtensions`, `UninstallExtensions`) ส่วนเสริมของบุคคลที่สามไม่ถูกแตะตอนถอนโปรแกรม; ในโหมด admin ผู้ดูแลระบบคัดลอกส่วนเสริมของบุคคลที่สามเข้า `extensions\<id>\` ใต้ Program Files เองแล้วใช้การอนุมัติเดิม

---

## 4. Schema ของ manifest `extension.json`

จำแนกและตรวจที่ `SysMonitor.Wpf/Extensions/ExtensionManifest.cs` (อ่าน JSON แบบ snake_case, ตัวพิมพ์ใหญ่-เล็กของ key ไม่สำคัญ, อนุญาต trailing comma และ comment)

| ฟิลด์ | ชนิด | จำเป็น | กฎการตรวจ (จากโค้ด) |
|---|---|---|---|
| `id` | string | ✅ | regex `^[a-z0-9][a-z0-9._-]{0,63}\z` (ยาว ≤64, ขึ้นต้นด้วยตัวพิมพ์เล็ก/เลข) และต้องตรงกับชื่อโฟลเดอร์ (§3) |
| `name` | string | ✅ | ไม่เปล่า/ไม่ช่องว่างล้วน |
| `version` | string | ✅ | ไม่เปล่า; เป็นตัวอักษรอิสระ (โฮสต์ไม่ parse) — ใช้แสดงในแถบข้าง |
| `contract` | string | ✅ | parse ได้เป็น `System.Version` และผ่านกฎ §2 |
| `min_host` | string | ❌ | ถ้ามามีอยู่ต้อง parse ได้เป็น `Version`; เทียบกับรุ่นโฮสต์แบบ normalize (`Build`/`Revision` ที่ `-1` → `0`) |
| `entry` | object | ✅ | ต้องมีทั้ง `entry.assembly` และ `entry.type` (ไม่เปล่า) |
| `entry.assembly` | string | ✅ | ต้องตรงกับ `path` ไฟล์หนึ่งใน `files[]` (เทียบแบบ case-insensitive) และไฟลน์นั้นต้อง **ไม่** เป็น native |
| `entry.type` | string | ✅ | full type name ที่ `GetType()` หาพบและ implement `IExtension` + มี public parameterless constructor |
| `provides` | string[] | ❌ (default `[]`) | ≤8 รายการ, แต่ละรายการ regex `^[a-z0-9][a-z0-9-]{0,31}\z`, ห้ามซ้ำ (case-sensitive) · โฮสต์รู้จักค่า `disk-temperature` เพียงค่าเดียวที่มีผลจริง (KeepLoaded, §8) — ค่าอื่นเป็นการประกาศเท่านั้น |
| `place_after` | string | ❌ | ถ้ามีต้องเป็น `cpu` \| `ram` \| `disk` \| `gpu` \| `network` เท่านั้น (§10) |
| `files` | object[] | ✅ | อย่างน้อย 1 รายการ, ไม่มี null |
| `files[].path` | string | ✅ | ไม่เปล่า; ห้ามซ้ำทั้ง normalized path **และ** ทั้งชื่อไฟล์ (case-insensitive) · ฝั่งตัวโหลด: ห้ามมี `:`, ห้ามขึ้นต้น `/` หรือ `\`, ห้าม `Path.IsPathRooted`, ห้าม segment `.`/`..`, resolve แล้วต้องอยู่ใต้โฟลเดอร์ส่วนเสริม |
| `files[].sha256` | string | ✅ | regex `^[a-fA-F0-9]{64}$` (เทียบค่าจริงแบบ case-insensitive) |
| `files[].native` | bool | ❌ (default `false`) | `true` = ดีลเป็น native DLL (โหลดด้วย `LoadLibraryEx`, ห้ามเป็น entry assembly) |

### ตัวอย่าง manifest ที่ผ่านจริง (รูปแบบของ SMART, SHA-256 ใส่ค่าจริงโดย PackTool)

```json
{
  "id": "smart",
  "name": "SMART",
  "version": "4.2.2",
  "contract": "1.1",
  "min_host": "4.2.0",
  "entry": {
    "assembly": "SysMonitor.Ext.Smart.dll",
    "type": "SysMonitor.Ext.Smart.SmartExtension"
  },
  "provides": [
    "disk-temperature"
  ],
  "place_after": "disk",
  "files": [
    { "path": "SysMonitor.Ext.Smart.dll", "sha256": "<64 hex>", "native": false },
    { "path": "DiskInfoCore.dll", "sha256": "<64 hex>", "native": true }
  ]
}
```

### template vs final manifest

แพ็กเกจที่ตรวจจริงตอนโหลดคือ `extension.json` ซึ่ง **สร้างโดยเครื่องมือ ไม่ใช่เขียนด้วยมือ**

| ไฟล์ | ใครเขียน | ต่างกันอย่างไร |
|---|---|---|
| `extension.template.json` | ผู้เขียนส่วนเสริม | ระบุ `"version": "$VERSION"`, **ไม่มี `sha256`**, มีฟิลด์เพิ่ม `optional` (เฉพาะฝั่ง PackTool), **ห้าม** ระบุ `extension.json` ใน `files[]` |
| `extension.json` | `SysMonitor.PackTool` | แทนค่า `$VERSION` ด้วย `--version`, คัดลอกรายการไฟล์ที่มี source จริงเท่านั้น (ไฟล์ `optional` ที่ไม่ส่งมาถูกตัดออก), คำนวณ SHA-256 ใส่ทุกไฟล์ แล้วตรวจซ้ำด้วย `ExtensionManifest.TryParse` ก่อนเขียน |

PackTool ตรวจล่วงหน้าก่อนสร้างแพ็กเกจ: path ต้อง relative ล้วน, ห้ามมี `:`, entry assembly ต้องถูกระบุและไม่ native/ไม่ optional และ PackTool เพิ่มข้อห้ามว่า segment ของ path ห้ามลงท้ายด้วย `.` หรือช่องว่าง ซึ่งตัวโหลดไม่มีกฎข้อนี้โดยตรง (ตัวโหลดปฏิเสธเรื่องนี้ด้วยเหตุผลชั้นอื่น) (`SysMonitor.PackTool/ExtensionPacker.cs`)

ตัวอย่าง template จริง: `SysMonitor.Ext.Smart/extension.template.json` · `SysMonitor.Ext.Sample/extension.template.json`

---

## 5. Entry point และ interfaces

ทุกชนิดอยู่ใน namespace `SysMonitor.Extensibility`

### 5.1 จำเป็น — `IExtension` (`IExtension.cs`)

| สมาชิก | ความหมาย |
|---|---|
| `void Initialize(IExtensionContext context)` | ตัวโหลดเรียก **ครั้งเดียว** บน background thread หลังสร้าง instance สำเร็จ ถ้า method นี้โยน exception ทั้งการโหลดล้มเหลว → `LoadFailed` |
| `void Shutdown()` | ปลดทรัพยากร managed/native; โฮสต์เรียกตอน retire ส่วนเสริมและ **ดูดับ exception** (log แล้วไปต่อ) |

ต้องมี public parameterless constructor (`ExtensionLoader` เรียก `Activator.CreateInstance`)

### 5.2 `IExtensionContext` (`IExtensionContext.cs`) — สิ่งที่โฮสต์ให้ส่วนเสริมอ่าน

| สมาชิก | ความหมาย |
|---|---|
| `string ExtensionDirectory` | absolute/canonical path ของโฟลเดอร์ส่วนเสริม (`Path.GetFullPath`) — เจ้าของคือระบบติดตั้ง ไม่ควรเขียนทับไฟล์ตัวเอง |
| `string Language` | `th` หรือ `en` ตามภาษาของโฮสต์ **ค่า snapshot ตอน Initialize** ไม่ update เอง — อ่านภาษาปัจจุบันจากพารามิเตอร์ของ `GetDisplayName`/`GetView` |
| `bool HostIsElevated` | โฮสต์รันด้วยสิทธิ์ผู้ดูแลระบบหรือไม่ (§8) |
| `Version HostContractVersion` | เวอร์ชัน contract ของโฮสต์ (= `ContractInfo.Version`) |
| `void Log(string message)` | เขียนเข้าสู่ diagnostics log ของโฮสต์ |

### 5.3 `IViewProvider` (`IViewProvider.cs`) — optional, ทำให้ส่วนเสริมมีหน้าจอ

| สมาชิก | ความหมาย / ข้อสัญญา |
|---|---|
| `string GetDisplayName(string language)` | ชื่อที่แสดงในแถบข้าง; ค่าว่าง/`null` → โฮสต์ถอยไปใช้ `name` จาก manifest |
| `ExtensionView GetView(string language)` | **ต้องคืนทันทีและไม่ block** และ **ต้อง thread-safe** (คอมเมนต์ในไฟล์ระบุไว้ตรง ๆ) — โฮสต์เรียกจาก sampler background thread เป็นรอบ ๆ; ห้าม query ฮาร์ดแวร์หนัก ๆ ในนี้ ให้เก็บ snapshot ไว้แล้วคืนค่าที่ cache แล้ว |

ข้อจำกัดจริง: ถ้า `GetView` ค้าง โฮสต์จะรอ retire ได้ไม่เกิน 5 วินาทีแล้ว dispose ต่อ (§9, §13 ข้อ 2)

### 5.4 `IDiskTemperatureSource` (`IDiskTemperatureSource.cs`) — optional

`IReadOnlyDictionary<int,int> GetDiskTemperatures()` — คืน อุณหภูมิ °C keyed ด้วย **physical drive index เริ่มที่ 0** · interface ระบุว่าโฮสต์รับ drive `0..255` และ °C `1..149` รายการที่นอกขอบนี้จะถูกทิ้ง

โฮสต์บังคับจริงใน `ExtensionHost.GetDiskTemperatures()`:
- รับเฉพาะ `drive is >= 0 and <= 255` และ `celsius is >= 1 and <= 149`
- อ่านเข้าไปได้ไม่เกิน **256 รายการ** ต่อส่วนเสริม และเก็บค่าที่ใช้ได้ไม่เกิน **64 รายการ** ต่อส่วนเสริม
- วนตามลำดับ **id แบบ ordinal** — ส่วนเสริมแรกที่รายงาน drive ใดชนะ (`TryAdd`)
- ข้ามส่วนเสริมที่ `IDemandAware` ยังไม่ได้รับ demand เลย (`DemandApplied == false`)

### 5.5 `IDemandAware` + `ExtensionDemand` (`ExtensionDemand.cs`) — optional แต่แนะนำให้ทำ

`void SetDemand(ExtensionDemand demand)` — โฮสต์เรียกทุกครั้งที่ความสนใจเปลี่ยน และ **เรียกอย่างน้อยหนึ่งครั้งก่อนอ่าน view/metric ครั้งแรก**

| flag | ความหมาย |
|---|---|
| `None = 0` | ไม่มีอะไรต้องใช้ — ส่วนเสริมควรหยุด polling พื้นหลัง |
| `Views = 1 << 0` | โฮสต์กำลังวาดหรือสลับไปหน้าของส่วนเสริมนี้ —应保持 view model สดใหม่ |
| `DiskTemperature = 1 << 1` | โฮสต์ต้องการอุณหภูมิดิสก์มาเสริมค่า storage |

การ map ฝั่งโฮสต์ (`ExtensionHost.PushDemand`): `Views` ส่งเมื่อกับดัก `enabled` ใน config เปิดอยู่ · `DiskTemperature` ส่งตาม `Sampler` ขอ (มีดิสก์ที่ค่าจาก storage stack ไม่น่าเชื่อถือ) · demand ที่ส่งสำเร็จและไม่เปลี่ยนจะไม่ถูกส่งซ้ำ (ถ้าการส่งล้มเหลวจะส่งใหม่ได้) และ delivery ต่อ instance ถูกล็อกด้วย `DemandGate` เฉพาะ slot นั้น

### 5.6 `ExtensionView` และ records (`ExtensionView.cs`)

`ExtensionView(WidgetPages, Overview, Tabs)` — snapshot ทั้งหมดของส่วนเสริม; มี `ExtensionView.Empty` สำหรับ "ไม่มีอะไรแสดง"

| record | ฟิลด์ | ใช้ที่ |
|---|---|---|
| `WidgetPage` | `Title`, `Verdict`, `Line`, `TemperatureC?`, `Percent`, `Severity`, `Message?` | หน้าที่ widget แบบหมุนหน้า; ถ้า `Message` ไม่เปล่า จะแสดงข้อความแทนแถบวัด |
| `OverviewRow` | `Title`, `Detail`, `ValueText`, `Percent`, `Severity`, `TemperatureC?` | หนึ่งแถวใน section ภาพรวม |
| `OverviewSection` | `Note`, `Rows[]` | section ของส่วนเสริมในภาพรวม (เป็น `null` = ไม่มี section) |
| `DetailTab` | `Key`, `Title`, `Subtitle`, `Detail`, `Hardware`, `Facts[]` | หนึ่งแท็บในมุมมองเต็ม; `Key` ต้องไม่ซ้ำ (โฮสต์ทิ้งแท็บที่ key ว่างหรือซ้ำ) และกลายเป็น `ext:<id>:<Key>` ใน FullTab |
| `Fact` | `Name`, `Value` | คู่ชื่อ–ค่าในแท็บ |

`Severity` (`Severity.cs`): `Neutral = 0`, `Good = 1`, `Caution = 2`, `Bad = 3`

### 5.7 ข้อจำกัดและการตรวจที่โฮสต์บังคับ (`ExtensionHost` constants + `SanitizeView`)

| เพดาน | ค่า | ทำอะไรเมื่อเกิน |
|---|---|---|
| `MaxWidgetPages` | 32 | ตัดทิ้งเฉพาะรายการแรก ๆ |
| `MaxOverviewRows` | 64 | ตัดทิ้ง |
| `MaxDetailTabs` | 32 | ตัดทิ้ง (key ว่าง/ซ้ำถูกทิ้งก่อน) · สแกนได้ไม่เกิน 128 รายการ |
| `MaxFactsPerTab` | 64 | ตัดทิ้ง (สแกนได้ไม่เกิน 256) |
| `MaxTextLength` | 512 ตัวอักษร | **ตัดท้าย** ทุกสตริงรวมชื่อเครื่อง (`SanitizeText`) |
| อุณหภูมิใน view (`MinTemperatureC`/`MaxTemperatureC`) | −50 … 150 °C | **ตั้งเป็น `null`** ไม่ใช่ทิ้งแถว |
| `Percent` ใน view | 0 … 100 | `Math.Clamp` |

> หมายเหตุสำคัญสำหรับผู้เขียน: ขอบอุณหภูมิของ view (−50…150) **คนละค่า** กับขอบของ `GetDiskTemperatures()` (1…149) ดู §5.4

### 5.8 `StringTable` (`StringTable.cs`) — helper ภาคสมัครใจ

คลาสช่วยแปลหลายภาษาใน assembly ของส่วนเสริมเอง: `Get(language, key)` lowercase ค่า language ก่อนค้น, fallback ไป `"en"`, ไม่เจอเลยคืน **ตัว key เอง** · static `MissingKeys(table)` ใช้ test parity ระหว่างภาษา (ไม่ใช่ API ฝั่งโฮสต์)

---

## 6. ลำดับการตรวจตอนโหลด

`ExtensionLoader.LoadAll` วนโฟลเดอร์ย่อยของ root เรียงตามชื่อโฟลเดอร์แบบ case-insensitive และรวมผลทั้งหมด; `LoadSingleFolder` ตรวจตามลำดับต่อไปนี้ (ตัวเลขในคอมเมนต์源码ตรงกับที่สรุป)

| # | การตรวจ | สถานะเมื่อไม่ผ่าน |
|---|---|---|
| 1 | root folder มีอยู่และไม่ใช่ reparse point; โฟลเดอร์ย่อยทุกชั้นทั้ง subtree ก็ห้ามเป็น reparse point | `UnsafePath` |
| 2 | หา/อ่าน/parse `extension.json` และคำนวณ SHA-256 ของไฟล์ manifest | `ManifestInvalid` |
| 3 | ชื่อโฟลเดอร์ == `id` (case-sensitive) | `ManifestInvalid` |
| 4 | contract: major เท่ากัน และ minor ของ manifest ≤ minor ของโฮสต์ | `ContractMismatch` |
| 5 | `min_host` ≤ รุ่นโฮสต์ (เทียบแบบ normalize) | `HostTooOld` |
| 6 | path ทุกไฟล์ใน `files[]` ปลอดภัย + **ไม่มีไฟล์ที่ไม่ได้ลงรายการในโฟลเดอร์** + ไม่มีไฟล์เป็น link | `UnsafePath` |
| 7 | ไฟล์ที่ประกาศทุกไฟล์ต้องมีอยู่บนดิสก์ | `FileMissing` |
| 8 | การตัดสินความเชื่อถือ (§8): elevated รับเฉพาะ first-party; ปกติ รับ first-party หรือที่อนุมัติแล้ว | `NotTrustedWhenElevated` / `NotApproved` |
| 8b | ปิดสวิตช์ใน config อยู่หรือไม่ — ยกเว้นเข้าเกณฑ์ KeepLoaded (§8) | `Disabled` |
| 9 | **อ่านทุกไฟล์หนึ่งครั้งและตรวจ SHA-256 ให้ครบก่อนโหลดโค้ดใด ๆ**; ไฟล์ native ถูกเปิดค้างด้วย `FileShare.Read` จนโหลดเสร็จ | `HashMismatch` (อ่านไฟล์ไม่สำเร็จ → `LoadFailed`) |
| 10 | `entry.assembly` ต้องอยู่ใน `files[]` และไม่ใช่ native | `EntryNotFound` |
| 11 | ห้ามมี `id` ซ้ำในรอบเดียวกัน (`loadedIds`) — ตรวจหลังขั้น 10 และข้ามเมื่อโหลดโฟลเดอร์เดี่ยว (`LoadFolder`/`Inspect` ส่ง `loadedIds = null`) | `DuplicateId` |
| — | preload native ที่ตรวจ hash แล้วด้วย `LoadLibraryEx` (ค้นจากโฟลเดอร์นี้ + System32 เท่านั้น) | `LoadFailed` |
| 12 | สร้าง `ExtensionLoadContext`, โหลด entry assembly จาก byte array, หา `entry.type` และ implement `IExtension`, `Activator.CreateInstance`, สร้าง `ExtensionContext`, เรียก `Initialize` | `EntryNotFound` (ไม่เจอ type / ไม่ใช่ `IExtension`) หรือ `LoadFailed` (instance สร้างไม่ได้, `Initialize` โยน, อ่านไฟล์ไม่สำเร็จ, เหตุการณ์ไม่คาดคิดอื่น) |

ผลที่ล้มเหลวมี `ManifestSha256` ก็ต่อเมื่ออ่าน manifest ได้แล้ว และมี `Manifest` ก็ต่อเมื่อ parse ผ่านแล้ว (ก่อนหน้านั้นเป็น null) · `LoadAll` ดักข้อผิดพลาดของการโหลดทั้งหมด แต่โยน `ArgumentNullException` เมื่อ `options` เป็น null · `ExtensionLoader.Inspect` รันขั้นเดียวกันแต่หยุดหลังขั้น 9 โดยข้าม 8, 8b, 11 และไม่โหลดโค้ด (ใช้โดยตัวช่วยอนุมัติ)

### ตารางค่า `ExtensionStatus` ทั้งหมด (`ExtensionStatus.cs`)

| ค่า | ความหมาย |
|---|---|
| `Loaded` | โหลดสำเร็จ (ในโหมด `Inspect` = โครงสร้างถูกและ hash ตรงครบทุกไฟล์ แต่ยังไม่ได้โหลดโค้ด) |
| `ManifestInvalid` | `extension.json` หาย/อ่านไม่ได้/parse ไม่ผ่าน/validate ไม่ผ่าน/ชื่อโฟลเดอร์ไม่ตรงกับ `id` |
| `ContractMismatch` | `contract` ไม่เข้ากฎ §2 |
| `HostTooOld` | `min_host` สูงกว่ารุ่นโฮสต์ |
| `FileMissing` | ไฟล์ที่ประกาศใน `files[]` ไม่มีอยู่บนดิสก์ |
| `HashMismatch` | SHA-256 ของไฟล์จริงไม่ตรงกับ manifest |
| `UnsafePath` | โฟลเดอร์/ไฟล์อยู่นอก root, เป็น reparse point, มี path อันตราย, หรือมีไฟล์ที่ไม่ได้ลงรายการ |
| `NotApproved` | โฮสต์โหมดปกติ และแพ็กเกจ neither first-party nor approved |
| `NotTrustedWhenElevated` | โฮสต์ elevated แต่แพ็กเกจไม่ใช่ first-party |
| `DuplicateId` | มี `id` นี้ถูกโหลดไปแล้วในรอบเดียวกัน |
| `EntryNotFound` | entry assembly ไม่ถูกระบุ/เป็น native, หรือหา `entry.type` ไม่พบ/ไม่ implement `IExtension` |
| `LoadFailed` | ผิดพลาดช่วงอ่านไฟล์, preload native, สร้าง instance, หรือ `Initialize` โยน exception |
| `Disabled` | ผู้ใช้ปิดสวิตช์ใน config และไม่เข้าเกณฑ์ KeepLoaded |

---

## 7. การแยกโหลด (`ExtensionLoadContext`)

`SysMonitor.Wpf/Extensions/ExtensionLoadContext.cs` — `internal sealed`, สืบทอด `AssemblyLoadContext`

- `isCollectible: true` ชื่อบริบท `Extension_<id>` → ปลดโหลดได้จริงเมื่อ retire
- **โหลด managed assembly ที่ลงรายการในแพ็กเกจจาก byte array ที่ตรวจ hash แล้วเท่านั้น** (`LoadFromStream`) ไม่เปิดไฟล์จากดิสก์ซ้ำ ⇒ กันสลับไฟล์หลังตรวจ
- `Load()` คืน `null` สำหรับ `SysMonitor.Extensibility` โดยเจตนา เพื่อให้ **แชร์ assembly เดียวกับโฮสต์** → type identity ใช้ได้จริง (`is IViewProvider` ทำงานข้าม boundary)
- assembly อื่นที่ resolution ไม่พบในแพ็กเกจ → `null` ให้ fall back ไปยังระบบ (BCL ของโฮสต์) ส่วน native ที่ลงรายการใช้ handle ที่ preload จากไฟล์ที่ตรวจ hash แล้ว ชื่อ native ที่ไม่อยู่ในรายการ `LoadUnmanagedDll` คืน `IntPtr.Zero` แล้วให้ runtime หาตามปกติ (ไลบรารีของระบบ เช่น kernel32 ใช้ได้)
- ลำดับเวลาสำคัญ: ไฟล์ native ถูกเปิดค้างด้วย `FileShare.Read` ตั้งแต่ตอน hash จนโหลดเสร็จ เพื่อไม่ให้เขียนทับหรือลบได้ในช่องว่านั้น และ **ไม่รัน native code ใด ๆ จนกว่าทุกไฟล์จะผ่าน hash ครบ** เพราะ import ของ native อาจ resolve จากโฟลเดอร์นี้

---

## 8. ความเชื่อถือและสิทธิ์

โมเดลนี้เป็นการตัดสินใจของผู้ใช้ (2026-10-13/14, `Requirements.md` R-021) แบบ trust-on-first-use แบ่งเป็นชั้น:

### 8.1 first-party allowlist

- ค่าที่ใช้ตัดสินคือ **SHA-256 ของไฟล์ `extension.json`** ไม่ใช่ของ DLL
- ฝังใน executable เป็น embedded resource ชื่อ `first-party-extensions.json` (`SysMonitor.Wpf.csproj` include when exists) และอ่านด้วย `FirstPartyAllowlist.FromAssembly` (ไม่มี resource → allowlist ว่างโดยไม่ log · parse ผิดพลาด → allowlist ว่างและ log ข้อผิดพลาด)
- ไฟล์นี้ถูกรวมโดย `ExtensionPacker.UpdateAllowlist`: array ของ hash เรียง ordinal, UTF-8 no BOM, merge ค่าเดิมแบบไม่ซ้ำ

### 8.2 พฤติกรรมตามโหมดของโฮสต์ (`ExtensionLoader` ขั้น 8)

| โฮสต์ | โหลดอะไรได้ | สถานะเมื่อไม่เข้าเกณฑ์ |
|---|---|---|
| **elevated** | เฉพาะ first-party เท่านั้น (คำอนุมัติของผู้ใช้ **ไม่** มีผล) | `NotTrustedWhenElevated` |
| **ปกติ** | first-party หรือ id+hash ที่มีอยู่ใน approval store | `NotApproved` |

เหตุผล: โค้ดที่โหลดตอนรันเป็น admin ย่อมรันเป็น admin ตาม ⇒ ปิดทางให้แพ็กเกจที่ไม่รู้จักถูกโหลดในบริบทที่มีสิทธิ์สูง

### 8.3 approval store

`%ProgramData%\SystemMonitor\approved-extensions.json` (`ApprovalStore.DefaultPath`) — JSON array ของ `{ id, name, manifest_sha256, approved_at }` (snake_case, `WriteIndented`)

เงื่อนไขที่บังคับทั้งตอนอ่านและตอนเขียน (`ApprovalStore` + `AclPolicy`):
- ไม่มี reparse point ที่ไฟล์หรือ ancestor ใด ๆ (`HasReparsePointInPath`)
- owner ต้องเป็น `BUILTIN\Administrators` หรือ `SYSTEM` (`IsAdminOrSystemOwner`)
- สิทธิ์เขียน/แก้ไข/ลบ/change-permissions/take-ownership ต้องจำกัดที่ Administrators + SYSTEM เท่านั้น (`IsAdminOnlyWritable` ตรวจทั้ง parent และ ancestor ทุกชั้น; `AncestorForbiddenRights` ห้าม standard user ลบ/เปลี่ยนเจ้าของ/แก้ ACL ของโฟลเดอร์ด้านบน)
- ไฟล์ไม่ผ่านเกณฑ์ → **ถือว่า store ว่าง** (fail closed) และ log ไว้ ไม่ใช่เงียบ
- ตอนเขียน: ใช้ lock file `<store>.lock` แบบ exclusive ข้าม process (timeout 10 วิ), เขียนลง `appr_<guid>.tmp` → ตั้ง ACL admin-only → `File.Move(..., overwrite: true)`; โฟลเดอร์ไม่มีอยู่ → สร้างแล้วตั้ง `SetAdminOnlyWritableDirectory` ถ้าตั้งไม่สำเร็จลบลูกค้าที่สร้างค้างทิ้ง
- ไม่เก็บใน `%APPDATA%` เพราะมัลแวร์สิทธิ์ผู้ใช้เดียวกันจะเขียนเองได้

### 8.4 ตัวช่วยอนุมัติ (one-time elevated helper)

ปุ่ม **"อนุมัติ..."** ในแถบข้าง (`MainWindow.xaml.cs` → `ExtensionApproval.CreateStartInfo`) เปิด `SystemMonitor.exe` ตัวเดียวกันด้วย `Verb = "runas"` โดย:

```
SystemMonitor.exe --approve-extension <id> <manifest-sha256>
```

`TryParse` รับ switch นี้เป็น 3 argument ท้ายสุดเท่านั้น (ห้ามมี switch ซ้ำ), `id` ต้องผ่าน regex `^[a-z0-9][a-z0-9._-]{0,63}\z` และ hash ต้องยาว 64 hex

ลำดับงานของ `ExtensionApproval.Run` (ไม่โหลดโค้ดส่วนเสริมเลย — ใช้ `ExtensionLoader.Inspect`) และ exit code (`SysMonitor.Wpf/Extensions/ExtensionApproval.cs`):

| exit | ความหมาย |
|---|---|
| `0` `ExitApproved` | อนุมัติสำเร็จ (รวมถึงกรณี first-party ซึ่งไม่ต้องเขียน store) |
| `1` `ExitBadArguments` | argument ผิดรูปแบบ/ตำแหน่ง |
| `2` `ExitDeclined` | ผู้ใช้กดไม่ยอมรับกล่องยืนยัน |
| `3` `ExitInspectFailed` | ตรวจแพ็กเกจไม่ผ่าน, SHA-256 ที่คำนวณได้ไม่ตรงกับที่ส่งมา, หรือ exception ไม่คาดคิด |
| `4` `ExitWriteFailed` | เขียน store ไม่สำเร็จ (ACL, busy ฯลฯ) — รายงานผู้ใช้ ไม่ทำเงียบ |
| `5` `ExitNotElevated` | กระบวนการไม่ได้ยกระดับสิทธิ์ |

กล่องยืนยันแสดง id, ชื่อ/รุ่นที่ sanitize (ตัด control/format char, ย่อ 60/32 ตัวอักษร), hash 12 ตัวอักษรแรก และรายการไฟล์ (สูงสุด 10 + `(+N more)`) พร้อมคำเตือนเพิ่มถ้ามีไฟล์ native (`SysMonitor.Wpf/I18n.cs:ext_approve_*`) · โฮสต์เรียก `ExtensionHost.Reload(id)` เมื่อ exit 0

### 8.5 KeepLoaded สำหรับ `disk-temperature`

`ExtensionHost.IsKeptLoadedManifest` → `isFirstParty && provides contains "disk-temperature"` (**case-sensitive**, เฉพาะ first-party)

ผล: ส่วนเสริม first-party ที่ให้อุณหภูมิดิสก์ยังคงโหลดอยู่แม้ผู้ใช้ปิดสวิตช์ เพราะโฮสต์ยังต้องใช้ข้อมูลพื้นหลัง — สวิตช์จึงคุมแค่ **การแสดงผลหน้าจอ** เท่านั้น และ `CollectViews` ข้ามส่วนเสริมที่ไม่ได้ enabled ไว้ชัดเจน ส่วนเสริมบุคคลที่สามไม่ได้สิทธิ์นี้

### 8.6 สิ่งที่ส่วนเสริมสมมติได้/สมมติไม่ได้เกี่ยวกับสิทธิ์

| สมมติได้ | สมมติไม่ได้ |
|---|---|
| ถ้า `IExtensionContext.HostIsElevated == true` กระบวนการมีสิทธิ์ผู้ดูแลระบบ | ระบบจะยกระดับให้เอง — สัญญานี้ไม่มีการยกระดับอัตโนมัติ (`Requirements.md`: "ไม่ยกระดับสิทธิ์อัตโนมัติ (ส่วนเสริมไม่ได้สิทธิ์เพิ่มเอง)") |
| โฮสต์ elevated โหลดได้เฉพาะ first-party | ส่วนเสริมที่ได้รับอนุมัติแล้วจะได้รันในโหมด elevated |
| | การอ่าน SMART ต้อง admin — ส่วนเสริม `SysMonitor.Ext.Smart` ตรวจ `HostIsElevated` เองและให้ `DiskTemperature.Merge` ใช้ค่าจาก storage stack ก่อน (`SysMonitor.Wpf/Sensors/DiskTemperature.cs`) |
| `ExtensionDirectory` เป็น absolute path | ผู้ใช้เขียนทับไฟล์ในโฟลเดอร์นี้ไม่ได้ — การติดตั้งเป็น per-user จึงยังเขียนได้ (ข้อจำกัด §13 ข้อ 3) |

---

## 9. Lifecycle

โฮสต์ (`SysMonitor.Wpf/Extensions/ExtensionHost.cs`) ทำงานทุกอย่างบน background ผ่าน `Task.Run` + `_lifecycle` semaphore (count 1) เพื่อไม่บล็อก UI; ผลลัพธ์รวม (`Entries`) expose เป็น list ที่ swap แบบ volatile อ่านจากทุก thread ได้

| ขั้น | เกิดอะไรขึ้น |
|---|---|
| **Start** | `StartAsync()` → `ScanAndLoadAll()` → `ExtensionLoader.LoadAll` (hash + load + `Initialize` อยู่นอก `_lock`) → สร้าง `ExtensionSlot` ต่อตัวที่ `Loaded` → อ่าน `GetDisplayName` ครั้งแรกเพื่อ cache ชื่อ |
| **demand แรก** | หลัง swap slot เข้า `_slots` แล้ว `PushDemand(id)` ทุกตัว ⇒ `SetDemand` ถึงก่อน `GetView`/`GetDiskTemperatures` ครั้งแรกเสมอ |
| **slot ที่ถูกแทนที่/ปิดไป** | `RetireAndDispose(5s)` นอก `_lock` |
| **เปิด/ปิด (`SetEnabled`)** | เขียน `_enabled` + `config.Extensions[id]` ทันที แล้ว reconcile บน background · ถ้าเป็นเคส KeepLoaded: slot เดิมอยู่ต่อ แค่ push demand ใหม่แล้ว publish entries · ส่วน save ไฟล์ config เป็นหน้าที่ caller (โฮสต์ไม่ save เอง) |
| **Reload(id)** | จำลอง/ล้าง `_lastPushedDemand`, ถอด slot ออก, ล้าง `_cachedDisplayNames[id]`, retire ของเก่า (รอ `ReloadRetireWait` = **5 วินาที**), แล้ว `ReconcileExtension(id, refresh: true)` ซึ่งเรียก `LoadFolder` เพื่อ re-evaluate ความเชื่อถือเสมอ · id ที่ไม่รู้จัก = no-op |
| **อ่าน view** | `CollectViews(language)` — ข้าม slot ที่ยังไม่ `DemandApplied` หรือ `TryAcquire()` ไม่ผ่าน, เรียก `GetDisplayName` + `GetView`, sanitize, คืน `ExtensionViewData(id, displayName, view, PlaceAfter)`; exception จากส่วนเสริม → log ครั้งเดียวต่อ `(id, exception type)` ไม่ทำให้รอบอ่านล้ม |
| **อ่านอุณหภูมิ** | `GetDiskTemperatures()` — ใช้ snapshot `_sortedSlotsSnapshot` เรียง id ordinal (ดูขอบใน §5.4) |
| **Dispose / Shutdown** | `ExtensionHost.Dispose()` ตั้ง `_disposed`, รอ `_lifecycle` ไม่เกิน 5 วิ, retire ทุก slot ด้วย `RetireAndDispose(5s)` · `LoadedExtension.Dispose()` เรียก `Instance.Shutdown()` (ดูดับ+log) → `alc.Unload()` (ดูดับ+log) → `FreeLibrary` ทุก handle · `App.OnExit` ให้เวลา dispose บน task ของตัวเอง ถ้าหมดเวลา log `Extension host dispose timed out on exit.` |

กลไกประตูอายุ (`ExtensionSlot.cs`): `TryAcquire`/`Release` นับผู้ใช้ที่กำลังเรียกอยู่, `RetireAndDispose` ทำเครื่องหมาย retired (ทำให้ `TryAcquire` ครั้งต่อไป fail), รอ `_zeroUsers` จนกว่าผู้ใช้ค้างจะออกครบ **ถ้าครบ 5 วินาทียังไม่ออก — log แล้ว dispose ต่ออยู่ดี** (ค่า 5 วินาทีมาจาก default ของ `ExtensionHost.ReloadRetireWait` และจาก `TimeSpan.FromSeconds(5)` ที่ hard-code ในทุก call site ของ retire)

---

## 10. ค่าที่โฮสต์เก็บเพื่อส่วนเสริม และการวางตำแหน่งในแถบข้าง

**config ผู้ใช้:** `%APPDATA%\SystemMonitor\config.wpf.json` (`AppConfig.Path`; โฟลเดอร์เก่า `%APPDATA%\SysMonitor\` ถูกย้ายเข้ามาก่อน — `AppConfig.CarryOverLegacyConfig`)

```json
"extensions": {
  "smart": { "enabled": false }
}
```

- map `id → { enabled }` เท่านั้น (`AppConfig.cs:ExtensionSetting` มี property เดียว) และ key เทียบแบบ case-insensitive
- ไม่มี key = **ปิด** (default `Enabled = false`)
- `SetEnabled` แก้ในหน่วยความจำทันที ส่วนการบันทึกลงไฟล์เป็นของ caller ที่ save config
- การย้ายค่าเก่าอัตโนมัติ (`AppConfig.Normalize`): `show_smart` → `extensions.smart` (แล้วลบ key เก่าทิ้งตอนเขียน) · `full_tab` = `"smart"` → `"ext:smart:status"`, `"smart<digits>"` → `"ext:smart:disk<digits>"`

**ตำแหน่งในแถบข้าง/ภาพรวม/แท็บ (`place_after`)** — `WidgetViewModel` + `WidgetViewModel.Extensions.cs:PlanExtensionPlacement`

- ค่าที่เป็นไปได้: `cpu` | `ram` | `disk` | `gpu` | `network` (canonical order เดียวกันทุกมุมมอง)
- โมดูลเป้าหมายยังแสดงอยู่ → แทรกต่อท้ายมัน (ถ้ามุมมองเต็มใช้ `FindLastIndex` ของแท็บโมดูลนั้น) และหลายตัวที่ anchor เดียวกันเรียงตาม target position แล้วตามด้วย `id`
- โมดูลเป้าหมายถูกซ่อน → anchor ถอยหลังหาโมดูลที่แสดงอยู่ก่อนหน้า, ถ้าไม่มีเลยวางที่ต้นรายการ
- ไม่ระบุ/ระบุไม่ตรง → ต่อท้ายสุดเสมอ เรียงตาม `id`
- section/แท็บที่ได้ key เป็น `ext:<id>` (ภาพรวม/widget) และ `ext:<id>:<tab.Key>` (มุมมองเต็ม) ซึ่ง `FullTab` จำค่านี้ได้; `ext:<id>:<subKey>` ที่หายi fall back ไปยังแท็บอื่นของส่วนเสริมเดียวกัน

---

## 11. การ build และแพ็กส่วนเสริม

### ทางลัด: ดูตัวอย่างจริงก่อน

`SysMonitor.Ext.Sample/` (id `sample`, implement `IExtension` + `IViewProvider` เท่านั้น, ไม่มี native, **ไม่ได้อยู่ใน allowlist** จึงต้องผ่านขั้นอนุมัติ) — เป็นแบบอย่างเล็กที่สุดที่โหลดได้

### `SysMonitor.PackTool` command line (`SysMonitor.PackTool/Program.cs`)

```
SysMonitor.PackTool.exe --template <extension.template.json> --out <dir> --version <x.y.z>
                        [--file <name>=<path>]... [--allowlist <file>]
```

| option | หมาย |
|---|---|
| `--template` | (จำเป็น) template ของส่วนเสริม |
| `--out` | (จำเป็น) โฟลเดอร์ปลายทาง — **ต้องว่างหรือไม่มีอยู่** ไม่อย่างนั้น fail ด้วย `PackException` กันไฟล์ค้างเก่าหลุดเข้าแพ็กเกจ |
| `--version` | (จำเป็น) แทนค่า `$VERSION` ใน template |
| `--file <path ใน template>=<source>` | ซ้ำได้; ทุกไฟล์ที่ไม่ `optional` ต้องมี source และ source ต้องมีจริง |
| `--allowlist <file>` | ใส่ hash ของ `extension.json` ที่เพิ่งสร้างลง allowlist (merge + เรียง ordinal) |

exit code: `0` สำเร็จ (พิมพ์บรรทัดเดียว `packed <id> <manifestSha256> <n> files`) · `1` `PackException`/error ไม่คาดคิด · `2` ไม่มี argument, ขอ help (`--help`/`-h`/`/?`), option ขาด value, option ไม่รู้จัก, หรือขาด option จำเป็น

### `build-wpf.cmd` ต่อสายอย่างไร (5 ขั้น)

1. `dotnet test SysMonitor.Tests`
2. build `SysMonitor.Ext.Smart`, `SysMonitor.Ext.Sample`, `SysMonitor.PackTool` (Release) → ลบ `build-ext\` และ `SysMonitor.Wpf\first-party-extensions.json` ของเก่า (ตรวจว่าลบสำเร็จจริง) → แพ็ก SMART เข้า `build-ext\smart` **พร้อม `--allowlist SysMonitor.Wpf\first-party-extensions.json`** → แพ็ก sample เข้า `build-ext\sample` **แบบไม่ใส่ `--allowlist`** (จึงเป็นบุคคลที่สาม) แล้วตรวจว่าไฟล์ allowlist ถูกสร้างจริง
3. `dotnet publish SysMonitor.Wpf -r win-x64 --self-contained false -p:PublishSingleFile=true` —这一步ฝัง allowlist ที่เพิ่งสร้างเข้าไปใน exe
4. copy exe ลง `SysMonitor.Setup\payload`, วาง `build-ext\smart\*` เป็น `payload\extensions\smart\`, แล้ว publish ตัวติดตั้ง (payload เป็น embedded resource)
5. รวบรวมผล: `dist-wpf\SystemMonitor-<ver>.exe`, `dist-wpf\SystemMonitor-Setup-<ver>.exe`, `dist-wpf\sample-extension\sample\`

`DiskInfoCore.dll` เป็นไฟล์ **native + optional** ใน template: สคริปต์หาจาก env `DISKINFOCORE` หรือ sibling checkout `..\DiskInfo\DiskInfoCore\Build\Releasex64\DiskInfoCore.dll` ถ้าไม่มีก็แพ็ก SMART โดยไม่มีไฟล์นี้ (แพ็กเกจยังถูก, manifest ไม่มี entry นั้น) — ดู §13 ข้อ 4

ลองส่วนเสริม sample ตามคำแนะนำท้ายสคริปต์: copy `dist-wpf\sample-extension\sample` เข้า `%LOCALAPPDATA%\Programs\SystemMonitor\extensions\` แล้วเปิดโปรแกรม กด "อนุมัติ..." ในแถบข้างและยืนยัน UAC เอง

---

## 12. การจัดการข้อผิดพลาดและการวินิจฉัย

**ผู้ใช้เห็นอะไร:** แถบข้างส่วน "Extensions" (`MainWindow.xaml.cs:AddExtensionRow`) แสดง `name` (หรือ display name จาก `IViewProvider` ถ้ามี), `version`, และคำอธิบายสถานะจาก I18n key ตามตาราง (`ExtensionStatusText.KeyFor`) · `Loaded` ไม่มีคำอธิบาย (ค่าว่าง)

| สถานะ | I18n key | ข้อความที่ผู้ใช้เห็น (th) |
|---|---|---|
| `NotApproved` | `ext_status_not_approved` | ยังไม่ได้รับอนุญาตให้ทำงาน |
| `NotTrustedWhenElevated` | `ext_status_not_trusted_when_elevated` | ไม่อนุญาตให้ทำงานเมื่อโปรแกรมทำงานด้วยสิทธิ์ผู้ดูแลระบบ |
| `ContractMismatch` | `ext_status_contract_mismatch` | เวอร์ชันของอินเทอร์เฟซไม่ตรงกัน |
| `HostTooOld` | `ext_status_host_too_old` | โปรแกรมหลักเวอร์ชันเก่าเกินไปสำหรับส่วนเสริมนี้ |
| `HashMismatch` | `ext_status_hash_mismatch` | ไฟล์ส่วนเสริมถูกแก้ไขหรือความสมบูรณ์ไม่ตรงกัน |
| `UnsafePath` | `ext_status_unsafe_path` | โฟลเดอร์ของส่วนเสริมมีความเสี่ยงด้านความปลอดภัย |
| `FileMissing` | `ext_status_file_missing` | ไม่พบไฟล์ของส่วนเสริม |
| `ManifestInvalid` | `ext_status_manifest_invalid` | ไฟล์ Manifest ของส่วนเสริมไม่ถูกต้อง |
| `EntryNotFound` | `ext_status_entry_not_found` | ไม่พบคลาสหลักของส่วนเสริม |
| `LoadFailed` | `ext_status_load_failed` | โหลดส่วนเสริมไม่สำเร็จ |
| `DuplicateId` | `ext_status_duplicate_id` | รหัสส่วนเสริมซ้ำกับส่วนเสริมอื่น |
| `Disabled` | `ext_status_disabled` | ถูกปิดการใช้งาน |

การกระทำที่ UI อนุญาต (`ExtensionStatusText`):
- สวิตช์เปิด/ปิด (`CanToggle`): ปิดได้เสมอ · เปิดได้เมื่อสถานะเป็น `Loaded` หรือ `Disabled` เท่านั้น
- ปุ่มอนุมัติ (`CanApprove`): ปรากฏเมื่อสถานะ `NotApproved` และโฮสต์ **ไม่** elevated · เมื่อ elevated และสถานะเป็น `NotApproved`/`NotTrustedWhenElevated` จะแสดงเฉพาะบรรทัดบอกเหตุ (`MainWindow.xaml.cs`)

**อะไรถูก log:** ทุกอย่างลง diagnostics ผ่าน `Diag.Write`
- ตัวโหลด: ข้อความ `Detail` ภาคภาษาอังกฤษแบบ technical ใน `ExtensionLoadResult.Detail` (รวม path, hash ที่คาดหวัง/ที่ได้, Win32 error ของ `LoadLibraryEx`) และข้อผิดพลาดระดับโครงสร้าง (`Unexpected error processing extension folder ...`, `Failed to load native library ...`)
- `Initialize` ที่โยน: `Extension '<id>' failed during load or Initialize(): ...`
- ข้อผิดพลาดตอนรัน: `Extension '<id>' threw <Type> in IViewProvider|IDemandAware|IDiskTemperatureSource: ...` **พิมพ์ครั้งเดียวต่อ `(id, exception type)`** และกันตาราง log โต (`SafeLogOnce`, เพดาน 256 key)
- `Shutdown()` / `alc.Unload()` ที่โยน: log แล้วปล่อยให้เกิด cleanup ต่อ
- retirement ค้าง: `Extension '<id>' in-flight calls did not complete within 5s; disposing anyway.`
- ACL/store: ความล้มเหลวของ approval store ทุกครั้ง log ไว้ (`ApprovalStore`, `ExtensionApproval`) ไม่มีการเงียบ
- `IExtensionContext.Log(message)` ของส่วนเสริมไหลลง log เดียวกัน

---

## 12.1 ข้อควรรู้เพิ่มสำหรับผู้เขียนส่วนเสริม

- `GetDisplayName` ถูกเรียกตอนโหลด **ก่อน** `SetDemand` ครั้งแรก (`ExtensionHost.StartAsync`) — อย่าพึ่ง demand ในเมธอดนี้
- โฮสต์เรียก `GetDiskTemperatures()` ได้เมื่อ demand ถูกส่งถึง instance แล้ว (`ExtensionSlot.DemandApplied`) โดยไม่ได้ตรวจว่า flag `DiskTemperature` เปิดอยู่ ส่วนเสริมที่เสียค่าใช้จ่ายสูงต้องหยุดสแกนเองเมื่อ flag นี้ไม่มี
- manifest ที่เปลี่ยน (hash เปลี่ยน) ต้องได้รับอนุมัติใหม่ หรืออยู่ใน allowlist ที่ฝังใน exe ใหม่ (การอนุมัติผูกกับ `id` + manifest SHA-256)

## 13. ข้อจำกัดที่ยอมรับแล้ว (เฉพาะที่เกี่ยวกับสัญญา)

| # | ข้อจำกัด | เกี่ยวกับ |
|---|---|---|
| 1 | ถ้า `GetView` ค้างเกิน 5 วินาที โฮสต์ dispose ส่วนเสริมต่ออยู่ดี (รอเท่า `ReloadRetireWait`) | §5.3, §9 |
| 2 | ติดตั้ง per-user: exe หลักอยู่ในโฟลเดอร์ที่ผู้ใช้เขียนได้ มัลแวร์สิทธิ์ผู้ใช้เดียวกันเขียนทับ exe ได้ (รวมทั้งปลอมเป็น "first-party") — ปิดได้สำหรับผู้ที่เลือกติดตั้งแบบ admin (R-022, `Docs/adr/0002-machine-wide-install.md`; **ยังไม่ได้ตรวจบนเครื่องจริง**) | §8, §7 |
| 3 | ปุ่ม "รันใหม่แบบผู้ดูแลระบบ" ขึ้นเมื่อส่วนเสริม SMART มีสถานะ `Loaded` แม้จะไม่มี `DiskInfoCore.dll` ในแพ็กเกจ (โฮสต์ไม่รู้ว่ามีไฟล์ native หรือไม่) | §8.6, §11 |
| 4 | `ExtensionHost.Reload` ล้าง cached display name ของส่วนเสริม | §9 |
| 5 | build ตรงผ่าน IDE/`dotnet build` โดยไม่ผ่าน `build-wpf.cmd` อาจฝัง `first-party-extensions.json` เวอร์ชันเก่า | §8.1, §11 |
| 6 | รัน PackTool สองprocess พร้อมกันบน allowlist ไฟล์เดียวอาจทำให้ hash หายไปหนึ่งรายการ (ตัวแพ็กไม่ lock ไฟล์; สคริปต์ build รันเรียงลำดับ) | §11 |
| 7 | ไฟล์ในแพ็กเกจถูกล็อกตอนอัปเกรด → ส่วนเสริมแสดง `HashMismatch`/`UnsafePath` จนกว่าจะรันตัวติดตั้งซ้ำ | §3 |
| 8 | test 2 ตัวถูกข้ามบนเครื่องพัฒนา (สร้าง file symlink ไม่ได้) ⇒ การป้องกัน file link **ยังไม่ถูกพิสูจน์ด้วยของจริง** | §3, §6 ขั้น 1/6 |
| 9 | ไม่มีตลาด/ดาวน์โหลด/อัปเดตส่วนเสริมอัตโนมัติ, ไม่มีแพ็กเกจบุคคลที่สามใน repo, ไม่มีการสแกนมัลแวร์ (หน้าที่ของ antivirus) | §1, §11 |
