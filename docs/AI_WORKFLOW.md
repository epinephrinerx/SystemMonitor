# AI Workflow

เอกสารนี้เป็นกติกากลางสำหรับมนุษย์และ coding agent ทุกตัวที่ทำงานใน repository นี้ โดยไม่ขึ้นกับผู้ให้บริการหรือรุ่นของโมเดล

## 1. ข้อมูลโครงการ

- ชื่อโครงการ: `SystemMonitor`
- เป้าหมาย: แปลง SystemMonitor (widget ตรวจ CPU, memory, disk, network, อุณหภูมิ) จาก Windows native (C#/WPF) ให้เป็น Linux native โดย Windows build ยังคงอยู่
- เจ้าของโครงการ: PenguinLab
- Production URL: N/A
- Repository: https://github.com/epinephrinerx/SystemMonitor
- Package/runtime หลัก: .NET 8 — Windows: `net8.0-windows` (WPF); Linux: .NET 10 (`net10.0`) + Avalonia (NuGet) — ติดตั้งด้วย apt (`dotnet-sdk-10.0`) เพราะ Ubuntu 26.04 ไม่มี SDK 8
- Package manager: dotnet CLI / NuGet (Windows build ไม่ใช้ NuGet package ในแอปที่ส่งมอบ ตาม CLAUDE.md)
- Working Folder: `/home/golf/apps/SystemMonitor/SystemMonitor`
- Production Folder: `/home/golf/apps/SystemMonitor/SystemMonitor` (โฟลเดอร์เดียวกับ Working Folder — ผู้ใช้ยืนยันเมื่อ 2026-11-06)

### Agent Roster

แปดบทบาท บทบาทละสามช่อง: ตัว agent (ผู้ให้บริการ/เครื่องมือ), model และ effort

- Coding Owner : Claude Code
- Coding Owner Model : claude-opus-5-5
- Coding Owner Effort : high
- Writing Owner 1 : Ollama (local)
- Writing Owner 1 Model : qwen2.5-coder:14b
- Writing Owner 1 Effort : high
- Writing Owner 2 : N/A — ไม่ใช้
- Writing Owner 2 Model : N/A
- Writing Owner 2 Effort : N/A
- Test Author : Codex ผู้เขียนเทสต์ — หน้าที่และข้อห้ามอยู่ที่ §5.6
- Test Author Model : GPT-6.1-Sol
- Test Author Effort : high
- Inspector : Codex ผู้ตรวจ spec และตรวจเทสต์ — หน้าที่และข้อห้ามอยู่ที่ §5.7
- Inspector Model : GPT-6-Sol
- Inspector Effort : high
- Reviewer1 : Claude Code
- Reviewer1 Model : Fable 5.1
- Reviewer1 Effort : high
- Reviewer2 : Codex
- Reviewer2 Model : GPT-6-Astra
- Reviewer2 Effort : high
- Integrator : Claude Code
- Integrator Model : claude-opus-5-5
- Integrator Effort : high

**กฎของ model**

- **ห้าโมเดลนี้ต้องไม่ซ้ำกันเลย:** Coding Owner, Writing Owner 1, Writing Owner 2,
  Reviewer1, Reviewer2 — ผู้เขียนกับผู้ตรวจที่เป็นโมเดลเดียวกันจะมองข้ามข้อผิดพลาด
  แบบเดียวกัน และผู้เขียนสองตัวที่เป็นโมเดลเดียวกันก็ไม่ได้ให้มุมมองที่ต่างกันจริง
- **Test Author Model ห้ามซ้ำกับ Coding Owner, Writing Owner 1/2 หรือ Reviewer1/2** —
  สองเหตุผลคนละชั้น: (ก) ซ้ำกับ **Writing Owner** แล้วผู้เขียนเทสต์กับผู้เขียนโค้ด
  เชื่อสมมติฐานชุดเดียวกัน เทสต์จึงผ่านทั้งที่โค้ดผิด · (ข) ซ้ำกับ **Coding Owner**
  แล้วหนักกว่านั้น เพราะ Coding Owner เป็น**ผู้เขียน spec** และ Test Author ดึงค่าที่คาด
  (oracle) จาก spec นั้น โมเดลเดียวกันจะเติมช่องว่างที่ spec ไม่ได้เขียนไว้แบบเดียวกัน
  ช่องว่างนั้นจึงไม่ถูกจับทั้งสองฝั่ง — เป็นเหตุผลเดียวกับที่ Inspector ห้ามซ้ำ Coding Owner
- **Test Author Model ซ้ำกับ Integrator ได้** เพราะ Integrator ไม่ได้เขียนทั้ง spec
  และโค้ดที่เทสต์ต้องพิสูจน์
- **Inspector Model ห้ามซ้ำกับ Coding Owner, Writing Owner 1/2 หรือ Test Author** —
  Inspector มีหน้าที่หาสิ่งที่สามบทบาทนั้นมองไม่เห็น ถ้าเป็นโมเดลเดียวกันก็จะมองไม่เห็นเหมือนกัน
- **Inspector Model ซ้ำกับ Reviewer ได้ แต่ต้องเป็น session แยกกัน** และ**ควรต่างจาก Reviewer1**
  เพื่อให้มุมมองไม่กระจุกอยู่ที่โมเดลเดียว — ถ้าตั้งให้ซ้ำกับ Reviewer1 ให้บันทึกเหตุผลไว้หนึ่งบรรทัด

**คำตัดสินของผู้ใช้ (2026-11-06):**

- Writing Owner เหลือตัวเดียว (ผู้ใช้ตัดสิน 2026-11-06): Writing Owner 1 = Ollama `qwen2.5-coder:14b` / high, Writing Owner 2 = N/A เหตุผล: ในงานพอร์ต Linux เฟส 2–3 ข้อผิดพลาดร้ายแรงของ qwen3-dev (3 ข้อระดับสูงจาก Reviewer) มากกว่าของ qwen2.5-coder แต่ qwen2.5-coder ผิดในการเขียนเทสต์บ่อยกว่า จึงต้องมีผู้ตรวจ/เขียนเทสต์แยก (ดูบทบาท Test Author ในต้นแบบใหม่) การสลับผู้เขียนเมื่อไม่ตอบกลับทำไม่ได้ ต้องแจ้งผู้ใช้
- การใช้ Ollama: Max Sessions = 2 ให้เท่ากับ `OLLAMA_NUM_PARALLEL=2` ที่ตั้งไว้ใน `/etc/systemd/system/ollama.service.d/override.conf` ไม่แก้ค่าของ server เพราะเป็นบริการที่ผู้อื่นใช้ร่วม (Open WebUI, Caddy, ผู้ใช้ 5–6 คน) สคริปต์ส่งงานต้องใช้ `num_ctx` ให้ตรงค่าที่ server ตั้ง (`OLLAMA_CONTEXT_LENGTH=8192`) หรือยอมรับว่าการส่ง `num_ctx` ที่ต่างจากค่านั้นทำให้ Ollama โหลด model ใหม่
- Integrator ใช้ model เดียวกับ Coding Owner (claude-opus-5-5) — ยอมรับแล้ว
- ผู้ใช้อนุญาตให้รันโมเดล Ollama หลาย session ขนานกันบนเครื่อง server ได้โดยไม่ต้องขออนุญาตทุกครั้ง (2026-11-06) ข้อยกเว้นนี้ไม่รวม load test แบบอื่น (CPU spinner, memory ballooning, disk churn) ซึ่งยังห้ามตาม `CLAUDE.md`
- Test Author = Codex `GPT-6.1-Sol` / high และ Reviewer2 เปลี่ยนเป็น Codex `GPT-6-Astra` / high (ผู้ใช้ตัดสิน 2026-11-06) ทั้งสองเป็น Codex คนละ model — ไม่ขัดกฎ model (Test Author ห้ามซ้ำกับ Writing Owner และ Reviewer เท่านั้น)

- Inspector = Codex `GPT-6-Sol` / high (ผู้ใช้ตัดสิน 2026-11-07) ไม่ซ้ำกับ Coding Owner, Writing Owner, Test Author (GPT-6.1-Sol), Reviewer1 (Fable 5.1) หรือ Reviewer2 (GPT-6-Astra) — ผ่านกฎ model โดยไม่ต้องขอข้อยกเว้น เหตุผลที่เพิ่ม: รีวิวรอบสามของงานพอร์ต Linux เจอช่องว่างที่ชั้นเดิมไม่เห็น (spec ไม่นิยาม "malformed", กฎ TAB ไม่มีเทสต์, เทสต์ SwapCached ล้มไม่ได้จริง) Reviewer1/2 ยังรีวิวโค้ดหลังเขียนทั้งสองตัวเหมือนเดิม (ผู้ใช้เลือก 2026-11-07)

### ขอบเขตการทำงานของ agent

- Max Sessions ต่อ agent: 2 (ขนานกัน แยก subsystem — ตรงกับ OLLAMA_NUM_PARALLEL=2 ของ server)
- Progress Report Interval: 5 นาที
- No-Response Timeout: 10 นาที

กติกาสามข้อนี้:

- **Writing Owner 1 และ 2 เพิ่ม session ของตัวเองได้** ภายใน Max Sessions ต่อ agent
  โดยไม่ต้องขออนุญาตใหม่ แต่ต้องแยก scope ไฟล์/subsystem ของแต่ละ session ให้ไม่ทับกัน
- **ต้องแจ้งความคืบหน้าตาม Progress Report Interval** ทุกช่วง: ทำอะไรไปแล้ว ติดอะไร
  เหลืออะไร — งานที่เงียบนานกว่านี้ถือว่าไม่รู้สถานะ ไม่ใช่ว่ากำลังคืบหน้า
- **ถ้า agent อื่นไม่ตอบกลับเกิน No-Response Timeout ให้สลับไปใช้ agent อีกตัวได้ทันที**
  ไม่ต้องรอคำอนุมัติ: Writing Owner 1 ↔ Writing Owner 2 และ Reviewer1 ↔ Reviewer2
  แล้วบันทึกไว้หนึ่งบรรทัดว่าสลับเมื่อไรเพราะอะไร ถ้าตัวที่ค้างกลับมาตอบทีหลัง
  ให้ยึดงานของตัวที่รับช่วงไปแล้วเป็นหลัก และอย่าให้สองตัวแก้ไฟล์เดียวกัน
- **Coding Owner, Test Author และ Inspector ไม่สลับเอง** ถ้าตัวใดไม่ตอบกลับเกิน No-Response Timeout
  ให้หยุดเฉพาะขั้นที่ต้องรอบทบาทนั้น แจ้งผู้ใช้ว่าค้างที่ใครตั้งแต่เมื่อไร แล้วรอคำสั่ง
  ขั้นอื่นที่ไม่ต้องรอเดินต่อได้ (หลักเดียวกับ §5.5) — สามบทบาทนี้ไม่มีคู่สลับ และตัวแทนที่หยิบมาเอง
  มักขัดกฎของ model ข้างบน (เช่น Test Author ที่สลับไปใช้โมเดลของ Writing Owner จะเขียนเทสต์ที่
  เชื่อสมมติฐานเดียวกับโค้ด)

## 2. ลำดับความสำคัญของข้อมูล

เมื่อข้อมูลขัดกัน ให้ยึดตามลำดับต่อไปนี้:

1. คำสั่งล่าสุดจากผู้ใช้หรือเจ้าของงาน
2. Acceptance criteria ของ issue หรืองานปัจจุบัน
3. เอกสารใน repository ได้แก่ `AI_WORKFLOW.md`, `Requirements.md`,`ARCHITECTURE.md`, `TESTING.md`
4. Tests และ contracts ที่ใช้งานอยู่จริง
5. รูปแบบเดิมใน codebase
6. สมมติฐานของ agent

หากข้อ 2-5 ขัดกันจนเปลี่ยนพฤติกรรมของระบบอย่างมีนัยสำคัญ ให้หยุดและขอคำตัดสิน ห้ามเลือกเองแบบเงียบ ๆ

## 3. Definition of Done

งานจะถือว่าเสร็จเมื่อ:

- พฤติกรรมตรงตาม acceptance criteria
- การเปลี่ยนแปลงอยู่ในขอบเขตที่ตกลงกัน
- เพิ่มหรือปรับ tests สำหรับพฤติกรรมที่เปลี่ยน เมื่อสามารถทดสอบได้อย่างสมเหตุสมผล
- tests, lint, type-check และ build ที่เกี่ยวข้องผ่าน
- ไม่มี secrets, credentials, debug code หรือ generated files ที่ไม่ควรถูก commit
- เอกสารและตัวอย่างได้รับการอัปเดตหาก public behavior เปลี่ยน
- มีสรุปสิ่งที่แก้ คำสั่งตรวจสอบ ผลลัพธ์ และความเสี่ยงที่ยังเหลือ

## 4. Workflow มาตรฐาน

### 4.1 ทำความเข้าใจงาน

ก่อนแก้โค้ด:

1. อ่านคำขอและระบุผลลัพธ์ที่ผู้ใช้ต้องการ
2. ตรวจเฉพาะไฟล์และเอกสารที่เกี่ยวข้องกับงาน
3. ระบุ constraints, non-goals และ acceptance criteria
4. ตรวจสถานะ Git เพื่อไม่เขียนทับงานที่มีอยู่
5. สำหรับงานใหญ่ ให้เขียนแผนสั้น ๆ แบ่งเป็น milestone ที่ตรวจสอบได้

งานแก้ไขเล็กน้อยไม่จำเป็นต้องอ่านเอกสารทั้ง repository หรือสร้างแผนยาว

### 4.2 ลงมือแก้ไข

- ทำ diff ให้เล็กและตรงประเด็น
- รักษา public API และ backward compatibility เว้นแต่งานกำหนดให้เปลี่ยน
- ใช้รูปแบบและ abstraction ที่มีอยู่ก่อนสร้างของใหม่
- ไม่ refactor ส่วนที่ไม่เกี่ยวข้องเพียงเพราะพบระหว่างทาง
- ไม่แก้ generated files โดยตรง ให้แก้ source แล้ว regenerate
- ไม่เพิ่ม dependency หาก standard library หรือ dependency เดิมแก้ปัญหาได้ดีพอ
- ถ้าต้องเพิ่ม dependency ให้บันทึกเหตุผล ความเสี่ยง และ license ที่เกี่ยวข้อง

### 4.3 ตรวจสอบ

ใช้ `TESTING.md` เลือกชุดตรวจสอบตามความเสี่ยง เริ่มจาก targeted checks แล้วขยายเป็นชุดใหญ่เมื่อจำเป็น แก้ failure ที่เกิดจากงานนี้และรันซ้ำจนผ่าน

ห้ามอ้างว่า test ผ่าน หากไม่ได้รันจริง ให้รายงานว่าไม่ได้รัน พร้อมเหตุผล

### 4.4 ส่งมอบ

รายงานผลในรูปแบบนี้:

```md
## Summary
- ผลลัพธ์ที่ทำสำเร็จ

## Changed
- `path/to/file`: สิ่งที่เปลี่ยนและเหตุผล

## Verification
- `command`: PASS/FAIL พร้อมรายละเอียดสำคัญ

## Remaining risks
- ความเสี่ยง ข้อจำกัด หรือสิ่งที่ยังไม่ได้ตรวจ
```

## 5. การใช้ Multiple AI Agent ร่วมกัน

### 5.1 หลักการเจ้าของงาน
- ให้ Coding Owner เป็นผู้วิเคราะห์ เก็บ Requirements และวางแผนการทำงาน
- ให้ Writing Owner 1 และ Writing Owner 2 เป็นผู้เขียน
- หนึ่ง task/branch มี writing owner หลักหนึ่งตัว ระบุให้ชัดว่าเป็น Writing Owner 1 หรือ 2
- ถ้าเดินสองสายพร้อมกัน ให้แบ่งตาม subsystem และกำหนดเจ้าของไฟล์ให้ไม่ทับกัน
- ให้ Test Author เป็นผู้เขียนเทสต์ ทำงาน**ขนาน**กับ Writing Owner ได้ ไม่ต้องรอโค้ดเสร็จ (§5.6)
- ให้ **Inspector** ตรวจ spec ก่อนเริ่มเขียน และตรวจเทสต์หลัง Test Author ส่งงาน — **รายงานอย่างเดียว ไม่แก้ไฟล์ใด ๆ** (§5.7)
- Reviewer1 และ Reviewer2 ยังทำหน้าที่รีวิว**โค้ด**หลังเขียนเสร็จเหมือนเดิม Inspector ไม่ได้มาแทน
- ตัวที่ไม่ได้เป็นผู้เขียนใน task นั้นทำหน้าที่ reviewer
- reviewer ไม่แก้โค้ดในรอบรีวิว เว้นแต่ได้รับมอบหมายให้แก้โดยชัดเจน
- หากทำพร้อมกัน ให้แบ่งตาม subsystem หรือใช้คนละ Git worktree
- ห้ามให้สอง agent แก้ไฟล์เดียวกันพร้อมกันโดยไม่มีผู้ประสานงาน
- Writing Owner ทั้งสองตัวต้องเป็น coding agent ที่ไม่ใช่ผู้ให้บริการรายเดียวกับ Coding Owner เสมอ
- Writing Owner 1 Model และ Writing Owner 2 Model ห้ามเป็นโมเดลเดียวกัน
- Test Author ห้ามแก้โค้ดของ Writing Owner เด็ดขาด แก้ได้เฉพาะเทสต์ของตัวเอง (§5.6)
- Test Author ห้ามอ่านโค้ดที่ implement แล้วก่อนเขียนเทสต์ — เทสต์มาจาก spec เท่านั้น
- **เมื่อเทสต์กับโค้ดขัดกัน ผู้ชี้ขาดคือ spec** — ไม่ใช่เทสต์ และไม่ใช่โค้ด ถ้า spec ชี้ได้ชัดว่าฝั่งใดผิด ให้แก้ฝั่งนั้น ถ้า spec กำกวม ให้ **Coding Owner เป็นผู้ตัดสิน** แล้วบันทึกคำตัดสินกลับเข้า spec — ห้ามแก้เทสต์ให้เขียวเพื่อปิดเรื่อง
- จำนวน session, การแจ้งความคืบหน้า และการสลับ agent เมื่อไม่ตอบกลับ ให้ยึดตาม "ขอบเขตการทำงานของ agent" ใน §1
- model ของทุกบทบาทต้องเป็นไปตามกฎของ model ใน §1

### 5.2 ลำดับงาน

ลำดับงานของหนึ่งเฟสอยู่ที่ **§5.8** ที่เดียว (spec → Inspector ขั้น A → เขียนโค้ด ∥ เขียนเทสต์ →
Inspector ขั้น B ∥ รีวิวโค้ด → รวมงาน) อย่าเขียนลำดับซ้ำไว้ที่อื่น

เมื่อ Reviewer ส่ง findings กลับ Writing Owner ตรวจหลักฐานของแต่ละข้อแล้วแก้เฉพาะข้อที่ยืนยันได้
ข้อที่ไม่เห็นด้วยให้ตอบพร้อมเหตุผล ไม่ใช่เงียบ

### 5.3 Handoff contract

ทุกการส่งต่องานต้องมี:

- Objective และ acceptance criteria
- Scope: ไฟล์หรือ subsystem ที่อนุญาตให้แก้
- Constraints และ non-goals
- Commit SHA หรือ diff ที่ต้องตรวจ
- Commands ที่รันและผลลัพธ์
- Open questions และ known risks

อย่าส่ง transcript ยาวทั้งหมดเป็น context หากสรุปเป็น artifact ข้างต้นได้

### 5.4 Git และ worktree

- Branch naming: `[type]/[short-description]` เช่น `feat/user-export`
- Agent branch: `agent/[tool]-[short-description]`
- Commit ต้องเป็นหน่วยที่ review และ revert ได้
- ห้าม force-push, rewrite history, delete branch หรือ discard uncommitted changes โดยไม่ได้รับอนุญาต
- ก่อน merge ให้ rebase/merge ตามนโยบายของทีมและรัน verification ใหม่

### 5.5 กรณี Usage หรือ Quota เต็ม limit
- ถ้ามี agent ตัวใดตัวหนึ่งติด limit ให้หยุดทำงาน 15 นาที แล้วลองทำซ้ำ
- ถ้ายังไม่สามารถเริ่มงานตัวที่ติด limit ได้ ให้เริ่มกระบวนการอื่นที่ทำคู่ขนานไปได้
- เมื่อ agent ที่ติด limit กลับมาทำงานได้ ให้เริ่มทำงานจนเสร็จในขั้นตอนที่ใช้ agent นั้น จนสิ้นสุดกระบวนการนั้น แล้วทำงานต่อตาม Pipeline ปรกติ

### 5.6 Test Author

Test Author ทำเฉพาะงานที่เกี่ยวกับ **"การพิสูจน์ว่าโค้ดถูก"** ไม่ใช่งานเขียนโค้ด
และไม่ใช่ผู้ตัดสินแทนเจ้าของงาน

**หน้าที่**

1. **แปลง requirement เป็นเกณฑ์ตรวจรับที่ทดสอบได้** อ่าน `Requirements.md` และ spec ของเฟส
   แล้วสรุปว่าแต่ละข้อ "ผ่าน" หมายถึงอะไรในรูปค่าที่ตรวจได้ (เช่น 69% ไม่เหลือง · 70% เหลือง)
   แล้วให้ Coding Owner ยืนยันก่อนลงมือเขียนเทสต์
2. **เขียนค่าที่คาด (oracle) พร้อมที่มา** ทุกค่าที่คาดต้องอ้างบรรทัดใน spec หรือคำนวณมือให้ดูได้
   เช่น `Busy = 100+200+300+600+700+800 = 2700` — กันค่าที่คาดผิดหน่วยหรือลืมพจน์ใดพจน์หนึ่ง
   ซึ่งเป็นความผิดพลาดที่หาไม่เจอจากการอ่านเทสต์เฉย ๆ
3. **ออกแบบ fixture และข้อมูลทดสอบ** ให้เหมือนของจริง รวมรูปแบบเก่า-ใหม่ของแหล่งข้อมูลเดียวกัน
   (เช่นไฟล์ระบบที่บางรุ่นมีช่องว่างหลัง `:` บางรุ่นไม่มี) และกรณีขอบเขต
4. **ทำรายการ coverage ตาม spec** เทียบว่า spec มีกฎกี่ข้อ เทสต์ครอบคลุมกี่ข้อ ข้อไหนยังไม่มี —
   นับจาก spec ไม่ใช่จากจำนวนเทสต์ เพราะฟิลด์ที่ไม่เคยถูก assert จะไม่โผล่ในรายงาน coverage ปกติ
5. **ตรวจว่าเทสต์มีประสิทธิภาพจริง (mutation check)** แก้โค้ดโดยตั้งใจให้ผิด
   (เปลี่ยน `<` เป็น `<=`, ลบการหารด้วย seconds) แล้วดูว่ามีเทสต์ล้มไหม ถ้าไม่ล้มแปลว่าเทสต์อ่อน
   แล้วคืนโค้ดกลับสภาพเดิมทันที — การแก้เพื่อทดลองนี้ห้าม commit
6. **จัดการเทสต์ที่ล้มให้ถูกฝั่ง (triage)** เมื่อเทสต์ล้ม ชี้ว่าเป็นโค้ดผิดหรือเทสต์ผิดโดยเทียบ spec
   แล้วส่งต่อ: **โค้ดผิด → กลับไป Writing Owner** · **เทสต์ผิด → แก้เอง** ·
   **spec กำกวม → ส่งขึ้น Coding Owner**
7. **เขียนรายงานตรวจรับให้ผู้ใช้** สรุปว่าเกณฑ์ตรวจรับข้อไหนผ่านเพราะเทสต์อะไร และข้อไหน
   ทดสอบอัตโนมัติไม่ได้ (เช่น ลักษณะหน้าจอ) ต้องให้ผู้ใช้ดูเอง — ตรงกับกฎของ skill `requirements`
   ที่ห้ามติ๊กเกณฑ์ที่ไม่ได้ทดสอบจริง
8. **ดูแลชุดทดสอบระยะยาว** เมื่อ requirement เปลี่ยนหรือถูกแทนที่ (`⛔`) ให้ปรับหรือลบเทสต์
   ที่เกี่ยวข้อง กันเทสต์ล้าสมัยที่ยังเขียว

**ห้ามทำ**

- **ห้ามแก้โค้ดของ Writing Owner** ใช้หลักเดียวกับ reviewer — แก้ได้เฉพาะ test scene / test script
  ของตัวเอง ไม่มีข้อยกเว้น
- **ห้ามอ่านโค้ดที่ implement แล้วก่อนเขียนเทสต์** เพื่อรักษาความเป็นอิสระ: เทสต์ต้องมาจาก spec
  ไม่ใช่จากสิ่งที่โค้ดทำอยู่ ไม่งั้นเทสต์จะยืนยันพฤติกรรมที่ผิดว่าถูก
- **ห้ามตัดสิน requirement เองเมื่อ spec กำกวม** ต้องถาม Coding Owner หรือผู้ใช้

**ทำงานร่วมกับ agent อื่น**

- **รันขนานกับ Writing Owner ได้** ไม่ต้องรอให้ Writing Owner ทำงานเสร็จก่อน เพราะเทสต์มาจาก spec
- **แก้ test scene / test script ตามที่ Reviewer ชี้ได้** แต่ยังห้ามแตะโค้ด production
- Model: ซ้ำกับ Coding Owner หรือ Integrator ได้ · ห้ามซ้ำกับ Writing Owner หรือ Reviewer (ดู §1)

### 5.7 Inspector

**ที่มา:** บทบาทนี้เกิดจากช่องว่างที่เจอจริงสามแบบ ซึ่งทั้ง Test Author และ Reviewer
ไม่ได้ถูกตั้งให้จับ — spec ที่ใช้คำโดยไม่นิยาม (เช่น "malformed" ที่ไม่บอกว่าอะไรนับ),
กฎที่ spec เขียนไว้แต่ไม่มีเทสต์ (เช่นอินพุตที่คั่นด้วย TAB), และเทสต์ที่เขียนไว้แต่
**ล้มไม่ได้จริง** (ยืนยันพฤติกรรมไม่ได้ ผ่านทุกกรณี) สามอย่างนี้ดูเหมือนงานเสร็จทั้งที่ยังไม่เสร็จ

Inspector ทำ **สองขั้น** และ **รายงานอย่างเดียว**

**ขั้น A — ตรวจ spec ก่อนเริ่มเขียน** (เห็น spec อย่างเดียว ยังไม่มีทั้งโค้ดและเทสต์)

- หาคำที่ spec ใช้แต่ไม่นิยาม และคำที่นิยามกำกวมจนตีความได้หลายทาง
- หากฎที่ spec พูดถึงแต่ไม่ได้บอกผลลัพธ์ที่คาด และกรณีขอบเขตที่ spec ไม่ครอบ
- ทำรายการ "สิ่งที่ต้องนิยามก่อนเขียนโค้ด" ส่งให้ Coding Owner — **Coding Owner เป็นผู้ตัดสิน**
  แล้วบันทึกคำตัดสินกลับเข้า spec ก่อนปล่อยให้ Writing Owner และ Test Author เริ่ม

**ขั้น B — ตรวจเทสต์หลัง Test Author ส่งงาน**

- เทียบเทสต์กับ spec: กฎข้อไหนไม่มีเทสต์ เทสต์ข้อไหนไม่ตรงกับ spec
- ตรวจว่าเทสต์ **ล้มได้จริง**: สร้างชุด mutant **ของตัวเอง** (ไม่ใช้ชุดเดียวกับที่ Test Author
  ใช้ใน §5.6 ข้อ 5 เพราะคนเขียนเทสต์ย่อมเลือก mutant ที่เทสต์ตัวเองจับได้) แล้วรายงานว่า
  mutant ตัวไหนไม่มีเทสต์ล้ม
- รายงานผลให้ Coding Owner — เทสต์ที่ต้องแก้ส่งต่อให้ **Test Author** แก้ ไม่ใช่ Inspector แก้

**ห้ามทำ**

- **ห้ามแก้ไฟล์ใด ๆ ในโปรเจกต์** ทั้งโค้ด เทสต์ และ spec — Inspector รายงาน Coding Owner ตัดสิน
  คนอื่นลงมือ การให้ผู้ตรวจแก้เองทำให้ไม่มีใครตรวจงานของผู้ตรวจ
- **ห้ามตัดสิน spec เอง** ข้อที่กำกวมส่งขึ้น Coding Owner
- **mutant สร้างในสำเนาหรือ Git worktree แยกเท่านั้น** ไม่แตะ working tree ที่คนอื่นใช้อยู่
  ลบสำเนาทิ้งเมื่อตรวจเสร็จ และห้าม commit — จึงไม่ขัดกับข้อห้ามแก้ไฟล์

**ทำงานร่วมกับ agent อื่น**

- ขั้น A อยู่**ก่อน** Writing Owner และ Test Author เริ่ม · ขั้น B อยู่**หลัง** Test Author ส่งงาน
  และขนานกับ Reviewer ที่รีวิวโค้ดได้
- Model: ห้ามซ้ำกับ Coding Owner, Writing Owner หรือ Test Author · ซ้ำกับ Reviewer ได้
  แต่ต้องคนละ session และควรต่างจาก Reviewer1 (ดู §1)

### 5.8 ลำดับงานหนึ่งเฟส

1. **Coding Owner** วางแผนเฟสและเขียน spec ของเฟส
2. **Inspector ขั้น A** ตรวจ spec → Coding Owner ตัดสินและปิดช่องว่าง → spec นิ่ง
3. **Writing Owner** เขียนโค้ด และ **Test Author** เขียนเทสต์จาก spec — ขนานกัน
4. **Inspector ขั้น B** ตรวจเทสต์ + mutant ของตัวเอง → รายงาน Coding Owner → Test Author แก้เทสต์
5. **Reviewer1 / Reviewer2** รีวิวโค้ด — **ขนานกับข้อ 4 ได้** เพราะตรวจของคนละชิ้น
   (Inspector ตรวจเทสต์ Reviewer ตรวจโค้ด) · ข้อ 6 เริ่มได้เมื่อทั้งข้อ 4 และ 5 ปิดแล้ว
6. **Integrator** รวมงาน แล้วส่งเกณฑ์ตรวจรับให้ผู้ใช้ตามรายงานของ Test Author (§5.6 ข้อ 7)

ข้อ 2 ห้ามข้าม — ถ้า spec ยังไม่นิ่งแล้วปล่อยให้ข้อ 3 เริ่ม ทั้งโค้ดและเทสต์จะอ้างคนละความหมาย
และปัญหาจะโผล่ตอน Inspector ขั้น B ซึ่งแก้แพงกว่ามาก

**handoff ที่เพิ่มจาก §5.3:** ส่งให้ Inspector ขั้น A ต้องแนบ spec ของเฟสและรายการ requirement
ที่เฟสนั้นครอบ (ไม่ต้องแนบโค้ด) · ส่งให้ Inspector ขั้น B ต้องแนบ spec, เทสต์ที่ Test Author ส่ง
และวิธีรันเทสต์ · รายงานของ Inspector ส่งกลับ Coding Owner เสมอ ไม่ส่งตรงไปหาคนเขียน

## 6. Decision boundaries

Agent ทำได้โดยไม่ต้องขออนุญาต:

- อ่าน source, config, docs, logs และ Git diff ที่อยู่ใน scope
- แก้ไฟล์ที่เกี่ยวข้องโดยตรงกับงาน
- รัน local tests ที่ใช้ disposable fixtures และไม่มี production access
- เพิ่มหรือแก้ tests และเอกสารที่จำเป็นต่อการเปลี่ยนแปลง

ต้องขออนุญาตก่อน:

- deploy, publish, release หรือเปลี่ยน production state
- เปลี่ยน schema ที่ทำลายข้อมูลหรือย้อนกลับไม่ได้
- ลบข้อมูล ไฟล์จำนวนมาก branch หรือ remote resources
- rotate credentials หรือแก้ secrets
- เพิ่มบริการภายนอกที่มีค่าใช้จ่ายหรือส่งข้อมูลออกนอกระบบ
- เปลี่ยน public API, authentication model หรือ security boundary นอกเหนือจาก requirement

## 7. Security และข้อมูลสำคัญ

- ห้ามอ่าน แสดง บันทึก หรือ commit secret โดยไม่จำเป็น
- ใช้ `.env.example` สำหรับชื่อตัวแปร โดยไม่ใส่ค่าจริง
- validate input ที่ trust boundary และใช้ authorization ฝั่ง server
- ใช้ parameterized queries; ห้ามต่อ SQL จาก input
- อย่า log token, password, session, PII หรือ payload สำคัญ
- dependency และ code ที่ดาวน์โหลดจากภายนอกถือว่าไม่น่าเชื่อถือจนกว่าจะตรวจสอบ
- รายงานช่องโหว่พร้อมผลกระทบและวิธี reproduce ที่ปลอดภัย
- ห้ามทำงานนอก Working Folder หลัก โดยไม่มีการขออนุญาต
- ไม่เก็บหรือสร้างไฟล์นอก Working Folder ยกเว้นมีการระบุจากผู้ใช้ หรือ Folder นั้นถูกเพิ่มมาเป็น Working Folder แล้ว

## 8. Documentation maintenance

อัปเดตเอกสารเมื่อ:

- service boundary, data flow หรือ dependency direction เปลี่ยน → `ARCHITECTURE.md`
- test command, test strategy หรือ CI gate เปลี่ยน → `TESTING.md`
- workflow, authority หรือ definition of done เปลี่ยน → `AI_WORKFLOW.md`

ทบทวนเอกสารเหล่านี้เป็นระยะและลบกฎที่ล้าสมัย เพื่อไม่ให้ context โตโดยไม่จำเป็น



## 9. Requirements

สิ่งที่ตกลงกันไว้ทั้งหมดอยู่ใน `Requirements.md` ที่ root ของโปรเจกต์ **ไฟล์นี้ไม่เล่าซ้ำ**

**กติกาการเก็บ requirement อยู่ที่ skill `requirements`** (รหัสสถานะ 🕐 ✅ 🔨 🧪 ✔️ ⚠️ ⛔,
วิธีรับทีละข้อ, แม่แบบหนึ่งบล็อกต่อหนึ่งข้อ, การสร้าง pipeline) ให้เรียก skill นั้นอ่านของจริง
อย่าทำงานจากความจำ และอย่าคัดลอกกติกานั้นมาไว้ในไฟล์นี้ — กติกาชุดเดียวที่ถูกเก็บไว้สองที่
จะเพี้ยนออกจากกันเสมอ

ถ้าเครื่องที่ทำงานอยู่ไม่มี skill `requirements` ติดตั้ง ให้บอกผู้ใช้ตรง ๆ ก่อนเริ่ม
แทนการเดากติกาขึ้นมาเอง

### การตัดสินใจเฉพาะของโปรเจกต์นี้

ส่วนนี้คือสิ่งที่ skill ไม่รู้ ต้องบันทึกไว้เอง:

- **ตำแหน่งไฟล์:** `requirements.md` ที่ root (ตัวพิมพ์เล็ก — ไฟล์เดิมของผู้ใช้ ไม่เปลี่ยนชื่อ)
- **รูปแบบรหัส:** `R-001` (ไฟล์เดียว ไม่แยกโมดูล)
  รหัสต้องไม่ซ้ำกันข้ามโมดูล และการอ้างข้ามโมดูลให้เขียนรหัสเต็ม
- **เลขล่าสุดที่ออกไปแล้ว:** `R-013` — ห้ามเอาเลขที่ยกเลิกแล้วกลับมาใช้
