# Resource Service

Vật tư, tồn kho, mua sắm, quyên góp, đồ mượn (owner: Vũ — spec §5.2, tag `Materials`, `Purchases`, `Contributions`).
Trạng thái: F-RES-01 (một phần), 02, 03, 04, 05 — S2/S3; F-RES-06, 07 — S4.

## Kiến trúc

```
Services/Resource/
├── Resource.Domain/          # Catalog, Scope (projection), Inventory (requirement, ledger, InventoryCalculator), Purchases
├── Resource.Contracts/       # Event contract-first: material.shortage.changed, purchase.request.submitted/decided
├── Resource.Application/     # Handler thường (không MediatR), WorkAreaAccess, InventoryReader, ShortageTracker
├── Resource.Infrastructure/  # EF Core + Npgsql (resource_db), store, migration
└── Resource.Api/             # Minimal API + BuildingBlocks.Web/Security
tests/Resource.UnitTests/     # xUnit + FluentAssertions, fake store (không cần DB)
```

- Dùng chung `BuildingBlocks.Web` (envelope, ProblemDetails, correlation id) và `BuildingBlocks.Security` (JWT, `ICurrentUser`)
  như ParishCoordination. Domain không phụ thuộc ASP.NET: ném `DomainRuleException(code, kind)`,
  `DomainRuleTranslationMiddleware` đổi sang `DomainException` của BuildingBlocks (Validation → 422, Conflict → 409,
  NotFound → 404, Forbidden → 403, BadRequest → 400, PreconditionFailed → 412).
- Enum JSON và cột trạng thái đều `SNAKE_CASE_UPPER` (`IN_PROGRESS`, `OPENING_STOCK`).
- Query list: tham số lạ, `size > 100`, sort/enum sai → 400 (`QueryReader`).

## Chạy local

`dotnet run --project Services/Resource/Resource.Api` (port 5183) với `ConnectionStrings:ResourceDatabase`,
`Jwt:SigningKey`, `InternalApi:ApiKey`; migrate tự động lúc startup. Docker: `resource.api` port 6103.
Mẫu request: `Resource.Api.http`.

## Endpoint

| Endpoint | operationId | Quyền | Function |
|---|---|---|---|
| `GET /api/v1/material-categories` | `listMaterialCategories` | USER (mặc định ACTIVE) | F-RES-02 |
| `POST /api/v1/material-categories` | `createMaterialCategory` | MO | F-RES-02 |
| `PATCH /api/v1/material-categories/{id}` | `updateMaterialCategory` | MO | F-RES-02 |
| `GET /api/v1/materials` | `listMaterials` | USER (`categoryId,status,q`, sort `name|code`) | F-RES-02 |
| `POST /api/v1/materials` | `createMaterial` | MO | F-RES-02 |
| `PATCH /api/v1/materials/{id}` | `updateMaterial` | MO (unit khóa khi đã có số lượng) | F-RES-02 |
| `GET /api/v1/work-areas/{id}/material-requirements` | `listMaterialRequirements` | USER cùng parish | F-RES-03 |
| `POST /api/v1/work-areas/{id}/material-requirements` | `createMaterialRequirement` | MO của work area | F-RES-03 |
| `PATCH /api/v1/material-requirements/{id}` | `updateMaterialRequirement` | MO của work area; `version`/`If-Match` | F-RES-03 |
| `POST /api/v1/inventory/opening-stock` | `recordOpeningStock` | MO của work area; 1 lần/(work area, material) | F-RES-04 |
| `GET /api/v1/inventory` | `listInventory` | PARISH, LEADER, MO (`seasonId,workAreaId,materialId,onlyShortage`) | F-RES-04 |
| `GET /api/v1/materials/{id}/allocation-history` | `getAllocationHistory` | PARISH, LEADER, MO (`from,to,workAreaId`) | F-RES-04 |
| `GET /api/v1/work-areas/{id}/shortages` | `getWorkAreaShortages` | PARISH, LEADER, MO | F-RES-05 |
| `GET /api/v1/seasons/{id}/material-needs` | `listMaterialNeeds` | USER cùng parish (chỉ shortage > 0) | F-RES-05 |
| `GET /api/v1/purchase-requests` | `listPurchaseRequests` | PARISH, LEADER, MO (`seasonId,workAreaId,status`, sort `createdAt`, mặc định mới nhất trước) | F-RES-06 |
| `POST /api/v1/purchase-requests` | `createPurchaseRequest` | MO của work area → PLANNING | F-RES-06 |
| `GET /api/v1/purchase-requests/{id}` | `getPurchaseRequest` | PARISH, LEADER, MO cùng parish (VOLUNTEER: xem "Việc còn mở") | F-RES-06 |
| `PATCH /api/v1/purchase-requests/{id}` | `updatePurchaseRequest` | MO của work area; chỉ PLANNING; `items` thay toàn bộ dòng; `version`/`If-Match` | F-RES-06 |
| `POST /api/v1/purchase-requests/{id}/submit` | `submitPurchaseRequest` | MO của work area: PLANNING → PENDING_APPROVAL | F-RES-06 |
| `POST /api/v1/purchase-requests/{id}/approve` | `approvePurchaseRequest` | LEADER **của work area** (D-01, BR-29) | F-RES-07 |
| `POST /api/v1/purchase-requests/{id}/reject` | `rejectPurchaseRequest` | LEADER của work area; `reason` bắt buộc (BR-31) | F-RES-07 |
| `GET /api/v1/purchase-requests/{id}/history` | `getPurchaseRequestHistory` | PARISH, LEADER, MO — `StateChange[]` cũ → mới | F-RES-06 |
| `PUT /internal/work-area-scopes/{id}` | — | Service (`X-Internal-Api-Key`) — feed tạm | F-RES-01 |
| `PUT /internal/season-scopes/{id}` | — | Service (`X-Internal-Api-Key`) — feed tạm | F-RES-01 |

## Quy tắc tồn kho (BA 8.5, Contract 7.1)

- Mọi thay đổi stock chỉ qua `material_transaction` (append-only, theo work area — BR-23/24). Dấu cố định theo loại
  (nhập > 0, xuất < 0) ở Domain **và** CHECK trong DB. Mọi phát sinh trừ `OPENING_STOCK` phải có `reference_type/id`;
  unique `(reference_type, reference_id, transaction_type)` → mỗi chứng từ chỉ post một lần.
- `InventoryCalculator`: `confirmedAvailable = SUM(ledger)`, `unallocated = available − allocated` (allocation: F-RES-15),
  `shortage = max(0, required − available)` (BR-25). `existing_quantity` của requirement chỉ để hiển thị.
- `material.shortage.changed` (v1) chỉ phát khi shortage của (work area, material) thực sự đổi.

## Purchase request (F-RES-06/07, BA 8.6 + 9.5, D-01)

- `PLANNING` → `PENDING_APPROVAL` (submit) → `APPROVED` / `REJECTED` (Leader của work area). Không có DRAFT (R-12,
  Contract v1.3.2). Chỉ sửa khi PLANNING; PLANNING được
  phép chưa có dòng/lý do, submit thì bắt buộc ≥ 1 dòng + lý do (BR-27). Phê duyệt áp dụng cho cả request.
- Dòng: material ACTIVE, mỗi material 1 lần/request, `requestedQuantity > 0` (2 số lẻ), `estimatedCost` = chi phí cả dòng
  (VND, ≥ 0, null = chưa ước tính). `estimatedTotal` = tổng các dòng, trả về dạng `Money { amount, currency: "VND" }`.
- **Chặn vượt shortage** (BA 8.6 "không tạo khi shortage = 0 trừ override + lý do"): mỗi dòng ≤ `shortage − phần đã xin ở
  request mở khác` (PENDING_APPROVAL, APPROVED, BUYER_ASSIGNED, PURCHASED) của cùng work area; vượt → 409
  `PURCHASE_EXCEEDS_SHORTAGE` trừ khi dòng có `shortageOverrideReason`. Kiểm tra lúc tạo/sửa dòng **và** lại lúc submit.
- Event: `purchase.request.submitted` (kèm `leaderUserId` để báo Leader), `purchase.request.decided` (`decision` =
  `APPROVED|REJECTED`, `decidedByUserId`, `note`) — Task (F-TSK-17) chỉ cho tạo task PURCHASE khi APPROVED.
  Payload: `Resource.Contracts/Events/PurchaseRequest*.cs`, cần Lâm review (plan §4.2 "giữa S4 chốt với Lâm").
- Bổ sung so với ERD v3.6 (đã ghi ở spec §3): bảng kỹ thuật `purchase_request_state_change` (nguồn của
  `getPurchaseRequestHistory`) và cột `purchase_request_item.shortage_override_reason`.

## Phạm vi (scope)

- Đọc: mọi user cùng parish (parish khác → 404). Ghi: MO được gán cho work area; duyệt purchase request: Leader được gán
  cho work area (403 `WORK_AREA_SCOPE_DENIED`).
  Season `ARCHIVED` → 409 `SEASON_READ_ONLY` (BR-07).
- Scope lấy từ projection `work_area_scope` (spec §2.2). **Tạm thời** nạp qua `/internal/*-scopes` vì Parish chưa
  publish `planning.work-area.*` / `planning.season.*`; consumer MassTransit sau này gọi đúng các handler
  `ApplyWorkAreaSnapshotHandler` / `ApplySeasonStatusHandler` (payload cần có: xem BuildingBlocks README §5.2).

## Việc còn mở

- Consumer `planning.*`, `task.materials.changed`, `task.completion.reviewed`, `task.purchase-task.assigned` +
  `task_projection` (F-RES-01 phần còn lại) — chờ Lâm publish event và outbox của F-PLT-05.
- Event đang chỉ log (`LoggingIntegrationEventPublisher`) — chưa qua outbox.
- Purchase request: VOLUNTEER đọc được request của task PURCHASE mình đã nhận — cần buyer từ `task.purchase-task.assigned`
  (F-RES-01), tạm thời trả 404. Chưa có endpoint hủy (Contract chưa có; BA cho PLANNING/PENDING/APPROVED → CANCELLED).
  PARTIALLY_RECEIVED sẽ trừ phần đã giao khỏi "đã xin" khi làm F-RES-10.
- Gateway: `/purchase-requests/{id}/purchase-tasks` thuộc Task (F-TSK-17) — khi Lâm thêm cần route riêng `Order: -1`.
- Allocation (F-RES-15) sẽ cộng vào `allocated` và vào lịch sử `getAllocationHistory`.
- Chưa có integration test tự động (Testcontainers); đã smoke E2E trên PostgreSQL 16 + gateway.
