# Testing

เอกสารนี้เป็นแหล่งข้อมูลหลักสำหรับวิธีตรวจสอบ **System Monitor (Linux, `linux-native`)** แทนคำสั่งตัวอย่างทั้งหมดด้วยคำสั่งจริงที่รันใน repository นี้

ขอบเขต: branch นี้มีเฉพาะโปรเจกต์ Linux (R-027) คำสั่งของ Windows (WPF, ตัวติดตั้ง, ส่วนเสริม) อยู่บน `main` และไม่ถูกรันที่นี่

## 1. Quick start

```sh
export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH

dotnet build SysMonitor.Linux.App -c Release      # ต้องได้ 0 warnings, 0 errors
dotnet test  SysMonitor.Linux.Tests               # ต้องเขียวทั้งหมด
./build-linux.sh                                  # archive + sha256 ใน dist/
./build-linux.sh --deb                            # archive + sha256 + .deb + sha256 ใน dist/
```

ข้อกำหนดเบื้องต้น:

- Runtime: .NET SDK 10.0 (ทดสอบด้วย 10.0.404); ถ้าไม่มี `dotnet` บน PATH ให้ตั้ง `DOTNET_ROOT=~/.dotnet`
- Package manager: NuGet ผ่าน dotnet CLI — การ build ปกติใช้ cache ในเครื่อง การ build self-contained ครั้งแรกต้องดึง runtime pack `linux-x64`/`linux-arm64` จาก nuget.org ครั้งเดียว
- Services ที่ต้องใช้: ไม่มี (ไม่ใช้ฐานข้อมูล ไม่มี network ในเทสต์)
- ตัวแปร environment: ไม่มี และห้ามใช้ production credentials

## 2. Test levels

| Level | Purpose | Location | Command |
|---|---|---|---|
| Unit | ตรรกะของ reader และ presentation แบบ isolated | `SysMonitor.Linux.Tests` | `dotnet test SysMonitor.Linux.Tests` |
| Integration (fixture tree) | อ่าน `/proc` `/sys` จาก tree จำลอง + fake `ICommandRunner` | `SysMonitor.Linux.Tests` | เหมือนข้างบน |
| Contract | archive layout, checksum, `install.sh` contract, Exec escaping | `SysMonitor.Linux.Tests` (`UpdaterTests`, `AutostartTests`, `InstallScriptTests`) | `dotnet test SysMonitor.Linux.Tests --filter UpdaterTests` |
| End-to-end | snapshot จริงของเครื่องที่รัน | `SysMonitor.Linux.Probe` | `dotnet run --project SysMonitor.Linux.Probe -- --json` |
| Smoke (UI model) | สอง sample จริง → view model เดียวกับที่หน้าต่างใช้ | `SysMonitor.Linux.App --probe-ui` | `dotnet run --project SysMonitor.Linux.App -- --probe-ui` |
| Smoke (update) | ยิง GitHub จริงหนึ่งครั้ง (GET เท่านั้น ไม่ดาวน์โหลด) | `SysMonitor.Linux.App --probe-update` | `dotnet run --project SysMonitor.Linux.App -- --probe-update` |
| Packaging | publish, stage, archive, checksum | `build-linux.sh` | `./build-linux.sh` |
| Packaging (.deb) | layout, control, checksum | `build-linux.sh --deb` | `./build-linux.sh --deb` |
| Installer scripts | ติดตั้ง/อัปเดต/ถอน ใน sandbox | `packaging/linux` | ดู §5 |

`--probe-ui` และ `--probe-update` พิมพ์ข้อความแล้วจบ ไม่เปิดหน้าต่าง และไม่เขียน config, autostart หรือไฟล์ใด ๆ

## 3. เลือกชุดตรวจสอบตามการเปลี่ยนแปลง

| Change | Required checks |
|---|---|
| Docs/comments only | ตรวจ link และตัวอย่างคำสั่งให้ตรงกับสคริปต์ |
| Sensor reader (`SysMonitor.Linux/Sensors`) | `dotnet test SysMonitor.Linux.Tests` + `SysMonitor.Linux.Probe` บนเครื่องจริง |
| Presentation (`SysMonitor.Linux/Presentation`) | unit tests ที่เกี่ยวข้อง + `--probe-ui` |
| Updater / autostart / tray | `UpdaterTests`, `AutostartTests`, `TrayHostTests`, `TrayTextTests`, `RestartTests` + `--probe-update` |
| Avalonia app (`SysMonitor.Linux.App`) | build 0 warnings + full suite + `--probe-ui` |
| Packaging / install scripts | `./build-linux.sh` + transcript ของ §5 (sandbox เท่านั้น) |
| Dependency/config/build | `dotnet build -c Release` + full suite + `./build-linux.sh` |

## 4. Test design rules

- Test observable behavior ไม่ผูกกับ implementation detail โดยไม่จำเป็น
- ทุก bug fix ควรมี regression test ที่ fail ก่อน fix และ pass หลัง fix
- ครอบคลุม happy path, boundary, invalid input และ failure path ที่สำคัญ
- Tests ต้อง deterministic และทำซ้ำได้
- ห้ามพึ่งลำดับการรัน เวลาจริง network จริง หรือ shared mutable state หากหลีกเลี่ยงได้
- ใช้ fake clock, seeded random และ controlled fixtures เมื่อต้องทดสอบเวลา/ความสุ่ม
- Mock เฉพาะ system boundary; อย่า mock logic ที่กำลังทดสอบ
- Test name อธิบายเงื่อนไขและผลลัพธ์ที่คาดหวัง

แนวปฏิบัติของโปรเจกต์นี้: reader ทุกตัวรับ `root` เพื่ออ่าน tree จำลอง, เครื่องมือภายนอก (`iw`, `nvidia-smi`, `systemctl`, `busctl`) เข้าถึงผ่าน `ICommandRunner` เท่านั้น, HTTP ใช้ `HttpMessageHandler` ปลอม และเวลาใช้ clock ที่ฉีดเข้าไป

## 5. Test data and isolation

- ใช้ synthetic data; ห้ามใช้ข้อมูลผู้ใช้จริง
- แต่ละ test ต้องสร้างและล้างข้อมูลของตนเอง (fixture tree ใน `Path.GetTempPath()`)
- Local test database: ไม่มี
- Fixture factories: `SysMonitor.Linux.Tests/TestHelper.cs`
- Reset command: `dotnet clean SysMonitor.Linux.Tests`
- Tests ที่ destructive ต้องทำงานเฉพาะใน disposable environment และมี guard ป้องกัน production

### Installer-script tests (sandbox only)

**ห้ามรัน `install.sh`/`uninstall.sh` กับ `$HOME` จริงเด็ดขาด** และห้ามรัน `app/sysmonitor` ใช้ `HOME` ชั่วคราวเสมอ:

```sh
S=$(mktemp -d)                                    # หรือโฟลเดอร์ใต้ scratchpad
mkdir -p "$S/home" "$S/dist"
tar -xzf dist/SystemMonitor-4.3.0-linux-x64.tar.gz -C "$S/dist"   # archive แบน: install.sh อยู่ที่ root

run() { env HOME="$S/home" XDG_DATA_HOME="$S/home/.local/share" \
             XDG_CONFIG_HOME="$S/home/.config" \
             XDG_STATE_HOME="$S/home/.local/state" "$@"; }

run dash "$S/dist/install.sh"
find "$S/home" | sort                             # ตรวจ tree ที่ติดตั้ง
sed -n '/^Exec=/p' "$S/home/.local/share/applications/systemmonitor.desktop"

# update: ไม่ให้สคริปต์เปิดแอป
sh -c true & dead=$!; wait "$dead"
run env SYSMONITOR_NO_LAUNCH=1 dash "$S/dist/install.sh" --update --wait-pid "$dead"

run dash "$S/dist/install.sh" --update --wait-pid 12ab   # ต้อง exit 1 (ไม่ใช่ตัวเลข)
run dash "$S/dist/uninstall.sh"                          # settings ต้องยังอยู่
run dash "$S/dist/uninstall.sh" --purge                  # settings และ log ถูกลบ
rm -rf "$S"
```

- รันสคริปต์ด้วย `dash` เสมอ (ไม่ใช่แค่ `sh`) เพราะ `/bin/sh` ของผู้ใช้แต่ละคนไม่เหมือนกัน
- ทดสอบการปฏิเสธสิทธิ์ root ได้ด้วย `id` ปลอมใน `PATH` (`printf '#!/bin/sh\necho 0\n' > "$S/fakebin/id"`)
- ตรวจ escaping ของ `Exec` โดยเทียบกับ `DesktopEntry.Quote` ใน `SysMonitor.Linux` (สองขั้น: Exec quoting แล้ว string-value escaping)
- **ขอบเขตเส้นทางแบบ physical** (Amendment 2): ต้องได้ exit 1 พร้อมข้อความสำหรับ
  - `XDG_DATA_HOME="$S/home/.local/../../outside"` (component `..`)
  - `XDG_DATA_HOME` ที่เป็น symlink ไปโฟลเดอร์นอก `HOME` (ต้องไม่มีอะไรถูกเขียนนอก `HOME`)
  - `XDG_DATA_HOME="$S/home"` (ไม่ใช่ path ใต้ `HOME`)
  - `XDG_DATA_HOME="relative/path"` (ไม่ absolute)
  - `app.new`/`app.old` ที่เป็น symlink หรือไฟล์ธรรมดา, และ `app/` ที่เป็น symlink
  - `uninstall.sh --purge` ขณะ `XDG_STATE_HOME` อยู่นอก `HOME` → ต้องไม่ลบอะไรเลย (app และ settings ยังอยู่)
- **กู้การติดตั้งที่ถูกตัดกลางทาง** (Amendment 2): ติดตั้งให้สำเร็จ, `mv app app.old`,
  สร้าง `app.new` ค้างไว้, แล้วรัน `install.sh` ซ้ำ → ต้องพิมพ์
  `restored the previous version from …` และจบด้วย `app/` ที่สมบูรณ์โดยไม่มี `app.new`/`app.old`
- `shellcheck` เป็นทางเลือก: ถ้าติดตั้งไว้ ให้รัน `shellcheck build-linux.sh packaging/linux/*.sh`; ถ้าไม่มี ให้รายงานว่าไม่มี

### .deb package (build only — ห้ามติดตั้ง)

`--deb` สร้าง `dist/systemmonitor_<ver>_<amd64|arm64>.deb` จาก publish output เดียวกัน
ต้องมี `dpkg-deb` และ `dpkg-shlibdeps` (`dpkg-dev`) บนเครื่อง build และใช้ได้เฉพาะ
กับ self-contained build: `./build-linux.sh --deb --framework-dependent` ต้องพิมพ์
ข้อผิดพลาดและ exit 2

```sh
./build-linux.sh --deb
cd dist
sha256sum -c SystemMonitor-<ver>-linux-x64.tar.gz.sha256
sha256sum -c systemmonitor_<ver>_amd64.deb.sha256

dpkg-deb --info systemmonitor_<ver>_amd64.deb
dpkg-deb --contents systemmonitor_<ver>_amd64.deb | head -30
dpkg-deb --contents systemmonitor_<ver>_amd64.deb | wc -l
```

ตรวจเพิ่มด้วยตา:

- `/opt/systemmonitor/sysmonitor` เป็น 755 และเป็นไฟล์ executable เดียวในแพ็กเกจ
- `/usr/bin/sysmonitor` เป็น symlink ไป `/opt/systemmonitor/sysmonitor`
- `/usr/share/applications/systemmonitor.desktop` มี `Exec=/opt/systemmonitor/sysmonitor` (ไม่มี field code)
- `/usr/share/icons/hicolor/256x256/apps/systemmonitor.png` มีอยู่
- `/usr/share/doc/systemmonitor/copyright` เป็น DEP-5: โปรแกรม + ฟอนต์ IBM Plex Sans Thai (OFL-1.1)
- `Depends` ใน `control` มาจาก `dpkg-shlibdeps` บน `.so` ที่ publish แล้ว บวก ICU, OpenSSL, X11/SM ที่ runtime `dlopen`

**ห้าม**ติดตั้งแพ็กเกจนี้ระหว่างตรวจรับ: ไม่รัน `dpkg -i`, `apt install`, `sudo` หรือเปิด GUI
`lintian` ไม่ได้ติดตั้งบนเครื่องนี้ — ถ้าไม่มี ให้รายงานว่าไม่มี

## 6. Coverage policy

- Coverage เป็นสัญญาณ ไม่ใช่เป้าหมายเพียงอย่างเดียว
- Minimum project threshold: N/A (ไม่ได้ตั้ง threshold)
- โค้ดใหม่/เปลี่ยนต้องครอบคลุม decision paths ที่มีความเสี่ยง
- ห้ามเพิ่ม meaningless assertions เพื่อให้ตัวเลขผ่าน
- รายงานส่วนที่ไม่ทดสอบพร้อมเหตุผลและ risk mitigation

## 7. CI quality gates

Pull request ต้องผ่าน:

1. `dotnet build SysMonitor.Linux.App -c Release` — 0 warnings, 0 errors
2. `dotnet test SysMonitor.Linux.Tests` — เขียวทั้งหมด
3. `./build-linux.sh` — archive ถูกสร้าง, `find dist/stage -type l` ว่าง, `sha256sum -c` ผ่าน
4. Installer scripts ใน sandbox (เมื่อแตะ `packaging/linux` หรือ `build-linux.sh`)
5. Security/license checks ตามที่โครงการกำหนด — ไม่มี CI ที่ตั้งไว้บน branch นี้

CI configuration: ไม่มี (ตรวจด้วยมือตาม §1 และ §5)

Flaky test ห้าม rerun จนเขียวแล้วละเลย ให้เก็บหลักฐาน เปิด issue และ quarantine เฉพาะเมื่อมีเจ้าของกับวันติดตามชัดเจน

## 8. Manual verification

ใช้ manual testing เมื่อ automation ไม่คุ้มค่าหรือครอบคลุมไม่ได้ และบันทึก:

- Environment/build ที่ทดสอบ (`uname -m`, `cat /etc/os-release`, ค่า `<Version>`)
- Preconditions และ test data
- Steps
- Expected/actual result
- Screenshot/log ที่ไม่เปิดเผยข้อมูลสำคัญเมื่อจำเป็น

Critical smoke checklist:

- `dotnet run --project SysMonitor.Linux.Probe -- --json` คืน snapshot ที่มี CPU/RAM/disk/network
- `--probe-ui` พิมพ์ view model ของทั้งสามมุมมอง พร้อม autostart `IsEnabled`, tray tooltip และชื่อ asset ของ update
- `--probe-update` คืนผลที่อ่านได้: พิมพ์ endpoint, ชนิด install (`tar.gz` หรือ `.deb`), asset ที่จะใช้, และ tag ที่เลือกพร้อมเหตุผล (ยังไม่มี Linux release ที่ไม่ใช่ prerelease จึงคาดว่า `up_to_date` พร้อม `no SystemMonitorLinux-* release`)
- ติดตั้งใน sandbox แล้วเปิดเมนู `.desktop` และไอคอนได้ (ตรวจด้วยตา ไม่ต้องรันแอป)
- ถอนแล้วไม่มีไฟล์ของโปรแกรมเหลือ และ settings อยู่ครบ (หรือหายเมื่อ `--purge`)

## 9. Failure handling

เมื่อ test fail:

1. เก็บ command, error และ environment ที่เกี่ยวข้อง
2. ยืนยันว่า reproduce ได้
3. แยกว่าเกิดจาก change ปัจจุบัน, pre-existing failure หรือ environment
4. แก้เฉพาะ failure ที่อยู่ใน scope; อย่าซ่อนด้วย skip หรือ assertion ที่อ่อนลง
5. รัน targeted test ซ้ำ แล้วรัน broader suite ตามความเสี่ยง

หากไม่สามารถรัน test ได้ ให้รายงานเหตุผล สิ่งที่ตรวจแทน และความเสี่ยงที่เหลือ

## 10. Performance and security testing

- Performance command/scenario: วัด RSS/CPU ของแอปขณะใช้งานปกติเท่านั้น (ห้ามสร้าง load เพื่อทดสอบ)
- Baseline และ regression threshold: ไม่ได้กำหนด
- Security scanning: ไม่มีเครื่องมือใน CI; ตรวจด้วยการอ่านโค้ดและเทสต์ของ `TarExtractor` (path traversal, link), `install.sh`/`uninstall.sh` guard และ Exec escaping
- Dependency audit: `dotnet list SysMonitor.Linux.App package --vulnerable` (ต้องมี network)
- ห้ามยิง load test หรือ security test ไป production โดยไมได้รับอนุญาต

## 11. Release verification

ก่อน release:

- `dotnet build -c Release` 0 warnings และ `dotnet test SysMonitor.Linux.Tests` ผ่านจาก commit ที่จะ release
- `./build-linux.sh` สร้าง archive ที่มี `install.sh` ที่ root, ไม่มี link, และ `sha256sum -c` ผ่าน
- `./build-linux.sh --deb` สร้าง `.deb` ที่ `dpkg-deb --info`/`--contents` ตรงตาม §5 และ `sha256sum -c` ผ่าน
- ติดตั้ง/อัปเดต/ถอนใน sandbox ผ่านตาม §5 (ไม่แตะ `$HOME` จริง)
- `--probe-ui` และ `--probe-update` ให้ผลตามที่คาด
- rollback/forward-fix plan พร้อม: archive รุ่นก่อนหน้ายังติดตั้งทับได้ด้วย `install.sh` ตัวเดียวกัน
