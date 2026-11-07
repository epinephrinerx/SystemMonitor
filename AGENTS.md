# Repository instructions for Codex and compatible agents

เอกสารกลางของโครงการอยู่ที่:

- `docs/AI_WORKFLOW.md` — workflow, authority, handoff และ Definition of Done
- `docs/ARCHITECTURE.md` — system boundaries, dependency rules และ data flow
- `docs/TESTING.md` — commands และ verification policy

อ่านเฉพาะเอกสารที่เกี่ยวข้องกับงาน: ใช้ `ARCHITECTURE.md` เมื่อเปลี่ยน boundaries, interfaces, storage หรือ data flow และใช้ `TESTING.md` เมื่อแก้ behavior, tests, build หรือ CI

กติกาหลัก:

- รักษาการเปลี่ยนแปลงให้ตรง scope และอย่าเขียนทับงานที่มีอยู่
- ตรวจ Git diff ก่อนและหลังแก้ไข
- รัน verification ตามความเสี่ยงและแก้ failure ที่เกิดจากงานนี้
- อย่าอ้างว่า test ผ่านหากไม่ได้รันจริง
- ห้าม deploy, publish, ลบข้อมูล, เปลี่ยน production หรือเปิดเผย secrets โดยไม่มีคำสั่งชัดเจน
- เมื่อทำหน้าที่ reviewer หรือ Test Author อย่าแก้โค้ด production: reviewer รายงานเฉพาะ findings · Test Author แก้ได้เฉพาะเทสต์ของตัวเอง และห้ามอ่านโค้ดที่ implement แล้วก่อนเขียนเทสต์ (`AI_WORKFLOW.md` §5.6)
- เมื่อทำหน้าที่ Inspector รายงานอย่างเดียว **ห้ามแก้ไฟล์ใด ๆ** รวมทั้ง spec และเทสต์ · ส่งรายงานกลับ Coding Owner เท่านั้น (`AI_WORKFLOW.md` §5.7)
- เมื่อส่งมอบ ให้สรุปผล ไฟล์ที่เปลี่ยน คำสั่งที่รัน ผลลัพธ์ และความเสี่ยงที่เหลือ

Local tests ใช้ disposable fixtures และไม่มี production access: `YES`

หากงานเกี่ยวข้องกับ OpenAI API, ChatGPT หรือ Codex ให้ใช้ official OpenAI documentation ที่เป็นปัจจุบันก่อนตัดสินใจด้าน API หรือผลิตภัณฑ์

