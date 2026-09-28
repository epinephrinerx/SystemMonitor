# Testing

เอกสารนี้เป็นแหล่งข้อมูลหลักสำหรับวิธีตรวจสอบ `[PROJECT_NAME]` แทนคำสั่งตัวอย่างทั้งหมดด้วยคำสั่งจริงก่อนใช้งาน

## 1. Quick start

```sh
[INSTALL_COMMAND]
[FAST_TEST_COMMAND]
[LINT_COMMAND]
[TYPECHECK_COMMAND]
[BUILD_COMMAND]
```

ข้อกำหนดเบื้องต้น:

- Runtime: `[VERSION]`
- Package manager: `[VERSION]`
- Services ที่ต้องใช้: `[DATABASE/CACHE/NONE]`
- ตัวแปร environment: ดู `.env.example`; ห้ามใช้ production credentials

## 2. Test levels

| Level | Purpose | Location | Command |
|---|---|---|---|
| Unit | business logic แบบ isolated | `[PATH]` | `[COMMAND]` |
| Integration | database, filesystem หรือ adapter contracts | `[PATH]` | `[COMMAND]` |
| Contract/API | request/response และ compatibility | `[PATH]` | `[COMMAND]` |
| End-to-end | critical user journeys | `[PATH]` | `[COMMAND]` |
| Smoke | ตรวจระบบหลัง build/deploy | `[PATH]` | `[COMMAND]` |

## 3. เลือกชุดตรวจสอบตามการเปลี่ยนแปลง

| Change | Required checks |
|---|---|
| Docs/comments only | formatting/link check ถ้ามี |
| Local logic | targeted unit tests + lint/type-check ที่เกี่ยวข้อง |
| Public API | unit + integration/contract + compatibility checks |
| Database/schema | migration test + integration + rollback/forward-fix validation |
| Authentication/security | negative tests + authorization boundary tests + relevant integration tests |
| UI behavior | component tests + critical E2E + visual/accessibility check ตามความเสี่ยง |
| Dependency/config/build | build + smoke + affected test suites |
| Shared/core code | affected tests แล้วตามด้วย full suite |

## 4. Test design rules

- Test observable behavior ไม่ผูกกับ implementation detail โดยไม่จำเป็น
- ทุก bug fix ควรมี regression test ที่ fail ก่อน fix และ pass หลัง fix
- ครอบคลุม happy path, boundary, invalid input และ failure path ที่สำคัญ
- Tests ต้อง deterministic และทำซ้ำได้
- ห้ามพึ่งลำดับการรัน เวลาจริง network จริง หรือ shared mutable state หากหลีกเลี่ยงได้
- ใช้ fake clock, seeded random และ controlled fixtures เมื่อต้องทดสอบเวลา/ความสุ่ม
- Mock เฉพาะ system boundary; อย่า mock logic ที่กำลังทดสอบ
- Test name อธิบายเงื่อนไขและผลลัพธ์ที่คาดหวัง

## 5. Test data and isolation

- ใช้ synthetic data; ห้ามใช้ข้อมูลลูกค้าจริง
- แต่ละ test ต้องสร้างและล้างข้อมูลของตนเอง
- Local test database: `[STRATEGY]`
- Fixture factories: `[PATH]`
- Reset command: `[COMMAND]`
- Tests ที่ destructive ต้องทำงานเฉพาะใน disposable environment และมี guard ป้องกัน production

## 6. Coverage policy

- Coverage เป็นสัญญาณ ไม่ใช่เป้าหมายเพียงอย่างเดียว
- Minimum project threshold: `[PERCENT_OR_N/A]`
- โค้ดใหม่/เปลี่ยนต้องครอบคลุม decision paths ที่มีความเสี่ยง
- ห้ามเพิ่ม meaningless assertions เพื่อให้ตัวเลขผ่าน
- รายงานส่วนที่ไม่ทดสอบพร้อมเหตุผลและ risk mitigation

## 7. CI quality gates

Pull request ต้องผ่าน:

1. Format/lint
2. Type-check หรือ static analysis
3. Unit tests
4. Required integration/contract tests
5. Build/package validation
6. Security/license checks ตามที่โครงการกำหนด

CI configuration: `[PATH_OR_URL]`

Flaky test ห้าม rerun จนเขียวแล้วละเลย ให้เก็บหลักฐาน เปิด issue และ quarantine เฉพาะเมื่อมีเจ้าของกับวันติดตามชัดเจน

## 8. Manual verification

ใช้ manual testing เมื่อ automation ไม่คุ้มค่าหรือครอบคลุมไม่ได้ และบันทึก:

- Environment/build ที่ทดสอบ
- Preconditions และ test data
- Steps
- Expected/actual result
- Screenshot/log ที่ไม่เปิดเผยข้อมูลสำคัญเมื่อจำเป็น

Critical smoke checklist:

- `[USER_JOURNEY_1]`
- `[USER_JOURNEY_2]`
- Error handling และ recovery ที่สำคัญ

## 9. Failure handling

เมื่อ test fail:

1. เก็บ command, error และ environment ที่เกี่ยวข้อง
2. ยืนยันว่า reproduce ได้
3. แยกว่าเกิดจาก change ปัจจุบัน, pre-existing failure หรือ environment
4. แก้เฉพาะ failure ที่อยู่ใน scope; อย่าซ่อนด้วย skip หรือ assertion ที่อ่อนลง
5. รัน targeted test ซ้ำ แล้วรัน broader suite ตามความเสี่ยง

หากไม่สามารถรัน test ได้ ให้รายงานเหตุผล สิ่งที่ตรวจแทน และความเสี่ยงที่เหลือ

## 10. Performance and security testing

- Performance command/scenario: `[COMMAND_OR_PLAN]`
- Baseline และ regression threshold: `[TARGET]`
- Security scanning: `[COMMAND/CI_JOB]`
- Dependency audit: `[COMMAND]`
- ห้ามยิง load test หรือ security test ไป production โดยไม่ได้รับอนุญาต

## 11. Release verification

ก่อน release:

- CI gates ผ่านจาก commit ที่จะ release
- migration และ compatibility ได้รับการตรวจ
- artifacts สามารถ build แบบทำซ้ำได้
- smoke tests ผ่านใน staging หรือ environment ที่กำหนด
- rollback/forward-fix plan พร้อม
- monitoring และ alert ที่เกี่ยวข้องพร้อมใช้งาน

