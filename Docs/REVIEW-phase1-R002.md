# รีวิวเฟส 1 (R-002): `ecc12ee..7d60f69`

โค้ดทำงานถูกต้อง แต่ให้ผลรีวิวเป็น FAIL เพราะ test ใหม่ 2 ตัวไม่มีทางล้มได้ จึงไม่ได้ตรวจสิ่งที่แผนกำหนดไว้จริง ส่วนนี้แก้เร็ว

## ผล `dotnet test SysMonitor.Tests` ที่รันจริงบน HEAD `7d60f69`
```
Passed!  - Failed: 0, Passed: 202, Skipped: 0, Total: 202, Duration: 1 s
```
Build ผ่านทั้ง 3 project ไม่เหลือโค้ดที่อ้างชื่อ `CollapseButton` หรือ `FullCollapseButton` เดิม

## ต้องแก้

**F1 — test 2 ตัวผ่านเสมอ แม้คีย์ที่ตั้งใจเฝ้าไว้จะหายไปแล้ว**
- ไฟล์: `SysMonitor.Tests/NavigationButtonTests.cs:54-63` (`The_context_menu_keys_the_headers_stopped_using_survive`) และ `:20-29` (`The_navigation_keys_exist_in_both_languages`)
- สาเหตุ: `Lang` indexer ที่ `SysMonitor.Wpf/I18n.cs:228-231` ถ้าหาคีย์ไม่เจอจะถอยไปใช้ข้อความ en และถ้า en ก็ไม่มี จะคืน**ชื่อคีย์เอง** ผลคือค่าที่ได้ไม่มีวันเป็น `string.Empty` การเช็ก `AreNotEqual(string.Empty, …)` จึงไม่มีทางล้ม
- ผลกระทบ:
  - แผนข้อ "คีย์ `collapse` และ `expand_hint` ยังอยู่ (context menu ยังใช้)" ไม่ได้ถูกตรวจจริง ถ้าลบคีย์ไป context menu (`MainWindow.xaml.cs:752-753`) จะโชว์คำว่า "collapse" หรือ "exit_fullscreen" ดิบ ๆ แต่ test ยังเขียว
  - ขัดกับ `TESTING.md` §6 ที่ห้ามเพิ่ม assertion ที่ไม่มีความหมาย
- วิธี reproduce: ลบบรรทัด `["collapse"] = …` ออกจากทั้ง th (`I18n.cs:52`) และ en (`:155`) แล้วรัน `dotnet test` จะยังผ่านครบ 202 ตัว
  - ส่วนคีย์ใหม่ (test 1) ยังมี test 2 ที่เช็กว่า th ≠ en คอยจับไว้บางส่วน แต่คีย์ context menu ในข้อ 4 ไม่มีตัวไหนจับเลย
- วิธีแก้ที่เสนอ: เช็ก `Assert.AreNotEqual(key, lang[key])` ทั้งสองภาษา หรือเช็ก th ≠ en แบบเดียวกับ test 2

## สังเกตไว้

1. **ไม่มีคีย์ `go_full` แต่ใช้ `full_data` แทน**
   - แผนที่ `CODING-OWNER-PLAN:98` กำหนดให้มีคีย์ `go_full` แต่ใน `MainWindow.xaml.cs:776` ปุ่ม Full ของหน้า Overall ใช้ `full_data` ("ดูข้อมูลเต็ม" / "Full Data")
   - ความหมายยังพอใช้ได้ และเป็นข้อความเดียวกับ `FullTitle` และปุ่มใน sidebar
   - แต่เป็นการเปลี่ยนจากแผนโดยไม่ได้จดไว้ ควรให้ผู้ใช้ยืนยันว่า tooltip "ดูข้อมูลเต็ม" ถือว่าเป็น "ไปหน้า Full" ตาม R-002 ข้อ 162
2. **ยังไม่มีหลักฐานว่าผู้ใช้ตัดสิน D1 (icon) แล้ว**
   - ที่ใช้ใน commit: `E80F` (Home) สำหรับไปหน้ารวม และ `E745` (ResizeMouseWide) สำหรับเปิดเป็น Widget
   - แผนข้อ D1 กำหนดให้ผู้ใช้เลือก icon จากภาพหน้าจอ แต่หาบันทึกการตัดสินใน Docs/ ไม่เจอ
3. **ไม่มีรายงานผลตรวจด้วยมือ**
   - เกณฑ์จบของเฟสมีเรื่องที่ต้องตรวจด้วยมือ: tooltip หลังเปลี่ยนภาษาแล้ว hover ใหม่, การสลับหน้า 4 เส้นทาง, และปุ่มปิด 3 หน้า × Close Action 2 ค่า
   - commit message ไม่ได้รายงานผลส่วนนี้ ผมทำได้แค่อ่านโค้ด เลยยืนยันแทนไม่ได้
4. **เรื่องที่ตรวจแล้วไม่มีปัญหา**
   - **tooltip ค้างภาษาเก่า:** ไม่เกิด ภาษาเปลี่ยนได้จาก sidebar ที่เดียว (`:942-949`) และตรงนั้นเรียก `BuildSidebar()` ซึ่งเรียก `UpdateHeaderTooltips` ต่ออีกที ส่วนตอนเริ่มโปรแกรม `BuildSidebar()` (`:92`) ถูกเรียกหลัง `InitializeComponent()` จึงไม่เจอ null
   - **เส้นทางสลับหน้าอื่นไม่ถูกแตะ:** double-click, F11/Esc (`:381-395`), ปุ่ม Full Data ใน sidebar (`:823`) และ context menu ยังเหมือนเดิม
   - **ปุ่ม Full → Widget (`:102`):** ใช้ `SetMode` ตัวเดิม ซึ่งจะคืน `Left/Top` จาก `_beforeFull` ก่อน แล้วค่อย `ApplyPanelSize` และ `KeepOnScreen` ผลเหมือนกด Esc สองครั้ง ตรงกับเกณฑ์ที่ว่าขนาดและตำแหน่งต้องเป็นไปตามหน้าปลายทาง
   - **คลิกไม่ถูกตีความเป็น resize หรือ drag:** ปุ่มใหม่เป็น `Button` ซึ่งเป็น `ButtonBase` และ `OverControl` (`:624`) กันไว้แล้ว
   - **`OnClosing` และคีย์ i18n เดิม:** ไม่ถูกแตะ ไม่มีคีย์ไหนถูกลบ
   - **ขอบเขต diff:** มี 4 ไฟล์ อยู่ใน scope ของ handoff ทั้งหมด

VERDICT: FAIL — โค้ดถูกต้องและ test ผ่าน 202/202 แต่ test ใหม่ 2 ตัวผ่านเสมอ เพราะ `Lang` คืนชื่อคีย์เมื่อหาคีย์ไม่เจอ จึงไม่ได้ตรวจคีย์ context menu ตามที่แผนกำหนด ต้องแก้ assertion ก่อน และยังต้องรอผู้ใช้ตัดสิน D1 กับรายงานผลตรวจด้วยมือ
