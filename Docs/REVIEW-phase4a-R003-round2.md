# ผลรีวิวรอบ 2 ของเฟส 4a (R-003): `1561fbd..b92b252`

## ผล test (รันจริง)
`dotnet test SysMonitor.Tests`: **Passed 222, Failed 0, Skipped 0** เพิ่มจาก 218 เป็น 222 ตรงกับ test ใหม่ 4 ตัว (tied corner, round-trip, การงอกแบบต่อเนื่อง, clamp ด้านข้าง) และ `WindowGeometry.Resize` ของโหมด Overall/Full ยังไม่ถูกแตะ

## การแก้ findings เดิม

**F1 แก้แล้วสำหรับสิ่งที่รอบแรกรายงาน**
- ลากขอบบน/ล่างแล้ว widget ขยายรอบจุดกลาง ตอนนี้ถูก clamp ให้ขอบ panel ทั้งซ้ายและขวาอยู่ในจอ (`WindowGeometry.cs:214-218`) กรณีกว้างเกิน work area ก็มี guard จัดไว้กลางจอ
- test `A_vertical_drag_stays_on_the_screen_at_the_screen_edge` เข้า clamp จริง ผมคำนวณดู: s ถูก clamp ที่ sMax 1.77 ได้ width 866 และ left −83 จึงถูกดึงกลับมาที่ −12 แต่ test นี้เข้าแค่ด้านซ้าย ด้าน maxLeft ยังไม่มี test ครอบ

**N1 (กระโดดกลางการลาก) แก้ถูกวิธี**
- ใช้ `top = Math.Min(origin.Top, workArea.Bottom + ShadowPad − height)` (`:236`) เป็นฟังก์ชันต่อเนื่องของ height
- test `Growing_up_at_the_bottom_is_continuous_not_a_jump` ตรวจทั้งช่วงที่ยังงอกลง และช่วงที่ขอบล่างแตะ work area พอดี

**N4 แก้แล้ว**
- `_dragWorkArea` ถูกจับครั้งเดียวตอนเริ่มลาก (`MainWindow.xaml.cs:553`) และ `Resize` ใช้ค่านี้ (`:713`) จึงไม่มี P/Invoke ทุก frame อีกแล้ว

**F2 แก้แล้ว**
- panel 1000×50 ให้ sMin 1.28 มากกว่า sMax 0.9 จึงเข้า fallback จริง
- assertion ว่า width ≤ 900 จับได้ถ้า fallback เลือกปลายผิด เพราะถ้าเลือก 1.28 จะได้ width 1280 และ test จะ fail
- test round-trip และ test corner ครบ 4 มุมมีแล้ว

**Fallback ใหม่สอดคล้องกับ test**
- โค้ดเลือกปลายที่ใกล้ scale 1: `|1−1.28| = 0.28` มากกว่า `|1−0.9| = 0.1` จึงได้ 0.9 ผลคือ 900×45 ratio 20 ตรงกับ assertion
- การ refactor การเลือก `s` (`:189-195`) ให้ผลเท่าเดิมสำหรับ resize ปกติ

## ต้องแก้

**F3. ลากมุมแล้วแกนที่ไม่ได้ตามเมาส์ล้นออกนอก work area** (`WindowGeometry.cs:221-228`)
- เป็นปัญหาประเภทเดียวกับ F1 ที่ยังเหลืออยู่ เมื่อลากมุม `Edge.Bottom` ตัว top ยังเป็น `origin.Top` เสมอโดยไม่มีการเทียบกับ work area
- ถ้าแกน X นำ ขอบล่างจะงอกลงเองโดยที่เมาส์ไม่ได้ลากไปทางนั้น
- วิธี reproduce ใช้ widget ที่ตำแหน่งตั้งต้น (`MainWindow.xaml.cs:223-225`):
  - panel 270×104 ขอบล่างของ panel อยู่ที่ `area.Bottom − 36`
  - ลากมุม Left|Bottom ไปทางซ้าย 135px (dy = 0) จะได้ s = 1.5 panel สูงขึ้นเป็น 156 และขอบล่างของ panel ไปอยู่ที่ **`area.Bottom + 16`** คือใต้ taskbar
  - ถ้าลากจนชน sMax ≈ 3.33 จะล้นลงไปราว **206px**
  - แบบเดียวกันนี้เกิดกับมุม Left|Top หรือ Right|Top ที่ขอบบนของจอด้วย
- ผลกระทบ: ขัดกับเกณฑ์จบ "widget ที่มุมล่างขวาไม่หลุดจอ" และขัดกับ doc comment ที่เพิ่มใน commit นี้ ("The work area bounds the result", `:149`)
- ตอนปล่อยเมาส์ (`MainWindow.xaml.cs:646-661`) ไม่มี `KeepOnScreen` ขนาดที่ล้นจึงถูกบันทึกไว้
- แนวทางแก้: สำหรับการลากมุม ให้ใช้กฎเดียวกับ `:236` กับแกนที่ไม่ได้ตามเมาส์ หรือ clamp `s` ไม่ให้แกนนั้นเกิน work area แล้วเพิ่ม test มุม Left|Bottom ที่ work area แน่น

## สังเกตไว้ (ไม่ block)
1. **เริ่มลากจากตำแหน่งที่ล้นจอไปแล้วจะกระโดดตั้งแต่ frame แรก**
   - การย้าย widget (`:636-643`) ไม่มีการ clamp ถ้า widget ถูกย้ายไปห้อยใต้ taskbar หรือเลยขอบขวามาก่อน การลากขอบขวาหรือขอบล่างแค่ 1px จะดึง widget กลับขึ้นจอทันที
   - ดูเป็นพฤติกรรมที่ยอมรับได้ แต่ขัดกับคอมเมนต์ "never jumps"
2. **fallback ฝั่ง sMin ยังไม่มี test**
   - ตัวอย่าง panel 1800×100 (ratio 18): sMin 0.64 และ sMax 0.5 โค้ดเลือก 0.64 ได้ width 1152 ซึ่ง **เกิน max.W**
   - คอมเมนต์ใน test ที่ว่า "respects the maximum width" จึงจริงแค่ในกรณีที่ test ใช้ ไม่จริงทั่วไป
3. **test round-trip ยังตรวจแค่ `Panel()` = window − 24** ไม่ได้ผ่าน config แล้วสร้างหน้าต่างกลับมาจริง
4. **ข้อสังเกต N3, N5 และ N7 จากรอบแรกยังเหมือนเดิม** ซึ่งไม่ได้อยู่ในขอบเขตที่รับมาแก้

VERDICT: FAIL — F1/F2, N1 และ N4 แก้ถูกวิธี และ test ผ่าน 222/222 แต่การลากมุม (เช่น Left|Bottom ของ widget ที่ตำแหน่งตั้งต้น) ยังดันแกนที่ไม่ได้ตามเมาส์ออกนอก work area ได้ถึงราว 206px (F3) ขัดกับเกณฑ์จบและ doc comment ของ commit นี้ ส่วนการลากจริงบนจอเป็นหน้าที่ของผู้ใช้
