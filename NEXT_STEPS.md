# แนวทางดำเนินการต่อ — System Monitor

อัปเดต: 2026-09-28

เอกสารฉบับก่อนหน้าเป็นแผนย้าย UI จาก Python/Tk ไป C#/WPF ซึ่งเสร็จสิ้นตั้งแต่
v3.0 แล้ว จึงถูกแทนด้วยสถานะปัจจุบันและงานที่เหลือจริง

## สถานะ

- v4.0.0: เปลี่ยนชื่อผลิตภัณฑ์เป็น System Monitor ทุกจุดที่ผู้ใช้มองเห็น
  (exe, installer, registry, shortcut, หน้าต่าง) พร้อม migration:
  installer ตรวจจับและถอน build เก่าทั้ง Python ("SysMonitor") และ C# ก่อน
  4.0 ("SysMonitor.NET") ผ่าน uninstaller ของมันเอง, config ย้ายจาก
  `%APPDATA%\SysMonitor` มา `%APPDATA%\SystemMonitor` โดยไม่ทับของใหม่
- Python build ถูกถอนออกจาก repository แล้ว (source, scripts, tests)
- CHANGELOG ครบทุกเวอร์ชันตั้งแต่ v3.0.0, 198 tests ผ่าน

## งานที่เหลือ

1. ~~Release v4.0.0~~ — **เสร็จแล้ว (2026-09-30)**: tag `v4.0.0` push บน branch `v4.0.0` และ release พร้อม assets อยู่ที่ https://github.com/epinephrinerx/SystemMonitor/releases/tag/v4.0.0 (ตัวติดตั้งทับเครื่องที่มี v3.x/Python เดิมยังควรทดสอบจริงอีกครั้งเมื่อมีเครื่องเป้าหมาย)
2. **Soak test ตามปกติใช้งาน**: วัด RSS/log ขณะ widget และ full view เป็นระยะ
   ห้ามสร้าง load ขึ้นมาทดสอบ
3. **Repo name**: repository บน GitHub ยังชื่อ `SystemMonitor` (ติดกัน)
   พิจารณาเปลี่ยนเป็นชื่อที่สอดคล้องกับผลิตภัณฑ์ — เป็นการตัดสินใจของเจ้าของ
   (GitHub redirect เดิมให้เอง แต่ Updater อ้าง hard-coded URL ต้องแก้ตาม)
4. **เก็บ requirement ใหม่**: ฟีเจอร์เพิ่มเติมรอบหน้าบันทึกใน `Requirements.md`
   ของ workspace นี้ทีละข้อ
