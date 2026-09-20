# แนวทางดำเนินการต่อ — SysMonitor

อัปเดต: 2026-09-20

## สถานะและปัญหาที่ยืนยันแล้ว

โปรแกรมที่พบ error ตอนปิดเป็น executable เก่า (`v2.0.0`, frozen,
`sampler=event-loop`) ซึ่งใช้ Tcl/Tk 9.0.4. กล่องข้อความ
`alloc: invalid block ...` เป็น native allocator crash ของ Tcl/Tk ไม่ใช่
Python exception จึงไม่มี traceback ที่จับได้ตามปกติ

Source ปัจจุบันมีงานค้างสำหรับ `v2.0.1`: ป้องกัน callback/timer ระหว่าง
shutdown และยกเลิก queued `after` callbacks ก่อน destroy หน้าต่าง. การทดสอบ
UI lifecycle แบบ synthetic ผ่านบน Tcl/Tk 9.0.4 แต่ยังไม่สามารถยืนยันว่า
แก้ allocator crash ได้ทุกกรณี และยังไม่มี executable ใหม่ที่ build จากงานนี้.

การติดตั้ง Tcl/Tk 8.6 แยกต่างหากไม่เปลี่ยน Tk ที่ Python 3.14 ใช้งาน:
`_tkinter` ถูก build/link กับ Tcl/Tk 9.0 แล้ว. การเปลี่ยนให้ Python 3.14
ใช้ Tcl/Tk 8.6 ต้อง custom-build Python/_tkinter ทั้งชุด จึงไม่ใช่การแก้
ระยะสั้นที่เหมาะสม.

## ทางเลือก UI

| ทางเลือก | ข้อดี | ข้อแลกเปลี่ยน | ข้อสรุป |
|---|---|---|---|
| Python + PySide6 / Qt | คง Python 3.14 และ logic ส่วนใหญ่ไว้; custom drawing ดี | เพิ่ม Qt runtime/licensing และขนาดไฟล์ | เหมาะหากต้องการคง Python |
| C# + WPF | พัฒนา Windows widget ได้เร็ว; tooling/debug/deployment ดี; ตัด Tcl/Tk ออกทั้งหมด | ต้อง rewrite UI และ Win32 interop | **ตัวเลือกที่แนะนำ** |
| C# + WinUI 3 | Fluent UI และ Windows App SDK ที่ทันสมัย | setup/windowing ซับซ้อนกว่า WPF | เหมาะหากต้องการ Windows 11 look เป็นหลัก |
| C++ + Win32/GDI/Direct2D | เบา ควบคุม native windowing ได้สูงสุด ไม่มี UI runtime เพิ่ม | งาน rewrite และดูแลสูงมาก | ใช้เมื่อขนาด/ทรัพยากรสำคัญกว่าความเร็วพัฒนา |
| C++ + Qt | custom UI และ cross-platform ดี | Qt runtime/licensing และ C++ complexity | ไม่จำเป็นสำหรับแอป Windows-only นี้ |

## แนวทางที่เสนอ: ย้าย UI เป็น C# + WPF

เป้าหมายคือสร้าง executable ใหม่ที่ไม่มี Tcl/Tk อยู่ใน process เลย โดยคง
feature set เดิมไว้ก่อน:

- Widget แบบ frameless, translucent, always-on-top, drag, snap-to-edge และ resize
- Mini/expanded view, theme, Thai/English, opacity และ settings persistence
- CPU/RAM/disk, I/O speed และ temperature semantics เดิม
- Sampling อยู่เบื้องหลังโดยไม่ block UI thread
- Installer/uninstaller ที่ปลอดภัย และ diagnostics log

### สิ่งที่นำกลับมาใช้เป็น specification ได้

Python source ปัจจุบันเป็น reference behaviour สำหรับการ port:

- `sysmonitor/win32.py`: Win32 APIs และ disk-temperature parsing
- `sysmonitor/sensors.py` / `process_sampler.py`: cadence, timeout และ snapshot model
- `sysmonitor/config.py`: default settings และ config schema
- `sysmonitor/i18n.py`: ข้อความ Thai/English
- `sysmonitor/theme.py`: palette และ temperature thresholds
- `tools/installer.py`: ownership/safety requirements ของ installer
- regression tests: acceptance criteria สำหรับ sensor, worker และ uninstall safety

โค้ด Python ไม่สามารถ reuse โดยตรงใน C# ได้ แต่ logic, data model, test cases และ
API contracts จะลดความเสี่ยงของ rewrite ได้มาก.

## แผนงานตามลำดับ

1. **ตัดสินใจ framework**: ใช้ C# + WPF เป็น default; เลือก WinUI 3 เฉพาะเมื่อ
   Fluent/Windows App SDK มีความสำคัญกว่าความเร็วในการย้าย.
2. **สร้าง skeleton ใหม่**: .NET desktop project, CI build, versioning, app icon และ
   portable/dev launch profile โดยยังไม่แตะ executable เก่าหรือ installer เดิม.
3. **ย้าย data layer**: port configuration, diagnostics และ immutable snapshots; เขียน
   unit tests ให้ผลลัพธ์เทียบกับ Python reference.
4. **ย้าย native sampling**: port Win32 calls ผ่าน P/Invoke; แยก sampling ออกจาก UI
   thread, ใส่ timeout/backoff และทดสอบ synthetic data ก่อนทดสอบ hardware จริง.
5. **สร้าง WPF widget**: implement windowing, mini/expanded layouts, rendering และ
   interactions โดยไม่สร้าง/ลบ visual objects ใน every refresh.
6. **ย้าย feature และ QA**: themes, i18n, settings, opacity, temperature warnings,
   disk modes และ accessibility/keyboard close.
7. **Package และ smoke test**: build executable, ตรวจ startup/close/restart,
   installer upgrade/uninstall safety และ log output ใน isolated test location.
8. **Soak test แบบใช้งานปกติ**: ตรวจ RSS/log ขณะ mini และ expanded view เป็นระยะ;
   ห้ามสร้าง CPU/memory/disk load เพื่อทดสอบ.
9. **Release**: commit/tag, publish artifact hash/version และแทนที่ executable เก่า
   หลังผ่าน validation เท่านั้น.

## เกณฑ์รับงาน (acceptance criteria)

- ปิดหน้าต่างซ้ำได้โดยไม่มี fatal dialog หรือ process ค้าง
- UI thread ไม่รอ hardware I/O หรือ process join
- ไม่มี stale readings หลัง sampler timeout/failure
- การ uninstall ลบเฉพาะไฟล์ที่เป็นของ SysMonitor
- Mini/expanded resize และ settings persistence ทำงานเหมือนเดิม
- Diagnostics ระบุเวอร์ชัน executable, process และ sampling status ได้
- Build/release มี unit tests, smoke test และ artifact hash บันทึกไว้

## สิ่งที่ยังไม่ทำ

- ไม่ติดตั้ง Tcl/Tk standalone เพราะไม่แก้ runtime ของ Python 3.14
- ไม่ rebuild หรือแทนที่ executable v2.0.0 เดิม
- ไม่รัน stress/load generation test
- ไม่แก้ source ปัจจุบันจนกว่าจะยืนยัน technology direction
