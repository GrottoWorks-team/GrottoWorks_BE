# BuildingBlocks — chốt contract dùng chung

Tài liệu này là nguồn chốt cho các contract dùng chung giữa các service. Mọi service
(published qua Gateway) phải bám đúng các quy ước ở đây. Thay đổi contract → sửa file này
**trước**, tag người còn lại review (plan.md §4.1, "Contract").

## 1. JWT access token

Do Identity phát hành; Gateway/service khác validate. **Ký hiệu HMAC-SHA256**, issuer/audience/key
đọc từ config (`Jwt:Issuer`, `Jwt:Audience`, `Jwt:SigningKey` — key qua env/provider, không commit).

| Claim | Kiểu | Ý nghĩa |
|---|---|---|
| `sub` | `uuid` | `user_id` của account |
| `role` | `string` | Role code: `ADMIN`, `PARISH`, `LEADER`, `MO`, `VOLUNTEER` |
| `parishId` | `uuid?` | Tenant parish; **chỉ null cho ADMIN** (BR-67) |
| `communityId` | `uuid?` | Community hiện tại của volunteer (nếu có) |
| `jti` | `uuid` | Token id, chống replay |

Quy ước:
- Tên claim **viết thường theo đúng bảng trên** — mọi service đọc literal claim, không map
  inbound claim type (JWT bearer dùng `MapInboundClaims = false`, `RoleClaimType = "role"`,
  `NameClaimType = "sub"`).
- Access token ngắn hạn: **15 phút** (F-IDN-03). Refresh token **14 ngày**, rotate mỗi lần dùng,
  detect reuse → revoke cả family.

## 2. Event envelope (outbox / messaging)

`BuildingBlocks.Messaging` (F-PLT-05) publish các event theo envelope:

| Field | Kiểu | Ghi chú |
|---|---|---|
| `eventType` | `string` | `{domain}.{entity}.{action}` — ví dụ `identity.user.registered` |
| `version` | `int` | Version payload; đổi payload → bump version |
| `payload` | `object` | Record trong `<Service>.Contracts.Events` |
| `aggregateType` | `string` | `user`, `skill`, ... |
| `aggregateId` | `uuid` | Id aggregate |
| `occurredAt` | `DateTimeOffset` | UTC |

Event contract-first: record + `EventType` + `Version` khai báo trong `<Service>.Contracts`
trước khi hạ tầng publish có, để consumer code sớm.

## 3. API response envelope

Mọi endpoint trả JSON dạng `{ "data": ..., "meta": { "correlationId": "uuid" } }`.
Lỗi: RFC 9457 ProblemDetails kèm `code` ổn định + `correlationId` + `traceId`.

## 4. Interrim của Vũ (sẽ thay bằng BuildingBlocks.Web)

`BuildingBlocks.Web` (F-PLT-03) **đã có** và `ParishCoordination.Api` đã dùng chung
(`AddGrottoWorksWeb` / `UseGrottoWorksDefaults` / `ApiResults` / `IReadinessProbe`).
Tuy nhiên `Identity.Api` **vẫn còn** bản địa phương `Identity.Api.Common`
(ApiResponse/PagedResponse, CorrelationIdMiddleware, ExceptionHandlingMiddleware,
CurrentUser) — di chuyển sang package chung được ghi nhận ở đây để không phá 49 test
unit đang xanh; refactor Identity sang `BuildingBlocks.Web` + `BuildingBlocks.Security`
là việc còn lại của F-PLT-03/04, xóa bản địa phương sau khi test xanh.

## 5. Gọi service-to-service (`/internal/**`) — đề xuất của Vũ, cần Lâm review

Áp dụng cho `GET /internal/users` (F-IDN-09, đã có) và sau này `POST /internal/tasks`,
`POST /internal/support-transfers`, `GET /internal/files/{id}`.

| Mục | Quy ước |
|---|---|
| Xác thực | Header `X-Internal-Api-Key: <key>`; key chung cấu hình `InternalApi:ApiKey` (env `InternalApi__ApiKey`, compose lấy từ `INTERNAL_API_KEY` trong `.env`), tối thiểu 32 ký tự, so sánh constant-time. Thiếu cấu hình → mọi call bị 401 `INTERNAL_AUTH_REQUIRED` |
| Mạng | Gateway **không** có route `/internal/**` (gọi qua gateway → 404); chỉ service trong compose network gọi trực tiếp `http://identity.api:8080/internal/...` |
| OpenAPI | Endpoint internal `ExcludeFromDescription()` — không lộ cho FE/Mobile |
| Response | Cùng envelope `{ data, meta }` và ProblemDetails như API public |
| Client | Typed `HttpClient`, timeout, retry chỉ cho GET, circuit breaker (spec §6.2) |

### `GET /internal/users?ids=<uuid>,<uuid>` (Identity)

`ids` phân tách bằng dấu phẩy và/hoặc lặp tham số; 1–100 id (sai → 400). Id không tồn tại bị **bỏ qua**
(caller tự so sánh danh sách trả về). Thứ tự theo thứ tự `ids` gửi lên.

```json
{
  "data": [
    {
      "userId": "uuid",
      "displayName": "Nguyễn Văn A",
      "status": "ACTIVE",
      "role": "LEADER",
      "parishId": "uuid",
      "communityId": "uuid | null",
      "skillIds": ["uuid"]
    }
  ],
  "meta": { "correlationId": "uuid" }
}
```

Parish (F-PAR-08) kiểm tra `status == ACTIVE`, `role` đúng (LEADER/MO) và `parishId` khớp; Task (F-TSK-06)
dùng thêm `communityId`, `skillIds`. Không trả email/sđt (giảm dữ liệu cá nhân qua service khác).

### 5.2 Resource — feed tạm cho projection `work_area_scope` (F-RES-01)

Cho tới khi ParishCoordination publish `planning.work-area.*` / `planning.season.*`, Parish (hoặc script seed) gọi:

- `PUT http://resource.api:8080/internal/work-area-scopes/{workAreaId}` — body
  `{ seasonId, parishId, name, status, seasonStatus, communityId?, leaderUserId?, materialOfficerUserId?, version }`
  (`status`: `PLANNED|IN_PROGRESS|READINESS_REVIEW|NEEDS_CORRECTION|READY|COMPLETED`; `seasonStatus`:
  `PLANNING|ACTIVE|READY|COMPLETED|ARCHIVED`; snapshot có `version` nhỏ hơn bản đã lưu bị bỏ qua).
- `PUT http://resource.api:8080/internal/season-scopes/{seasonId}` — body `{ status }`.

**Đề nghị payload event của Lâm** chứa đúng các trường trên (+ envelope Mục 8), để consumer của Resource /
Readiness / Task chỉ map 1–1. Header `X-Internal-Api-Key` như §5.

### 5.3 Resource — feed tạm `task_projection` (F-RES-01)

Cho tới khi TaskExecution publish `task.status.changed` / `task.assignment.responded` / `task.purchase-task.assigned`:

- `PUT http://resource.api:8080/internal/task-scopes/{taskId}` — body
  `{ workAreaId, name, type, status, purchaseRequestId?, assigneeUserIds[], version }` (`type`: `WORK|PURCHASE|CORRECTIVE`;
  `status`: `OPEN|NOT_STARTED|IN_PROGRESS|SUBMITTED|REVISION_REQUIRED|COMPLETED|CANCELLED`; `assigneeUserIds` = volunteer có
  assignment `ACCEPTED`; PURCHASE bắt buộc `purchaseRequestId`; work area phải được project trước).

### 5.4 Resource — chuyển kho hỗ trợ vật tư (F-RES-20 ↔ F-TSK-15b, R-08)

- `POST http://resource.api:8080/internal/support-transfers` — body
  `{ supportResponseMaterialId, sourceWorkAreaId, targetWorkAreaId, materialId, quantity, approvedByUserId }`.
  - `201` → đã ghi `TRANSFER_OUT` cho work area nguồn; `200` → gọi lại y hệt (idempotent theo `supportResponseMaterialId`,
    không ghi thêm); `409 MATERIAL_INSUFFICIENT_AVAILABLE` → thiếu tồn chưa phân bổ (trả thẳng cho Leader);
    `409 SUPPORT_TRANSFER_CONFLICT` → cùng id nhưng dữ liệu khác; `422` → work area khác season / trùng nhau / chưa project.
  - Body trả về `SupportTransfer { supportResponseMaterialId, status: IN_TRANSIT|RECEIVED, quantity, ... }`.
- MO bên nhận gọi `POST /api/v1/support-response-materials/{itemId}/confirm-receipt` (Resource host, gateway route riêng)
  `{ receivedUsableQuantity, receivedUnusableQuantity?, note?, version? }` → `TRANSFER_IN` phần usable, event
  `support.transfer.confirmed { supportResponseMaterialId, sentQuantity, receivedUsableQuantity, receivedUnusableQuantity, ... }`
  để Task cập nhật FULFILLED/PARTIALLY_FULFILLED (consumer idempotent theo `supportResponseMaterialId`).

### 5.5 Reporting — nhận event (F-RPT-02) và payload đề nghị cho event của Task

Cho tới khi có outbox (F-PLT-05) và consumer MassTransit, producer (hoặc script seed) gửi từng event:

- `POST http://reporting.api:8080/internal/events` — body `{ eventId, eventType, version, occurredAt?, payload }` (envelope §2 +
  `eventId` = message id của broker, là khóa inbox). Trả `200 { outcome: APPLIED|DUPLICATE|IGNORED, contributionsRecorded,
  pointsAwarded }`; gửi lại cùng `eventId` → `DUPLICATE`, không tính lại (BR-55); `409 EVENT_CONCURRENT_UPDATE` → event khác
  đang cập nhật cùng contribution, gửi lại; `422 EVENT_PAYLOAD_INVALID` / `EVENT_SEASON_MISMATCH`. Event không dùng → `IGNORED`.
- Event của Resource / Readiness dùng đúng record trong `Resource.Contracts.Events` / `Readiness.Contracts.Events`
  (contribution + điểm: `purchase.delivery.confirmed`, `contribution.receipt.confirmed`, `contribution.donation.adjusted`;
  read model dashboard / báo cáo: thêm `material.shortage.changed`, `contribution.pledged`, `purchase.request.submitted/decided`,
  `readiness.item.failed`, `readiness.area.ready/reopened`).
- Task cho dashboard: `PUT http://reporting.api:8080/internal/task-scopes/{taskId}` như §5.3, thêm `dueAt?` (hạn task).
- **Đề nghị payload event của Lâm** (Reporting đang đọc theo các trường này, `Reporting.Application/Ingestion/ConsumedEvents.cs`):
  - `task.completion.reviewed { taskId, workAreaId, seasonId, parishId, taskName?, taskType, decision: APPROVED|REVISION_REQUIRED,
    volunteerUserIds[], reviewedByUserId, reviewedAt }` — `volunteerUserIds` = assignment ACCEPTED/COMPLETED được ghi nhận;
    task PURCHASE không tính ở đây (tính từ `purchase.delivery.confirmed`).
  - `attendance.hours.approved { attendanceId, taskId, workAreaId, seasonId, parishId, volunteerUserId, approvedHours,
    approvedByUserId, approvedAt }` — duyệt lại (correction) cùng `attendanceId` không tạo contribution mới.
  - `support.member.completed { supportResponseMemberId, supportRequestId, volunteerUserId, targetWorkAreaId, seasonId, parishId,
    completedAt }`.
