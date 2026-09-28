# Requirements — SystemMonitor4.0

บันทึกสิ่งที่ตกลงกันไว้ · อัปเดตล่าสุด 2026-09-28
บริบทของโปรเจกต์อยู่ที่ `AGENTS.md` / `CLAUDE.md` (root) — ไฟล์นี้ไม่เล่าซ้ำ

| สถานะ | หมายถึง |
|---|---|
| 🕐 รอยืนยัน | ตีความแล้ว รอคุณรับรอง — ยังไม่เข้า pipeline |
| ✅ ตกลงแล้ว | รับรองแล้ว พร้อมลงมือ |
| 🔨 กำลังทำ | |
| 🧪 รอตรวจรับ | ทำเสร็จและทดสอบเกณฑ์ที่ทดสอบเองได้แล้ว รอคุณตรวจรับ |
| ✔️ เสร็จแล้ว | คุณตรวจรับแล้ว ผ่านเกณฑ์ครบทุกข้อ |
| ⚠️ ขัดแย้ง | ขัดกับข้ออื่น ต้องตัดสินก่อน |
| ⛔ ยกเลิก | ถูกแทนที่ด้วยข้ออื่น หรือไม่เอาแล้ว |

---

## R-001 · ปรับปรุงระบบของ SystemMonitor ให้สมบูรณ์มากขึ้น

| | |
|---|---|
| สถานะ | 🧪 รอตรวจรับ |
| วันที่ | 2026-09-28 |
| แตะส่วนไหน | โค้ดจาก https://github.com/epinephrinerx/SystemMonitor tag `v3.3.0` (SysMonitor.Wpf, SysMonitor.Setup, SysMonitor.Tests, เอกสาร) |
| เกี่ยวกับ | — |

**คุณระบุ:**
> ปรับปรุงระบบของ SystemMonitor https://github.com/epinephrinerx/SystemMonitor ให้สมบูรณ์มากขึ้น
> เวอร์ชั่นปัจจุบันคือ https://github.com/epinephrinerx/SystemMonitor/releases/tag/v3.3.0 นะครับ
> ชื่อซอฟต์แวร์มีคำว่า .net ติดในชื่อด้วยครับ (ไม่ใช่ C#)
> ดังนั้นชื่อจริงๆ คือ "System Monitor" ครับ

**ผมเข้าใจว่า:**
- ชื่อซอฟต์แวร์ตามที่ผู้ใช้ยืนยันคือ **System Monitor** — แต่ในโค้ด branch `C_Sharp` ยังใช้ชื่อไม่ตรงกัน: หัวหน้าต่าง/UI = "SysMonitor", registry/shortcut = "SysMonitor (.NET)", install dir = `%LOCALAPPDATA%\Programs\SysMonitor.NET`, repo = "SystemMonitor" (ติดกัน) — รอยืนยันว่าการทำให้ชื่อในโค้ดเป็น "System Monitor" อยู่ในงานนี้ด้วยหรือไม่
- โค้ดฐานคือ branch `C_Sharp` (หน้ากว่า tag v3.3.0 อีก 1 commit) นำมาเป็นโค้ดตั้งต้นใน workspace SystemMonitor4.0
- เก็บความ "ไม่สมบูรณ์" ที่ repo บ่งชี้เอง ได้แก่:
1. Retire Python version ให้จบตาม requirement ข้อ 6 เดิม ("หยุดทำ Python Version ให้คงไว้แค่ C# Version") — ปัจจุบัน `sysmonitor/`, `tests/`, `SysMonitor.pyw`, `run.cmd`, `build.cmd` ยังอยู่ใน repo
2. เอกสารไม่ตรงกับเวอร์ชันจริง: `CHANGELOG.md` หยุดที่ v3.1.0 (ขาด v3.2.0, v3.2.1, v3.3.0), `README.md` ยังนำเสนอ Python build เป็นหัวเรื่อง, `NEXT_STEPS.md` (2026-09-20) เป็นแผน migration ที่ทำเสร็จแล้ว
3. Release readiness ตาม NEXT_STEPS ขั้น 7–9: build/tests/smoke/packaging ผ่านและตรวจได้ซ้ำ

**ไม่รวม:**
- ไม่เพิ่ม feature ใหม่ที่ยังไม่ได้รับคำขอ (แยกเป็น R- ข้อใหม่ภายหลัง)
- ไม่ deploy/publish release ขึ้น GitHub โดยไม่มีคำสั่งชัดเจน
- ไม่แก้ upstream repo บน GitHub — ทำงานในเครื่องนี้เท่านั้น

**ตรวจรับเมื่อ:**
- [x] ยืนยัน scope ข้อ 1–3 ข้างบน (หรือแก้ไข) ก่อนเริ่ม — ผู้ใช้รับรองครบ รวม rename เป็น "System Monitor" (2026-09-28)
- [x] `dotnet build` + test suite ทั้งหมดผ่านบนโค้ดหลังเก็บกวาด — `dotnet test` ผ่าน 198/198 (รวม test ใหม่ 3 ตัว) และ `build-wpf.cmd` ผ่านทั้ง 4 ขั้น (tests → publish → installer → collect)
- [x] เอกสาร (README/CHANGELOG/NEXT_STEPS) สะท้อนสถานะจริงของ C# build — CHANGELOG เติม v3.2.0/v3.2.1/v3.3.0/v4.0.0, README ใหม่, NEXT_STEPS แทนด้วยงานค้างจริง
- [x] Python source ถูกจัดการตามทางเลือกที่ยืนยัน (ลบ / ย้าย archive) และ C# build ยังทำงานได้ครบ — ลบตาม requirement ข้อ 6 เดิม; release build ผ่าน

**ผลที่ทำแล้ว (รอผู้ใช้ตรวจรับ):**
- ไฟล์ที่ได้: `dist-wpf\SystemMonitor-4.0.0.exe` และ `dist-wpf\SystemMonitor-Setup-4.0.0.exe`
- สิ่งที่ยังต้องตรวจบนเครื่องจริง (ทำเองไม่ได้): ติดตั้งทับเครื่องที่มี v3.x/Python เดิมเพื่อพิสูจน์ legacy removal + config migration, startup/tray/autostart, soak test

