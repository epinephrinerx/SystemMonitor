**รีวิวซ้ำเฟส 4b (R-003), diff `302f90c..8f36b2d`:** F1 และ O2 ปิดแล้ว ไม่พบ regression และ test ผ่านจริง 233/233

## 1. F1 ปิดแล้ว (พื้นที่ที่ title เว้นให้ badge)

badge อยู่ในชั้นที่ย่อขยายตาม widget (สเกล s) แต่ถูกสเกลกลับด้วย 1/s ความกว้างบนจอจึงคงที่ ระยะที่เว้นให้คือ `72/s` หน่วยในชั้นนั้น พอคูณ s กลับจะได้ **72px บนจอเสมอ**

`Margin="0,0,8,0"` ของ badge (`MainWindow.xaml:318`) อยู่นอก transform ของ badge เอง จึงยังโดนสเกลเป็น 8·s px บนจอ ตัวเลขด้านล่างรวมส่วนนี้แล้ว:

| s | ระยะเว้น (หน่วยในชั้นสเกล) | บนจอ | badge ~45px + margin 8·s | เหลือ |
|---|---|---|---|---|
| 0.64 | 112.5 | 72px | 45 + 5.1 = 50.1px | ~22px |
| 1 | 72 | 72px | 53px | ~19px |
| 2 | 36 | 72px | 45 + 16 = 61px | ~11px |

title ไม่ทับ badge ทั้งตอนย่อและตอนขยาย ส่วน margin ขวา 96 ยังเป็นหน่วยในชั้นสเกล ซึ่งถูกต้อง เพราะส่วนตัวเลขฝั่งขวาก็สเกลตามไปด้วย

## 2. O2 ปิดแล้ว (margin ซ้ายขวา)

- `WidgetMessage` กับ `WidgetContentPanel` ได้ margin `16/s` ใน `WidgetContentHost` ที่สเกลอยู่ บนจอจึงเป็น **16px เสมอ** มากกว่าส่วนที่ chip ของปุ่มเลื่อนยื่นเข้ามา 14px (ปุ่มกว้าง 20 ลบ -6) อยู่ 2px ทุกค่า s
- เดิมที่ s=0.64 ได้ 16×0.64 = 10.2px ซึ่งน้อยกว่า 14 ตอนนี้แก้แล้ว

**O1 และ O5 ปิดแล้วเช่นกัน:** CHANGELOG ครอบคลุมทั้ง widget ที่เล็กกว่าและใหญ่กว่า default และใช้ `ScaleTransform` สองตัวซ้ำแทนการสร้างใหม่ทุกครั้ง (`MainWindow.xaml.cs:36-37, 218-221`)

## 3. ตรวจ regression

- **ลำดับใน constructor:** `ApplyPanelSize()` (`MainWindow.xaml.cs:94`) ตั้ง Width/Height แล้วเรียก `ApplyWidgetScale()` ก่อน `BuildSidebar()` (`:96`) ซึ่งเรียก `ApplyRotationPauseState()` (`:899`) ดังนั้นตอนเรียก `CurrentWidgetScale()` ความกว้างความสูงถูกตั้งไว้แล้ว
- **ตอน unpause:** `ToggleRotationPause` ไปที่ `ApplyRotationPauseState` แล้วไป `UpdateWidgetTitleMargin` ได้ `Thickness(0,0,96,0)` margin จึงรีเซ็ตถูกต้อง
- **เปลี่ยนขนาดขณะ paused:** ทั้ง `ApplyPanelSize` (`:197`) และเส้นทาง resize (`:789`) เรียก `ApplyWidgetScale` ซึ่งเรียก `UpdateWidgetTitleMargin(scale)` ทุกครั้ง margin จึงตามขนาดใหม่
- **ข้อสังเกต (ไม่ใช่ finding):**
  - `CurrentWidgetScale()` (`:226`) ไม่ได้เช็ก `_mode` ถ้าผู้ใช้เปลี่ยนภาษาหรือ toggle pause ขณะอยู่ในโหมด Overall หรือ Full (ผ่าน `BuildSidebar` ที่ `:1053`) margin จะถูกคำนวณจากขนาดหน้าต่างของโหมดนั้น แต่ `WidgetView` ถูกซ่อนอยู่ และเมื่อกลับเข้าโหมด Widget `SetMode` จะเรียก `ApplyPanelSize` แล้วไป `ApplyWidgetScale` ซึ่งคำนวณ margin ใหม่ จึงไม่มีผลที่มองเห็น
  - ถ้าวันหน้ามีการย้าย `BuildSidebar()` ไปก่อน `ApplyPanelSize()` ค่า Width/Height จะยังเป็น NaN และ `ContentScale` เช็กแค่ `<= 0` ซึ่งไม่ครอบคลุม NaN ผลคือ `72/NaN` เข้าไปใน `Thickness` ซึ่ง WPF ไม่รับค่านี้ เป็นจุดที่แตกง่ายถ้าสลับลำดับ แต่โค้ดตอนนี้ยังไม่เกิด

## 4. ผลทดสอบ (รันจริง)

รัน `dotnet test SysMonitor.Tests` ได้ `Passed! - Failed: 0, Passed: 233, Skipped: 0, Total: 233` exit code 0

ผมไม่ได้แก้ไฟล์ใด และไม่ได้สร้างไฟล์ REVIEW ตามที่กำหนดให้อ่านอย่างเดียว ส่วนการตรวจบนจอจริงที่ s=0.64 และ s=2 ทั้งภาษาไทยและอังกฤษยังเป็นของคุณ

VERDICT: PASS — ระยะเว้นให้ badge เป็น 72px บนจอเสมอ (มากกว่าที่ใช้จริงสูงสุดราว 61px ที่ s=2) margin ข้างเป็น 16px บนจอมากกว่า chip 14px ไม่พบ regression และ test ผ่าน 233/233
