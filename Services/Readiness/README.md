# Readiness Service

Checklist sẵn sàng của từng khu vực, kết quả kiểm tra, công việc khắc phục, xác nhận / mở lại (owner: Vũ — spec §5.3,
tag `Readiness`). Trạng thái: F-RDY-02..06 — S6..S8 (làm 27/10).

## Kiến trúc

```
Services/Readiness/
├── Readiness.Domain/          # Checklists (ReadinessChecklist, ReadinessItem), Scope (work_area_scope, task_projection)
├── Readiness.Contracts/       # Event contract-first: readiness.item.failed, readiness.area.ready, readiness.area.reopened
├── Readiness.Application/     # Handler thường (không MediatR), WorkAreaAccess, ChecklistLoader, CorrectiveTaskSync
├── Readiness.Infrastructure/  # EF Core + Npgsql (readiness_db), store, migration
└── Readiness.Api/             # Minimal API + BuildingBlocks.Web/Security
tests/Readiness.UnitTests/     # xUnit + FluentAssertions, fake store (không cần DB)
```

Cùng khuôn với Resource: `DomainRuleException(code, kind)` → `DomainRuleTranslationMiddleware` (422/409/404/403/400/412),
enum `SNAKE_CASE_UPPER`, optimistic concurrency bằng `xmin` (`If-Match` hoặc `version` trong body).

## Chạy local

`dotnet run --project Services/Readiness/Readiness.Api` (port 5185) với `ConnectionStrings:ReadinessDatabase`,
`Jwt:SigningKey`, `InternalApi:ApiKey`; migrate tự động lúc startup. Docker: `readiness.api` port 6105.
Mẫu request: `Readiness.Api.http`.

## Endpoint

| Endpoint | operationId | Quyền | Function |
|---|---|---|---|
| `POST /api/v1/work-areas/{id}/readiness-checks` | `createReadinessCheck` | LEADER của khu vực | F-RDY-02 |
| `PUT /api/v1/readiness-checks/{id}/items` | `replaceReadinessItems` | LEADER (chỉ khi PLANNING; body là mảng → version qua `If-Match`) | F-RDY-02 |
| `GET /api/v1/readiness-checks/{id}` | `getReadinessCheck` | PARISH, LEADER, MO cùng parish | F-RDY-02 |
| `PUT /api/v1/readiness-checks/{id}/items/{itemId}` | `recordReadinessResult` | LEADER | F-RDY-03 |
| `POST /api/v1/readiness-checks/{id}/items/{itemId}/evidence` | `attachReadinessEvidence` | LEADER, VOLUNTEER được giao corrective task của item | F-RDY-03 |
| `PUT /api/v1/readiness-checks/{id}/items/{itemId}/corrective-task` | `linkCorrectiveTask` | LEADER | F-RDY-04 |
| `POST /api/v1/readiness-checks/{id}/items/{itemId}/recheck` | `recheckReadinessItem` | LEADER | F-RDY-05 |
| `POST /api/v1/readiness-checks/{id}/confirm` | `confirmAreaReadiness` | LEADER | F-RDY-05 |
| `POST /api/v1/readiness-checks/{id}/reopen` | `reopenReadinessCheck` | PARISH cùng parish, LEADER của khu vực | F-RDY-05 |
| `GET /api/v1/seasons/{id}/readiness` | `getSeasonReadiness` | PARISH, LEADER, MO cùng parish | F-RDY-06 |
| `PUT /internal/work-area-scopes/{id}`, `/internal/season-scopes/{id}`, `/internal/task-scopes/{id}` | — | `X-Internal-Api-Key` | feed projection tạm |

Ngoài parish → 404; season ARCHIVED → 409 `SEASON_READ_ONLY`.

## Quy tắc (BA 8.12 + 9.8, D-04, BR-52, BR-53, BR-66)

- Mỗi khu vực một checklist (unique `work_area_id` → 409 `READINESS_CHECK_EXISTS`, kể cả khi tạo song song).
  Item do leader tự định nghĩa (không template), tên không trùng, 1–100 item, `mandatory` mặc định true.
- Checklist: PLANNING → IN_REVIEW / NEEDS_CORRECTION → READY → REOPENED. Có kết quả đầu tiên là rời PLANNING;
  NEEDS_CORRECTION khi còn item bắt buộc FAILED / CORRECTIVE_ACTION / RECHECK.
- Item: PENDING → PASSED / FAILED → CORRECTIVE_ACTION → RECHECK → PASSED / FAILED. Kết quả đầu tiên chỉ cho item PENDING
  (sau reopen: cả item PASSED). Item bắt buộc FAILED phải có corrective task trước khi recheck (BR-66); item không bắt
  buộc FAILED được recheck trực tiếp.
- `linkCorrectiveTask`: task phải là CORRECTIVE, cùng khu vực, chưa CANCELLED (theo `task_projection`); task đã
  COMPLETED → item sang RECHECK ngay. Khi Task báo corrective task COMPLETED (`task-scopes` feed) → item sang RECHECK
  trong cùng transaction với projection.
- `confirmAreaReadiness`: mọi item bắt buộc PASSED, nếu không 409 `READINESS_MANDATORY_NOT_PASSED` (BR-52).
  `reopenReadinessCheck`: chỉ READY, lý do bắt buộc (BR-53).
- Version của checklist bảo vệ mọi thay đổi item (DbContext đánh dấu checklist modified khi item đổi → `xmin` tăng);
  response item trả `checkVersion` để gửi `If-Match` lần sau.
- Event (đang log, chờ outbox F-PLT-05): `readiness.item.failed`, `readiness.area.ready`, `readiness.area.reopened`.

## Việc còn mở

- Cột kỹ thuật ngoài ERD v3.6.2: `readiness_item.item_position` (thứ tự hiển thị), `readiness_item.evidence_file_ids`
  (đến khi NotificationAudit giữ liên kết file — F-NTF-01).
- Tạo corrective task từ Readiness cần `POST /internal/tasks` của TaskExecution; hiện trả 409
  `CORRECTIVE_TASK_CREATE_UNAVAILABLE` khi không gửi `taskId`.
- Chưa ràng buộc trạng thái work area (READINESS_REVIEW) khi confirm — Contract không nói rõ; Parish nhận
  `readiness.area.ready` để chuyển trạng thái (F-PAR-09).
- Projection nạp qua `/internal/*-scopes` đến khi có consumer `planning.*`, `task.status.changed`.
