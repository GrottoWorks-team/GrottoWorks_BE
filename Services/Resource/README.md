# Resource Service

Vật tư, tồn kho, mua sắm, quyên góp, đồ mượn (owner: Vũ — spec §5.2, tag `Materials`, `Purchases`, `Contributions`).
Trạng thái: F-RES-01 (một phần), 02, 03, 04, 05 — S2/S3; F-RES-06, 07 — S4; F-RES-12, 13 — S5; F-RES-13b, 13c, 15, 16 — S6; F-RES-17, 18, 20 — S7.

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
| `GET /api/v1/donors` | `listDonors` | MO (parish mình), ADMIN (`parishId` tùy chọn) — `q` (tên/sđt/email), `linked`, sort `name|createdAt` | F-RES-12 |
| `POST /api/v1/donors` | `createDonor` | MO, ADMIN (ADMIN bắt buộc `parishId`); `userId` tùy chọn | F-RES-12 |
| `PATCH /api/v1/donors/{id}` | `updateDonor` | MO, ADMIN; tên + liên hệ; `version`/`If-Match` | F-RES-12 |
| `GET /api/v1/donations` | `listDonations` | PARISH, LEADER, MO — `seasonId,workAreaId,donorId,type,status`, mới nhất trước | F-RES-13 |
| `POST /api/v1/donations` | `recordDirectDonation` | MO: MATERIAL → MO của work area nhận; MONEY → MO của work area (nếu có) hoặc MO của season | F-RES-13 |
| `GET /api/v1/donations/{id}` | `getDonation` | MO cùng parish; VOLUNTEER chỉ donation của donor gắn với tài khoản mình | F-RES-13 |
| `GET /api/v1/users/me/contributions` | `listMyContributions` | VOLUNTEER — donation của donor gắn với tài khoản (`seasonId`) | F-RES-13 |
| `GET /api/v1/seasons/{id}/contribution-needs` | `listContributionNeeds` | USER cùng parish — shortage trừ phần đã pledge chưa giao | F-RES-13b |
| `POST /api/v1/donations/material-pledges` | `pledgeMaterialDonation` | VOLUNTEER — vật tư work area đang có requirement | F-RES-13b |
| `POST /api/v1/donations/monetary-pledges` | `pledgeMonetarySupport` | VOLUNTEER — tiền cho season (work area tùy chọn) | F-RES-13b |
| `POST /api/v1/donations/{id}/cancel` | `cancelDonationPledge` | VOLUNTEER chủ pledge, MO của work area/season; `reason` bắt buộc | F-RES-13b |
| `POST /api/v1/donations/{id}/receipts` | `recordDonationReceipt` | MO của work area/season — một lần nhận thực tế của pledge | F-RES-13c |
| `POST /api/v1/material-allocations` | `allocateMaterial` | MO của work area — từ phần chưa phân bổ, tùy chọn gắn task đang mở | F-RES-15 |
| `GET /api/v1/material-allocations` | `listMaterialAllocations` | PARISH, LEADER, MO — `seasonId,workAreaId,taskId,materialId` | F-RES-15 |
| `POST /api/v1/inventory-adjustments` | `createInventoryAdjustment` | MO của work area — `DAMAGED|LOST|DISPOSED`, lý do bắt buộc, ≤ phần chưa phân bổ | F-RES-16 |
| `POST /api/v1/tasks/{id}/material-reports` | `submitTaskMaterialReport` | VOLUNTEER được giao task (assignee ACCEPTED), task COMPLETED | F-RES-17 |
| `GET /api/v1/tasks/{id}/material-reports` | `listTaskMaterialReports` | LEADER, MO cùng parish; VOLUNTEER được giao task | F-RES-17 |
| `POST /api/v1/task-material-reports/{id}/verify` | `verifyTaskMaterialReport` | MO của work area — trả phần remaining về kho | F-RES-17 |
| `POST /api/v1/task-material-reports/{id}/request-revision` | `requestMaterialReportRevision` | MO của work area — lý do bắt buộc | F-RES-17 |
| `POST /api/v1/support-response-materials/{itemId}/confirm-receipt` | `confirmSupportMaterialReceipt` | MO của work area nhận — `TRANSFER_IN` phần usable | F-RES-20 |
| `POST /api/v1/loan-offers` | `offerItemForLoan` | VOLUNTEER — nhập giùm người cho mượn (D-05) | F-RES-18 |
| `GET /api/v1/loan-offers` | `listLoanOffers` | MO — `status` (mặc định OFFERED) | F-RES-18 |
| `POST /api/v1/loan-offers/{id}/accept` · `/reject` | `acceptLoanOffer` · `rejectLoanOffer` | MO của work area / season; reject cần lý do | F-RES-18 |
| `GET /api/v1/borrowed-items` | `listBorrowedItems` | PARISH, LEADER, MO — `seasonId,workAreaId,status,overdue` | F-RES-18 |
| `GET /api/v1/borrowed-items/overdue` | `listOverdueBorrowedItems` | MO — `date`, `withinDays` (mặc định 2) | F-RES-18 |
| `POST /api/v1/borrowed-items/{id}/issue` · `PUT …/assignment` · `POST …/return` · `POST …/incidents` | `issueBorrowedItem` · `assignBorrowedItem` · `returnBorrowedItem` · `recordBorrowedItemIncident` | MO | F-RES-18 |
| `PUT /internal/work-area-scopes/{id}` | — | Service (`X-Internal-Api-Key`) — feed tạm | F-RES-01 |
| `PUT /internal/season-scopes/{id}` | — | Service (`X-Internal-Api-Key`) — feed tạm | F-RES-01 |
| `PUT /internal/task-scopes/{id}` | — | Service (`X-Internal-Api-Key`) — feed tạm `task_projection` (work area, type, status, assignee ACCEPTED) | F-RES-01 |
| `POST /internal/support-transfers` | — | TaskExecution (`X-Internal-Api-Key`) — `TRANSFER_OUT`, idempotent theo `supportResponseMaterialId` (BuildingBlocks README §5.4) | F-RES-20 |

## Quy tắc tồn kho (BA 8.5, Contract 7.1)

- Mọi thay đổi stock chỉ qua `material_transaction` (append-only, theo work area — BR-23/24). Dấu cố định theo loại
  (nhập > 0, xuất < 0) ở Domain **và** CHECK trong DB. Mọi phát sinh trừ `OPENING_STOCK` phải có `reference_type/id`;
  unique `(reference_type, reference_id, transaction_type)` → mỗi chứng từ chỉ post một lần.
- `InventoryCalculator`: `confirmedAvailable = SUM(ledger)`, `unallocated = available − allocated` (allocated = tổng
  `material_allocation_item`, Contract 7.1; phần dư sau task quay lại qua `TASK_RETURN` khi MO verify — F-RES-17),
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

## Donor (F-RES-12)

- Không bao giờ tự gộp donor theo tên/sđt/email (ERD note) — trùng tên vẫn tạo bản ghi mới; MO tự chọn donor có sẵn.
- `userId` (tùy chọn, unique) liên kết tài khoản volunteer: kiểm tra qua Identity `GET /internal/users` (cùng cách
  ParishCoordination, config `IdentityClient:BaseAddress` + `InternalApi:ApiKey`) — không tồn tại → 422
  `DONOR_USER_NOT_FOUND`, khác parish → 422 `DONOR_USER_OTHER_PARISH`, đã liên kết → 409 `DONOR_USER_ALREADY_LINKED`,
  Identity lỗi → 503 `IDENTITY_UNAVAILABLE`. Liên kết chỉ đặt lúc tạo (PATCH chỉ sửa tên + liên hệ).
- **Bổ sung so với ERD**: cột `donor.parish_id` — ERD không có, nhưng thiếu nó thì MO parish này đọc được sđt/email donor
  parish khác. MO chỉ thấy/sửa donor parish mình (khác → 404), ADMIN thấy tất cả.

## Donation ghi nhận trực tiếp (F-RES-13, BA 8.7, BR-35..39)

- Tạo donation `RECEIVED` + **một** `donation_receipt` trong cùng transaction. MONEY: `amount > 0`, không có item (BR-36),
  `workAreaId` tùy chọn (không có thì bắt buộc `seasonId`). MATERIAL: bắt buộc `workAreaId` + ≥ 1 item (BR-37), mỗi
  material 1 lần, `receivedQuantity > 0`, `0 ≤ unusableQuantity ≤ receivedQuantity`. `receivedAt` mặc định là lúc ghi,
  không được ở tương lai (cho lệch 5 phút).
- Tồn kho: mỗi dòng receipt có usable > 0 ghi 1 `material_transaction` `DONATION_RECEIPT` vào work area nhận,
  `reference = (DONATION_RECEIPT_ITEM, id dòng receipt)` → unique index ledger đảm bảo chỉ post **một lần** (BR-38). Phần
  unusable không vào kho. Donation, receipt và ledger lưu trong **một** `SaveChanges` (một transaction).
- Trạng thái item suy ra từ số lượng: `FULLY_RECEIVED`, `UNUSABLE` (nhận nhưng không dùng được hết)…
- Event: `contribution.receipt.confirmed` cho **mọi** receipt (`isDirect`, `isFirstReceipt`, `donorUserId` null với donor
  ngoài — không có điểm) + `material.shortage.changed` khi shortage đổi. Payload: `Resource.Contracts/Events/ContributionReceiptConfirmed.cs`.
- `listMyContributions`: Contract chưa có schema `Contribution` → tạm trả `Donation` của donor gắn với tài khoản.

## Pledge (F-RES-13b, D-02, BA 8.7)

- Pledge đầu tiên tự tạo donor gắn tài khoản volunteer (tên lấy từ Identity; Identity lỗi → tên tạm "Tình nguyện viên",
  MO sửa sau), các pledge sau dùng lại. Donation `PLEDGED`, **không** đụng kho hay tiền đã nhận (BR-39).
- MATERIAL: chỉ vật tư work area đang có requirement ACTIVE (422 `PLEDGE_MATERIAL_NOT_REQUIRED`), `pledgedQuantity > 0`.
  MONEY: `amount > 0`, season bắt buộc, work area tùy chọn.
- `listContributionNeeds`: `remainingNeed = max(0, shortage − pledgedOutstanding)` để volunteer không pledge trùng.
  Nhu cầu tiền cần ngân sách season (ParishCoordination) → chưa liệt kê.
- Lần nhận (F-RES-13c, BR-38): MONEY → `receivedAmount`; MATERIAL → `items[{donationItemId, receivedQuantity,
  unusableQuantity}]`. Cộng dồn received/unusable/amount; PLEDGED → PARTIALLY_RECEIVED → RECEIVED khi đủ; nhận vượt
  cam kết vẫn ghi và trả `warnings`. Phần usable của mỗi dòng post kho đúng một lần như donation trực tiếp. Donation
  RECEIVED/CANCELLED không nhận thêm (409). Response: receipt + `donationStatus` + `warnings`.
- Hủy: chỉ PLEDGED / PARTIALLY_RECEIVED, lý do 1–500 ký tự; receipt đã nhận giữ nguyên. Event `contribution.pledged`
  (kèm `materialOfficerUserId` để báo MO).

## Allocation (F-RES-15, BR-33, BR-34)

- Cấp từ phần **chưa phân bổ** của đúng work area (BR-24); thiếu → 409 `MATERIAL_INSUFFICIENT_AVAILABLE`. Tùy chọn
  gắn task (phải có trong `task_projection`, cùng work area, chưa COMPLETED/CANCELLED). Không đổi ledger hay shortage.
- Đồng thời: lệnh xuất kho của một work area chạy trong transaction có `pg_advisory_xact_lock` theo work area
  (`IWorkAreaStockLock`) → hai MO lấy phần cuối thì chỉ một thành công (BA edge case 19; smoke: 6 request song song → 1×201,
  5×409). Dùng lại cho chuyển kho (F-RES-20) và điều chỉnh tồn (F-RES-16).
- `allocated_quantity` là lịch sử, không sửa/xóa (BR-34); Contract chưa có endpoint hủy allocation.
- Điều chỉnh tồn (F-RES-16): ghi `ADJUSTMENT_DAMAGED/LOST/DISPOSED` âm, `reference = (INVENTORY_ADJUSTMENT, id riêng)` vì
  ERD không có bảng adjustment; lý do lưu ở `transaction_note`. Không bao giờ cộng lại usable (BR-43). Chỉ trừ được phần
  chưa phân bổ (vật tư đã cấp cho task báo hỏng/mất qua material report — F-RES-17).

## Task material report (F-RES-17, BR-44..51)

- Chỉ khi task `COMPLETED` (BR-44), chỉ volunteer có assignee ACCEPTED (`task_projection.assignee_user_ids`). Mỗi
  allocation item của task đúng **một** dòng, không thêm material khác (BR-45); `remaining + damaged + lost ≤ allocated`.
- Một report/task (unique `task_id`); `REVISION_REQUIRED` → volunteer gửi lại vào chính report đó (thay dòng).
- Verify (MO của work area, BR-47): nhập `verified_*` cho mọi dòng; `verifiedRemaining > 0` → `TASK_RETURN` vào **đúng**
  work area, `reference = (TASK_MATERIAL_REPORT_ITEM, id dòng)` → post một lần (BR-49). Damaged/lost không vào kho (BR-50).
  Allocation giữ nguyên (BR-51); `usedQuantity = allocated − remaining − damaged − lost`. Một transaction.
- Event `material-report.submitted` (kèm MO cần verify), `material-report.verified`.
- **Bổ sung so với ERD**: cột `task_material_report.revision_reason` (lý do trả về cho volunteer xem).

## Chuyển kho hỗ trợ vật tư (F-RES-20, D-03, BR-63/64, R-08/22/23)

- Task duyệt → gọi đồng bộ `POST /internal/support-transfers`: dưới lock tồn kho work area **nguồn**, số lượng ≤ phần
  chưa phân bổ (BR-63) → `TRANSFER_OUT`. Idempotent theo `supportResponseMaterialId` (smoke: 5 lần gọi song song y hệt →
  1×201, 4×200, đúng 1 dòng ledger). Hai work area phải khác nhau, cùng season.
- Bảng kỹ thuật `support_transfer` (khóa = `support_response_material_id` của Task) giữ trạng thái `IN_TRANSIT → RECEIVED`
  và số đã gửi để MO bên nhận xác nhận đúng; `support_response_material` vẫn thuộc TaskExecution.
- Một MO bên nhận xác nhận (R-23): `usable + unusable ≤ đã gửi`, chỉ usable → `TRANSFER_IN` (BR-64). Cả hai dòng ledger
  `reference = (SUPPORT_RESPONSE_MATERIAL, itemId)` → mỗi loại post một lần. Event `support.transfer.confirmed`.

## Đồ mượn (F-RES-18, D-05, BR-41..43)

- Volunteer nhập loan offer (tên + liên hệ người cho mượn, ngày trả dự kiến bắt buộc — BR-41) và là đầu mối liên hệ;
  event `loan.offer.submitted` báo MO. OFFERED → ACCEPTED/REJECTED (lý do) → ISSUED → IN_USE (work area/task cùng season)
  → RETURNED với tình trạng GOOD/DAMAGED/LOST (BR-42). OVERDUE suy ra từ `expected_return_date` (giờ Việt Nam).
- Đồ mượn **không** thuộc catalog và **không** vào ledger. `borrow_transaction`: dòng ISSUE (đóng lại khi trả, ghi tình
  trạng + số lượng trả) và dòng INCIDENT (DAMAGED/LOST khi đang mượn, không có ngày trả).
- Job nhắc (`BorrowedItemReminderService`, mỗi `BorrowedItems:ReminderIntervalMinutes` = 60): hạn hôm nay/mai →
  `borrowed-item.return-due`, quá hạn → `borrowed-item.overdue`, mỗi item tối đa 1 lần/ngày (`last_reminded_on`).
  Tắt bằng `BorrowedItems:RemindersEnabled=false`.
- **Bổ sung so với ERD**: cột `borrowed_item.rejection_reason`, `borrowed_item.last_reminded_on`.

## Phạm vi (scope)

- Đọc: mọi user cùng parish (parish khác → 404). Ghi: MO được gán cho work area; duyệt purchase request: Leader được gán
  cho work area (403 `WORK_AREA_SCOPE_DENIED`).
  Season `ARCHIVED` → 409 `SEASON_READ_ONLY` (BR-07).
- Scope lấy từ projection `work_area_scope` (spec §2.2). **Tạm thời** nạp qua `/internal/*-scopes` vì Parish chưa
  publish `planning.work-area.*` / `planning.season.*`; consumer MassTransit sau này gọi đúng các handler
  `ApplyWorkAreaSnapshotHandler` / `ApplySeasonStatusHandler` (payload cần có: xem BuildingBlocks README §5.2).

## Việc còn mở

- Consumer `planning.*`, `task.status.changed`, `task.materials.changed`, `task.completion.reviewed`,
  `task.purchase-task.assigned` (F-RES-01 phần còn lại; `task_projection` đã có, đang nạp qua `/internal/task-scopes`) — chờ Lâm publish event và outbox của F-PLT-05.
- Event đang chỉ log (`LoggingIntegrationEventPublisher`) — chưa qua outbox.
- Purchase request: VOLUNTEER đọc được request của task PURCHASE mình đã nhận — cần buyer từ `task.purchase-task.assigned`
  (F-RES-01), tạm thời trả 404. Chưa có endpoint hủy (Contract chưa có; BA cho PLANNING/PENDING/APPROVED → CANCELLED).
  PARTIALLY_RECEIVED sẽ trừ phần đã giao khỏi "đã xin" khi làm F-RES-10.
- `recordDirectDonation` cần `Idempotency-Key` (Contract 4.1) nhưng `IdempotencyFilter` (F-PLT-07, Lâm) chưa có → client
  retry có thể ghi trùng donation. Bằng chứng ảnh (`evidenceFileIds`) chờ F-NTF-01.
- Gateway: `/purchase-requests/{id}/purchase-tasks` thuộc Task (F-TSK-17) — khi Lâm thêm cần route riêng `Order: -1`.
- Chưa có integration test tự động (Testcontainers); đã smoke E2E trên PostgreSQL 16 + gateway.
