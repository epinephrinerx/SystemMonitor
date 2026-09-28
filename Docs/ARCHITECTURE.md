# Architecture

เอกสารนี้อธิบายโครงสร้างและขอบเขตของ `[PROJECT_NAME]` ให้อัปเดตเมื่อมีการเปลี่ยน service boundary, data flow, storage, public API หรือ dependency direction

## 1. System overview

`[อธิบายระบบ ผู้ใช้หลัก และคุณค่าที่ระบบมอบให้ภายใน 3-5 ประโยค]`

### Goals

- `[GOAL_1]`
- `[GOAL_2]`

### Non-goals

- `[NON_GOAL_1]`
- `[NON_GOAL_2]`

## 2. Context diagram

```text
[User/Client]
      |
      v
[UI/API Entry Point] ---> [External Service]
      |
      v
[Application/Core]
      |
      v
[Database/Storage]
```

แทน diagram นี้ด้วยองค์ประกอบจริง ระบุ trust boundary และ network boundary ที่สำคัญ

## 3. Repository map

```text
/
├── src/                 # application source
│   ├── presentation/    # UI, HTTP, CLI หรือ transport adapters
│   ├── application/     # use cases และ orchestration
│   ├── domain/          # business rules และ domain types
│   └── infrastructure/  # database, network, filesystem adapters
├── tests/               # automated tests
├── docs/                # project documentation
├── scripts/             # repeatable local/CI utilities
└── [config files]
```

ปรับโครงสร้างให้ตรงกับ repository จริง และลบ directory ที่ไม่มีอยู่

## 4. Components and ownership

| Component | Responsibility | Public interface | Owner |
|---|---|---|---|
| `[COMPONENT]` | `[หน้าที่]` | `[API/event/function]` | `[TEAM]` |
| `[COMPONENT]` | `[หน้าที่]` | `[API/event/function]` | `[TEAM]` |

## 5. Dependency rules

- Presentation เรียก application layer; ไม่เข้าถึง storage โดยตรง
- Application orchestrates use cases; business rules สำคัญอยู่ใน domain
- Domain ไม่ import framework, database client หรือ network client
- Infrastructure implement interfaces ที่ชั้นในกำหนด
- Shared utilities ต้องไม่มี business ownership ที่คลุมเครือ
- ห้ามสร้าง circular dependency

หาก architecture จริงไม่ใช่ layered architecture ให้แทนกฎด้านบนด้วย dependency rules ที่ใช้จริง

## 6. Runtime data flow

### `[FLOW_NAME เช่น Create order]`

1. `[Actor]` ส่ง `[request/event]`
2. `[Entry component]` ตรวจ syntax และ authentication
3. `[Use case]` ตรวจ business rules
4. `[Repository/service]` อ่านหรือบันทึกข้อมูล
5. ระบบตอบ `[response/event]`

Failure behavior:

- Validation failure → `[status/error type]`
- Authentication/authorization failure → `[behavior]`
- Dependency timeout → `[retry/fallback behavior]`
- Partial failure → `[transaction/compensation behavior]`

## 7. Data model and storage

| Entity/table | Purpose | Owner | Retention/notes |
|---|---|---|---|
| `[NAME]` | `[PURPOSE]` | `[COMPONENT]` | `[RETENTION]` |

- Source of truth: `[SYSTEM]`
- Identifier strategy: `[UUID/sequence/etc.]`
- Transaction boundary: `[DESCRIPTION]`
- Migration tool/process: `[COMMAND_OR_DOC]`
- Backup/restore expectation: `[DESCRIPTION]`

Schema changes ต้องมี migration, rollback/forward-fix plan และ compatibility strategy เมื่อมีหลาย application version ทำงานพร้อมกัน

## 8. Interfaces

### Public API

- Contract location: `[OpenAPI/GraphQL/protobuf/path]`
- Versioning policy: `[POLICY]`
- Compatibility policy: `[POLICY]`

### Events and jobs

| Name | Producer | Consumer | Delivery/idempotency |
|---|---|---|---|
| `[EVENT]` | `[SERVICE]` | `[SERVICE]` | `[POLICY]` |

### External dependencies

| Dependency | Purpose | Timeout/retry | Failure impact |
|---|---|---|---|
| `[SERVICE]` | `[PURPOSE]` | `[POLICY]` | `[IMPACT]` |

## 9. Cross-cutting concerns

- Authentication: `[METHOD]`
- Authorization: `[POLICY AND ENFORCEMENT POINT]`
- Configuration: `[ENV/CONFIG STRATEGY]`
- Secrets: `[SECRET MANAGER; ห้ามใส่ค่าจริง]`
- Logging: `[STRUCTURE AND REDACTION RULES]`
- Metrics/tracing: `[TOOLS AND REQUIRED SIGNALS]`
- Caching: `[OWNER, TTL, INVALIDATION]`
- Rate limiting: `[BOUNDARY AND POLICY]`
- Localization/timezone: `[POLICY]`

## 10. Quality attributes

| Attribute | Target | How verified |
|---|---|---|
| Availability | `[TARGET]` | `[METHOD]` |
| Latency | `[P95/P99]` | `[METHOD]` |
| Throughput | `[TARGET]` | `[METHOD]` |
| Data integrity | `[GUARANTEE]` | `[METHOD]` |
| Security/privacy | `[STANDARD]` | `[METHOD]` |

## 11. Architecture decisions

การตัดสินใจที่มีผลระยะยาวให้สร้าง ADR ใน `docs/adr/NNNN-title.md` โดยมี Context, Decision, Alternatives, Consequences และ Status

| ADR | Decision | Status |
|---|---|---|
| `[0001-title]` | `[SUMMARY]` | `[Proposed/Accepted/Superseded]` |

## 12. Known constraints and technical debt

- `[CONSTRAINT_OR_DEBT พร้อมผลกระทบและ issue link]`
