กำลังสรุปผลการตรวจครับ ตรวจ diff อ่านโค้ดรอบข้าง และรัน test ครบแล้ว

# ผลรีวิวเฟส 4a (R-003): commit `1561fbd` เทียบกับ `ab2f8d2`

## ผล test
`dotnet test SysMonitor.Tests` รันจริง: **Passed 218, Failed 0, Skipped 0**

- diff ของ `WindowGeometryTests.cs` มีแต่บรรทัดที่เพิ่ม ไม่ได้แก้ assertion ของ test เดิม
- `WindowGeometry.Resize` เดิมไม่ถูกแตะ

## จุดที่ผ่าน
- **สัดส่วนคิดจาก panel:** ใช้ `origin - ShadowPad*2` (`WindowGeometry.cs:157-158`) ตรงตามแผน
- **error ไม่สะสม:** ทุก frame คิดจาก `_resizeOrigin` และ delta รวมที่วัดจาก `_dragOrigin` (`MainWindow.xaml.cs:621-627`) ไม่มีการคิดทีละ frame
- **แยกโหมดถูก:** Overall/Full ยังเรียก `Resize` เดิม มีแค่โหมด Widget ที่เรียกฟังก์ชันใหม่ (`MainWindow.xaml.cs:706-711`)
- **config round-trip ในโค้ด:** `ResizeWidget` คืนค่า `panelW*s + 24` และ `Resize()` เขียน `window - 24` ลง `WidgetW/H` ค่าจึงตรงกัน
- **delta ติดลบเกินขนาด panel:** ได้ `s ≤ 0` ซึ่งถูก clamp ขึ้นไปที่ `sMin > 0` จึงไม่ได้ขนาดติดลบ
- **panelW/H ≤ 0 หรือ `Edge.None`:** คืน origin

## ต้องแก้

**F1. ลากขอบบนหรือล่างแล้ว widget ล้นออกนอกจอด้านข้าง** (`WindowGeometry.cs:199-203`)
- การขยายรอบจุดกลางไม่ได้เช็กขอบซ้าย/ขวาของ work area และตอนปล่อยเมาส์ (`MainWindow.xaml.cs:641-656`) ก็ไม่มี `KeepOnScreen`
- ผลกระทบ: ขัดกับเกณฑ์จบของแผนที่ว่า "widget ที่มุมล่างขวาไม่หลุดจอ" ซึ่งเป็นตำแหน่งตั้งต้นของ widget (`MainWindow.xaml.cs:223`)
- วิธี reproduce:
  1. ใช้ panel 270×104 ที่ตำแหน่งตั้งต้น ขอบขวาของ panel จะอยู่ที่ `area.Right - 36`
  2. ลากขอบบนขึ้นไป 50px จะได้ s ≈ 1.48 ความกว้างเพิ่ม 130 ขอบขวาจึงขยับออก 65 ไปอยู่ที่ **`area.Right + 29`**
  3. ถ้าลากจน s ชน sMax ≈ 3.33 จะล้นออกนอกจอราว 279px
- test `A_vertical_drag_widens_around_the_middle` ใช้ work area ไม่จำกัด จึงจับกรณีนี้ไม่ได้
- แนวทางแก้: ส่ง `workLeft/workRight` เข้าไปในฟังก์ชัน แล้ว clamp `left`

**F2. test ของ "config นอกขอบเขต" ไม่ได้ทดสอบ fallback จริง** (`WindowGeometryTests.cs`, `A_panel_outside_the_limits_never_goes_negative_or_runaway`)
- panel 276×276 **อยู่ในขอบเขต** (200..900 × 64..400) จึงได้ sMin 0.72 และ sMax 1.45 ช่วงไม่ว่าง
- ผลคือ branch `sMin > sMax` (`WindowGeometry.cs:165-168`) ไม่มี test ครอบเลย ทั้งที่แผนบังคับไว้ (บรรทัด 162) และคอมเมนต์ใน test ก็อ้างผิดว่าอยู่นอกขอบเขต
- test round-trip ที่แผนบังคับไว้ (บรรทัด 163) ก็ยังไม่มี
- วิธีตรวจ: ใช้ panel 1000×50 ซึ่ง ratio 20 เกิน `max.W/min.H ≈ 14.06` แล้วจะเข้า fallback จริง

## สังเกตไว้

1. **กฎ D4 ทำให้หน้าต่างกระโดดกลางการลาก** (`WindowGeometry.cs:214-215`)
   - การสลับจาก "งอกลง" เป็น "งอกขึ้น" เป็นแบบ discrete จึงกระโดดทันทีเท่ากับระยะห่างถึงขอบล่าง
   - reproduce: ใช้ Origin (100,200,500,250) และ `workBottom = 458` (ห่างขอบล่าง 20px) ที่ dx=43 ขอบล่างของ panel อยู่ที่ 458.4 พอ dx=44 กลับไปอยู่ที่ 438 คือกระโดดประมาณ 20px
   - แนะนำ: `top = Math.Min(origin.Top, workBottom + ShadowPad - height)` ให้งอกลงจนชนขอบล่างแล้วค่อยดันขึ้น ต่อเนื่องและยังตรงกับเจตนาของ D4 ควรแก้พร้อม F1
2. **fallback ยอมให้แกนหนึ่งต่ำกว่า min**
   - ตัวอย่าง: 1000×50 ได้ s = sMax = 0.9 ผลคือ 900×45 ซึ่งต่ำกว่า min.H 64 และขนาดเปลี่ยนทันทีตั้งแต่ frame แรก
   - คอมเมนต์เขียนว่า "closest end" แต่จริง ๆ เลือก sMax เสมอ
   - ไม่ติดลบและไม่ runaway แต่ควรตัดสินใจให้ชัด
3. **widget เก่าที่ ratio สุดขอบจะ resize ไม่ได้อีกเลย**
   - ตัวอย่าง: 900×64 หรือ 200×400 ซึ่งทำได้ด้วยการลากอิสระก่อนเฟสนี้ จะได้ sMin = sMax = 1
   - ทางออกเดียวคือ `ResetSize`
   - ไม่มี clamp ตอน load `WidgetW/H` (`AppConfig.cs:89-90, 242-243`)
4. **DPI และ work area**
   - `WorkArea()` ถูกเรียก (P/Invoke) ทุก mouse-move และใช้ `GetDpi(this)` ปัจจุบัน ไม่ได้ใช้ `_dragDpi`
   - ถ้า DPI เปลี่ยนกลางการลาก `workBottom` อาจเปลี่ยนและ D4 สลับทิศได้
   - แนะนำให้คำนวณครั้งเดียวตอนเริ่มลาก (`MainWindow.xaml.cs:548`)
   - การแปลง DIP↔physical ข้ามจอมีปัญหาอยู่แล้วก่อนเฟสนี้ ไม่ใช่ regression
5. **งอกขึ้นแล้วไม่เช็กขอบบนของ work area** โอกาสเกิดต่ำเพราะ max.H เป็น 400
6. **test ที่ยังขาด**
   - การยึดมุมตรงข้ามของมุม Left|Top, Right|Top, Left|Bottom (ตอนนี้ test แค่ Right|Bottom)
   - การขยายรอบจุดกลางเมื่อลากขอบ Top
   - มุมที่ `|sX-1| == |sY-1|`
7. **`ResetSize` ตั้ง `WidgetH = 90` แต่ค่า default คือ 104** (`MainWindow.xaml.cs:493`) ทำให้ ratio หลัง reset เปลี่ยน เรื่องนี้เป็นของ D3 เฟส 4b ไม่ใช่ข้อบกพร่องของ 4a

VERDICT: FAIL — การลากขอบบน/ล่างทำให้ widget ล้นนอกจอด้านข้าง (F1) ขัดกับเกณฑ์จบของแผน และ test ของ config นอกขอบเขตไม่ได้เข้า fallback จริง ส่วน test round-trip ที่แผนบังคับไว้ยังไม่มี (F2)
