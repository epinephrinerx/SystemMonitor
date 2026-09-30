# รายงาน Review: เฟส 4b (R-003) สเกลเนื้อหา widget (diff `87c396f..302f90c`)

ผลรวม: ต้องแก้ 1 ข้อ คือ badge "หยุด" ทับชื่อหัวข้อเมื่อ widget เล็กกว่าขนาด default และมีเรื่องที่สังเกตไว้อีก 5 ข้อ Overall/Full ไม่ได้รับผลกระทบ และ `dotnet test` ผ่านทั้งหมด 233/233

**ผล `dotnet test SysMonitor.Tests` (รันจริง):** `Passed! - Failed: 0, Passed: 233, Skipped: 0, Total: 233`

## จุดเสี่ยงที่ตรวจแล้วผ่าน
- **Overall/Full ไม่เปลี่ยน:** `ApplyWidgetScale` ออกทันทีถ้าไม่ใช่โหมด Widget (`MainWindow.xaml.cs:207`) และไม่ได้แตะ `ApplyFontScale` หรือ font ramp ระดับ Application (`:1216-1227`)
- **ตัวอักษรไม่เต้นเมื่อวนหน้า:** scale คำนวณจาก `Panel(Width, Height)` ของหน้าต่าง ไม่ได้ขึ้นกับขนาดเนื้อหา
- **ใช้ scale ครบทุกเส้นทาง:**
  - ตอนเปิดโปรแกรม (`:92`)
  - `SetMode` ผ่าน `ApplyPanelSize` (`:419`)
  - `ResetSize` (`:518`)
  - `Resize` (`:758`)
  - นอกจากนี้ไม่มีที่อื่นตั้ง Width/Height ในโหมด Widget
- **ปุ่มควบคุมไม่ถูกสเกล:** Close, Pause, Prev, Next เป็น sibling อยู่นอก `WidgetContentHost` ตรงกับ D6
- **XAML nesting ปิดครบ:** `<Grid x:Name="WidgetContentHost">` เปิดที่บรรทัด 292 และปิดที่ 356 ก่อน `</Grid>` ของ WidgetView เพราะ `Message` กับ `StackPanel` ไม่มี Grid.Row จึงซ้อนกันเหมือนเดิม และการย้าย `VerticalAlignment="Center"` จาก StackPanel ขึ้นไปที่ host ให้ผลเท่าเดิม
- **LayoutTransform + Stretch:** host ถูก measure/arrange ที่ความกว้างที่มีอยู่หารด้วย s แล้วคูณกลับ title row และแถบจึงยังเต็มความกว้าง ส่วน `TextTrimming` ทำงานที่ความกว้าง layout = 270 − 24/s ซึ่งถูกต้อง
- **D3:** `ResetSize` ใช้ `WidgetReference` แล้ว และมี test ผูก reference ไว้กับ `AppConfig` default

## ต้องแก้

**F1: badge "หยุด" ทับชื่อหัวข้อเมื่อ s < 1 (regression จากการ counter-scale)**
- **ตำแหน่ง:** `MainWindow.xaml.cs:215` ร่วมกับ `:388-389` และ `MainWindow.xaml:314-327`
- **สาเหตุ:**
  - badge ถูก counter-scale ให้ขนาดบนจอคงที่ (ความกว้างราว w + 8·s px)
  - แต่พื้นที่ที่จองไว้ให้ badge คือ `WidgetTitle.Margin.Left = 72` ซึ่งอยู่ใน layout ที่ถูกสเกล จึงกว้างบนจอเพียง 72·s px
  - ตอนนี้ขนาดที่จองกับขนาดจริงของ badge ใช้หน่วยคนละระบบกัน ซึ่งแผนเขียนไว้ว่า "badge อยู่นอกชั้นนี้" (`CODING-OWNER-PLAN:169`)
- **ผลกระทบ:**
  - widget ที่เล็กสุดเมื่อลากจาก default แบบล็อกสัดส่วนคือราว 200×77 → s ≈ 0.74 → จองได้ราว 53 px
  - ข้อความ "⏸ Paused" ใน Label style (SemiBold 10pt) กว้างราว 45 px (ประมาณการ ยังไม่ได้วัดบนจอ) บวก margin จะเต็มพอดี
  - ถ้า FontScale 1.2 ขึ้นไป badge จะกว้างราว 60 px ขึ้นไป และทับชื่อหัวข้อ ขณะที่ก่อน commit นี้ยังพอดีใน 72 px
  - ขัดกับคอมเมนต์ใน XAML ที่ว่า "neither word is ever drawn on top of the other"
  - ด้านกลับกัน ที่ s = 2 จะมีช่องว่างเปล่าราว 80 px ระหว่าง badge กับชื่อ ซึ่งเป็นเรื่องความสวยงามเท่านั้น
- **วิธี reproduce:**
  1. ตั้ง Font size = 1.2 ถึง 1.6
  2. ลาก widget ให้เล็กสุด
  3. กดปุ่ม Pause
  4. badge กับชื่อหัวข้อจะซ้อนกัน โดยเห็นชัดขึ้นเมื่อ widget เป็นสัดส่วน 270×90 (ดู O1) ที่ s ≈ 0.64
- **แนวทางแก้:** ตั้ง margin ซ้ายของ title เป็น 72/s (หรือคิดจากความกว้างจริงของ badge) ภายใน `ApplyWidgetScale`/`ApplyRotationPauseState` หรือย้าย badge ออกไปนอก host ตามแผน

## สังเกตไว้

- **O1: ผู้ใช้เดิมที่ widget เล็กกว่า default จะเห็นตัวอักษรเล็กลง แต่ CHANGELOG ไม่ได้บอก** (`CHANGELOG.md:25-30`)
  - `ResetSize` รุ่นก่อนบันทึกขนาด 270×90 ทุกคนที่เคยกด Reset Size จึงได้ s = min(1, 90/104) ≈ 0.865 หลังอัปเกรด ตัวอักษรเล็กลงราว 13%
  - CHANGELOG พูดถึงแค่ widget ที่ใหญ่กว่า default
  - พฤติกรรมนี้เป็นไปตาม D3 ทาง (ก) แต่หมายเหตุควรพูดถึงฝั่งเล็กลงด้วย
  - วิธี reproduce: ตั้ง config เป็น `WidgetW=270, WidgetH=90` แล้วเปิดโปรแกรม
- **O2: เนื้อหาเข้าไปใต้ปุ่ม Prev/Next เมื่อ s < 0.875** (`MainWindow.xaml:306`)
  - margin 16 ของเนื้อหาถูกสเกลเป็น 16·s แต่ chip ของปุ่มยื่นเข้ามาคงที่ 14 px
  - ที่ s = 0.74 ทับราว 2 px และที่ s = 0.64 ทับราว 4 px ปุ่มอยู่ ZIndex 2 จึงวาดทับข้อความหรือแถบ
  - แนวทางแก้: ตั้ง margin เป็น 16/s ขั้นต่ำ หรือใช้ padding ที่ไม่ถูกสเกล
- **O3: margin 12 ของ WidgetView อยู่นอก transform จึงไม่ถูกสเกล**
  - ความสูง layout ที่เนื้อหาได้คือ 104 − 24/s: ได้ 80 ที่ s = 1, ราว 71.6 ที่ s = 0.74 และราว 92 ที่ s = 2
  - เนื้อหาที่พอดี 80 ที่ขนาด reference จึงอาจถูกตัดเล็กน้อยทั้งบนและล่างเมื่อ s < 1 เพราะ host จัดกึ่งกลาง
  - ถ้ารวมกับ FontScale 1.6 เนื้อหาถูกตัดอยู่แล้วที่ s = 1 และ scale ไม่ได้ช่วย แต่ไม่แย่กว่าก่อน commit เพราะช่วง s ≥ 1 มีพื้นที่เพิ่มขึ้น ต้องให้ผู้ใช้ตรวจบนจอ
- **O4: ยังไม่มี test "scale ไม่ขึ้นกับ FontScale" ตามที่แผนกำหนด**
  - ด้านโครงสร้างผ่าน เพราะ `ContentScale` ไม่รับ FontScale แต่ไม่มี test ยืนยันตรง ๆ
  - ไม่มี test สำหรับ `ResetSize` (อยู่ใน code-behind) มีแค่ test ทางอ้อมผ่าน `The_reference_is_the_configured_default_widget`
- **O5: `ApplyWidgetScale` สร้าง `ScaleTransform` ใหม่ 2 ตัวทุก mouse-move ระหว่างลาก**
  - ไม่ผิด แต่ถ้าเก็บ instance ไว้แล้วแก้ ScaleX/ScaleY จะลด allocation ได้
  - ส่วนการลากลื่นหรือไม่ ผู้ใช้ต้องตรวจบนจอ

ไม่ได้แก้ไฟล์ใด ๆ ในการ review นี้

VERDICT: FAIL — badge ที่ถูก counter-scale ใช้พื้นที่จองของชื่อหัวข้อ (72) ในหน่วยที่ถูกสเกล จึงทับชื่อหัวข้อเมื่อ widget เล็กกว่า default (F1) ส่วนที่เหลือถูกต้องและ test ผ่าน 233/233
