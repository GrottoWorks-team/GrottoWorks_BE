# GrottoWorks Backend — Kế hoạch implement

**Ngày lập:** 07/10/2026 · **Dựa trên:** `GrottoWorks_BE_Task_Assignment_Spec_v1.md` (v1.2), BA v1.1, ERD v3.5, API Contract v1.2
**Team:** Nguyễn Tiến Lâm (Leader) · Nguyễn Trí Vũ
**Thời gian:** S0 (07/10/2026) → S9 (28/02/2027), 03/2027 tài liệu + bảo vệ

> File này là kế hoạch **thực thi**: mỗi function ID trong spec được tách thành các bước code cụ thể, thứ tự làm, phụ thuộc và tiêu chí xong. Khi spec đổi, cập nhật spec trước rồi sửa file này trong cùng PR.

---

## 0. Hiện trạng repo (`GrottoWorks_BE`, nhánh `main`, commit `69d2f0f`)

| Thành phần | Trạng thái |
|---|---|
| `Services/ParishCoordination` | Có Api / Application / Domain / Infrastructure; entity `Parish`; `listParishes`, `createParish`; migration `InitialCreateParish`; còn code mẫu `weatherforecast` |
| Pattern Application | Handler class thường (`CreateParishCommandHandler.HandleAsync`), đăng ký `AddScoped` trong `DependencyInjection.cs` — **không dùng MediatR**. Giữ pattern này cho mọi service |
| `ApiGateway/`, `BuildingBlocks/` | Thư mục rỗng |
| Chưa có | `.Contracts`, `tests/`, docker-compose, auth, messaging, CI |
| Nhánh khác | `origin/lam-dev-1` chưa merge |

---

## 1. Quy trình chuẩn cho mỗi function

Áp dụng cho mọi function ID, cả hai người:

1. **Issue** — 1 function ID = 1 issue trên GitHub Project (label `owner:lam|vu`, `P0|P1|P2`, `sprint:Sx`).
2. **Branch** — `feat/<service>/<function-id>` (vd. `feat/resource/F-RES-05`).
3. **Domain** — entity/aggregate + enum + method nghiệp vụ (state transition, kiểm tra BR) trong `.Domain`; ném `DomainException(code)` khi vi phạm rule.
4. **Infrastructure** — `IEntityTypeConfiguration` khớp ERD v3.5 (tên bảng/cột snake_case, unique, check), repository, migration `dotnet ef migrations add <FunctionId>_<Mô tả>`.
5. **Application** — Command/Query + Handler, scope check (`ICurrentUser` + projection `work_area_scope`), ghi outbox event nếu có.
6. **Api** — Minimal API, `WithName(operationId)` + `WithTags`, request/response record theo schema API Contract, envelope `{ data, meta }`.
7. **Test** — unit test domain rule; integration test (Testcontainers PostgreSQL) cho happy path + 1 lỗi chính.
8. **Contract** — event mới/đổi payload đặt trong `<Service>.Contracts`, tăng `Version`, tag người kia review.
9. **PR** — commit `<type>(<Service>): <mô tả>`, review chéo, CI xanh, cập nhật `.http` file mẫu.

**Definition of Done:** chạy qua gateway · ProblemDetails + `code` · 422/409/412 đúng chỗ · scope check · test pass · OpenAPI đúng · event qua outbox.

---

## 2. Lộ trình tổng thể

| Sprint | Thời gian | Mục tiêu demo | Lâm | Vũ |
|---|---|---|---|---|
| S0 | 07/10 – 18/10 | `docker compose up`, tạo parish | PLT-01..03, PAR-01 | IDN-01, chốt JWT claim |
| S1 | 19/10 – 01/11 | Login → gọi API qua gateway | PLT-04..06, PAR-02, 04, 05, 07 | IDN-02, 03, 05, 06 |
| S2 | 02/11 – 15/11 | Season + work area + gán leader/MO, activate | PAR-06, 08, TSK-01..03, PLT-08, 09 | IDN-04, 07..10, RES-01, 02 |
| S3 | 16/11 – 29/11 | Tạo task, assign/apply, xem shortage | TSK-04, 06, 07, 08, NTF-01, PLT-07 | RES-03, 04, 05 |
| S4 | 30/11 – 13/12 | Check-in duyệt, progress, nghiệm thu; PR → duyệt → task mua | TSK-05, 09, 10, 11, 17 | RES-06, 07 |
| S5 | 14/12 – 27/12 | Mua → xác nhận giao → stock tăng; donation | TSK-12, 13, PAR-09, 10, NTF-02, 03 | RES-09, 10, 12, 13 |
| S6 | 28/12 – 10/01 | Support nhân lực; pledge; allocation | TSK-14, 15, NTF-04 | RES-11, 13b, 13c, 15, 16, RDY-02, RPT-01 |
| S7 | 11/01 – 24/01 | Support vật tư; material report; readiness; điểm | TSK-15b, 16, hỗ trợ FE/Mobile | RES-17, 18, 20, RDY-03..05, RPT-02, 03 |
| S8 | 25/01 – 07/02 | Dashboard + báo cáo | NTF-05, 06, fix bug | RDY-06, RES-14, RPT-04, 05 |
| Tết | 06/02 – 14/02 | Buffer | — | — |
| S9 | 15/02 – 28/02 | Full flow demo | Hardening, E2E 17 kịch bản, perf | RPT-06, hardening, test |
| — | 03/2027 | Bảo vệ | Tài liệu, deploy | Tài liệu, deploy |

---

## 3. Kế hoạch của Lâm

### 3.1 Platform — S0 → S3

| Bước | Function | Việc cần làm | Output |
|---|---|---|---|
| 1 | F-PLT-01 | Merge `lam-dev-1`; xóa `weatherforecast` khỏi `Program.cs`; thêm project `ParishCoordination.Contracts` và `tests/ParishCoordination.UnitTests`, `tests/ParishCoordination.IntegrationTests`; README "tạo service mới" | Template chuẩn để Vũ copy |
| 2 | F-PLT-02 | `docker-compose.yml`: PostgreSQL 17 (script init tạo `identity_db, parish_db, task_db, resource_db, readiness_db, reporting_db, notify_db`), RabbitMQ 4 management, Redis, MinIO; `.env.example`; `docker-compose.override.yml` cho service | `docker compose up` |
| 3 | F-PLT-03 | `BuildingBlocks.Web`: `ApiResponse<T>` / `PagedResponse<T>`, `DomainException` → ProblemDetails handler (`code`, `traceId`), parser `page/size/sort` (size ≤ 100, field lạ → 400), `CorrelationIdMiddleware`, health check `/health/live` `/health/ready`, extension `AddGrottoWorksDefaults()` | Package dùng chung, Parish dùng trước |
| 4 | F-PLT-04 | `BuildingBlocks.Security`: JWT bearer (issuer/audience/key từ config), `ICurrentUser { UserId, Role, ParishId, CommunityId }`, policy `RequireRole(...)`, helper `EnsureSameParish` | Chờ Vũ chốt claim (giữa S1) |
| 5 | F-PLT-05 | `BuildingBlocks.Messaging`: MassTransit + RabbitMQ, `IntegrationEvent` envelope (Mục 8 API Contract), EF Core Outbox/Inbox (`AddEntityFrameworkOutbox`), retry 3 lần → `_error` queue, base consumer idempotent theo `eventId` | Viết sample publish/consume giữa Parish ↔ service test |
| 6 | F-PLT-06 | `ApiGateway` (YARP): route `/api/v1/{service-prefix}/**`, validate JWT, rate limit (fixed window) cho `/auth/*`, `/files/uploads`, `/reports/exports`, forward `X-Correlation-Id` | Login → gọi Parish qua gateway |
| 7 | F-PLT-08 | Gateway gom `/openapi/v1.json` từng service, Scalar UI | S2 |
| 8 | F-PLT-09 | GitHub Actions: `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes` | S2 |
| 9 | F-PLT-07 | `IdempotencyFilter` (Redis, key + hash body + response, TTL 24h; trùng key khác body → 422) | S3, trước RES-09 / TSK-11 |

### 3.2 ParishCoordination — S0 → S5

| Thứ tự | Function | Domain / DB | Endpoint | Rule & test chính | Event |
|---|---|---|---|---|---|
| 1 | F-PAR-01 | `Parish` (có) | `getParish`, `updateParish` (+ 2 cái đã có) | ADMIN/PARISH; status ACTIVE/INACTIVE | — |
| 2 | F-PAR-02 | `Community` | `listCommunities`, `createCommunity`, `updateCommunity` | Tên unique trong parish; deactivate thay xóa | — |
| 3 | F-PAR-04 | `WorkAreaCategory` | CRUD | Đã tham chiếu → chỉ deactivate | — |
| 4 | F-PAR-05 | `ChristmasSeason` (+ `estimated_budget`) | `listSeasons`, `createSeason`, `getSeason`, `updateSeason` | BR-01..03; unique tên **và** năm trong parish; chỉ sửa DRAFT/PLANNING | — |
| 5 | F-PAR-07 | `WorkArea` | `listWorkAreas`, `createWorkArea`, `getWorkArea`, `updateWorkArea` | BR-04, BR-05; lịch nằm trong season | `planning.work-area.created/updated` |
| 6 | F-PAR-08 | cột leader/officer/community | `assignWorkAreaCommunity`, `assignWorkAreaLeader`, `assignMaterialOfficer` | BR-06; gọi `/internal/users` kiểm tra ACTIVE + đúng role | `planning.work-area.assigned` |
| 7 | F-PAR-06 | state machine season | `activateSeason`, `completeSeason`, `archiveSeason` | Không activate khi work area thiếu leader/MO; không 2 season ACTIVE trùng thời gian; archive → read-only | `planning.season.activated/completed/archived` |
| 8 | F-PAR-09 | state machine work area | Consumer | PLANNED → IN_PROGRESS → READINESS_REVIEW → NEEDS_CORRECTION/READY → COMPLETED | consume `task.status.changed`, `readiness.area.*` |
| 9 | F-PAR-10 | read-model `work_area_progress` | `getWorkAreaProgress` | % task COMPLETED | consume event Task |

**Mốc giao cho Vũ:** event `planning.work-area.*` + `planning.season.*` ổn định **hết S2**.

### 3.3 TaskExecution — S2 → S7

Khởi tạo service theo template (S2, ngày đầu): `TaskExecution.*`, `task_db`, outbox/inbox, projection.

| Thứ tự | Function | Việc chính | Phụ thuộc | Sprint |
|---|---|---|---|---|
| 1 | F-TSK-01 | Bảng `work_area_scope`, `user_status_projection`; consumer `planning.*`, `identity.user.locked` | PAR-07/08, IDN-08 | S2 |
| 2 | F-TSK-02 | `Task` aggregate (`task_type`, `completion_criteria`, version); CRUD; BR-08/09; chặn season ARCHIVED | TSK-01 | S2 |
| 3 | F-TSK-03 | `publishTask`, `cancelTask` (reason bắt buộc) | TSK-02 | S2 |
| 4 | F-TSK-04 | `task_skill`, `task_material`; `replaceTaskSkills/Materials/CompletionCriteria`; BR-10 | RES-02 (material id), IDN-06 | S3 |
| 5 | F-TSK-06 | `TaskAssignment`; assign/accept/decline; BR-11..14; capacity + override reason | IDN-09 | S3 |
| 6 | F-TSK-07 | apply/approve/reject; tranh slot cuối → optimistic concurrency (test 2 request song song) | TSK-06 | S3 |
| 7 | F-TSK-08 | `listMyTasks` | TSK-06 | S3 |
| 8 | F-TSK-17 | Consumer `purchase.request.decided`; `POST /purchase-requests/{id}/purchase-task`; consumer `purchase.delivery.confirmed` → task COMPLETED | RES-07 (Vũ) | S4 |
| 9 | F-TSK-11 | `attendance_request`, `attendance`; requestCheckIn → Leader duyệt **từng** request; check-out trực tiếp; `working_hours`; Idempotency-Key | PLT-07 | S4 |
| 10 | F-TSK-09 | `task_progress`, `progress_evidence`; fileId phải complete (`/internal/files/{id}`) | NTF-01 | S4 |
| 11 | F-TSK-10 | submit/review/approve/revision/resubmit; approve set `approved_by/at` + task COMPLETED trong 1 transaction | TSK-09 | S4 |
| 12 | F-TSK-05 | `listVolunteerMatches` (skill, community, availability, xung đột lịch BR-15), cache Redis | IDN-07 | S4 |
| 13 | F-TSK-12 | Attendance correction BR-20..22 | TSK-11 | S5 |
| 14 | F-TSK-13 | `listMyAttendance`, `getWorkingHours`, `listLongOpenSessions` + `BackgroundService` quét phiên mở lâu | TSK-11 | S5 |
| 15 | F-TSK-14 | Support request STAFFING/MATERIAL/MIXED, position + skill (AND), material item | — | S6 |
| 16 | F-TSK-15 | Response nhân lực, approve member → tạo assignment | TSK-14 | S6 |
| 17 | F-TSK-15b | Response vật tư (MO nguồn), approve → `support.material.approved`; consume `support.transfer.confirmed` → FULFILLED/PARTIALLY_FULFILLED | RES-20 (Vũ) | S7 |
| 18 | F-TSK-16 | `getCrossCommunitySupportSummary` | TSK-14/15 | S7 |
| + | Internal | `POST /internal/tasks` cho corrective task (Idempotency-Key) | — | **hết S6** |

**Mốc giao cho Vũ:** `task.materials.changed`, `task.completion.reviewed`, `task.assignment.responded` hết S4; `POST /internal/tasks` hết S6; `support.material.approved` giữa S7.

### 3.4 NotificationAudit — S3 → S8

| Thứ tự | Function | Việc chính | Sprint |
|---|---|---|---|
| 1 | F-NTF-01 | `file_object`; `createUploadRequest` (presigned PUT MinIO), `completeUpload`, `getDownloadUrl`, `deleteUnattachedFile`; giới hạn type/size; `GET /internal/files/{id}` | S3 (**mốc cho Vũ**) |
| 2 | F-NTF-02 | Consumer theo bảng sự kiện BA mục 14 → `notification`; template không chứa dữ liệu nhạy cảm | S5 |
| 3 | F-NTF-03 | list/read/read-all, preference, rule | S5 |
| 4 | F-NTF-04 | `activity_log` append-only; consumer mọi approve/confirm/reject/lock/export; `listActivityLogs`, `getActivityLog`, `getResourceHistory` | S6 |
| 5 | F-NTF-05 | FCM push | S8 (P2) |
| 6 | F-NTF-06 | System export/backup (202 + job) | S8 (P2) |

---

## 4. Kế hoạch của Vũ

### 4.1 Identity — S0 → S2

Khởi tạo (F-IDN-01, S0): copy template Parish → `Services/Identity/Identity.{Api,Application,Domain,Infrastructure,Contracts}` + `tests/`; DB `identity_db`; seed 5 role (`ADMIN, PARISH, LEADER, MO, VOLUNTEER`) + tài khoản ADMIN mặc định qua migration/`DataSeeder`.

| Thứ tự | Function | Domain / DB | Endpoint | Rule & test chính | Sprint |
|---|---|---|---|---|---|
| 1 | — | **Chốt JWT claim** `sub, role, parishId, communityId` với Lâm, ghi vào `BuildingBlocks/README.md` | — | — | S0 |
| 2 | F-IDN-02 | `AppUser` (email normalize, `parish_id`, status), `PasswordHasher<T>` | `registerUser`, `login` | Email unique; LOCKED/INACTIVE không login; sai mật khẩu không lộ thông tin | S1 |
| 3 | F-IDN-03 | `refresh_token` (hash, family, `replaced_by`, `revoked_at`) | `refreshToken`, `logout` | Access 15'; rotate; reuse → revoke cả family; denylist Redis | S1 (**giao JWT giữa S1**) |
| 4 | F-IDN-05 | `volunteer_profile` | `getMyProfile`, `updateMyProfile` | — | S1 |
| 5 | F-IDN-06 | `Skill` | `listSkills`, `createSkill`, `updateSkill` | Đã tham chiếu → deactivate | S1 |
| 6 | F-IDN-04 | `password_reset_token` (hash, 1 lần, hết hạn) | `requestPasswordReset`, `resetPassword` | Luôn trả 202 dù email không tồn tại; revoke refresh token sau reset | S2 |
| 7 | F-IDN-07 | `volunteer_skill`, `volunteer_availability` | `replaceMySkills`, `replaceMyAvailability` | Unique (user, skill); BR-70 `available_to > available_from`, không chồng lấn | S2 |
| 8 | F-IDN-08 | Admin user | `listUsers`, `createUser`, `updateUser`, `lockUser`, `unlockUser`, `assignUserRole`, `listAssignableRoles` | 1 role/account; BR-67 `parish_id` bắt buộc trừ ADMIN; lock → revoke token | S2 |
| 9 | F-IDN-09 | Query | `GET /internal/users?ids=` | Chỉ service-to-service (policy client credential/API key nội bộ); trả status, role, communityId, skills | S2 (**mốc cho Lâm**) |
| 10 | F-IDN-10 | Query | `listParishUsers` | Chỉ user cùng parish; filter community/role/skill | S2 |

Event publish: `identity.user.registered`, `identity.user.locked/unlocked`.

### 4.2 Resource — S2 → S8

Khởi tạo (S2): `Services/Resource/*`, `resource_db`, outbox/inbox.

**Thiết kế cốt lõi (làm trước F-RES-04):**

- `material_transaction` là **ledger append-only** duy nhất làm thay đổi stock. Không có cột stock lưu cứng; tồn kho = tổng hợp ledger theo `(work_area_id, material_id)` (có thể thêm view/materialized view sau nếu chậm).
- Unique `(reference_type, reference_id)` trên transaction phát sinh từ chứng từ → post stock đúng 1 lần.
- Service `InventoryCalculator`: `on_hand`, `allocated`, `unallocated`, `shortage = max(0, required − on_hand − pending_incoming...)` theo công thức BA 8.5 — viết unit test kỹ trước khi dùng ở RES-05/10/15/17/20.
- Thao tác tranh chấp (allocation, transfer) dùng `SELECT ... FOR UPDATE` hoặc isolation `Serializable` + retry.

| Thứ tự | Function | Việc chính | Phụ thuộc | Sprint |
|---|---|---|---|---|
| 1 | F-RES-01 | `work_area_scope`, `task_projection`; consumer `planning.*`, `task.materials.changed`, `task.completion.reviewed`, `task.assignment.responded` (task PURCHASE → lưu buyer) | PAR-07/08 (Lâm) | S2 |
| 2 | F-RES-02 | `material_category`, `material` (code unique, đơn vị tính) | — | S2 |
| 3 | F-RES-03 | `material_requirement` unique (work_area, material), quantity > 0, MO đúng work area | RES-01 | S3 |
| 4 | F-RES-04 | `material_transaction` + `recordOpeningStock`, `listInventory`, `getAllocationHistory`; BR-23, BR-24 | thiết kế ledger | S3 |
| 5 | F-RES-05 | `getWorkAreaShortages`, `listMaterialNeeds`; BR-25; event `material.shortage.changed` | RES-03/04 | S3 |
| 6 | F-RES-06 | `purchase_request` + items; CRUD/submit/history; BR-27; shortage = 0 → cần override + lý do | RES-05 | S4 |
| 7 | F-RES-07 | `approvePurchaseRequest`, `rejectPurchaseRequest` — **role LEADER** đúng work area (D-01); reject cần lý do (BR-31); event `purchase.request.submitted/decided` | RES-06 | S4 (**giữa S4 chốt với Lâm**) |
| 8 | F-RES-12 | Donor CRUD; không auto-merge; link `user_id` tùy chọn | — | S5 |
| 9 | F-RES-09 | `purchase`, `purchase_item`; `recordPurchase`, `attachPurchaseReceipt`; chỉ buyer (assignee ACCEPTED của task PURCHASE); status PURCHASED, stock **chưa** tăng (BR-26); Idempotency-Key | TSK-17, NTF-01, PLT-07 | S5 |
| 10 | F-RES-10 | `confirmPurchaseDelivery` (usable qty → ledger + tính lại shortage trong 1 transaction; giao thiếu → còn phần dở), `requestPurchaseInfo`; event `purchase.delivery.confirmed` | RES-09 | S5 |
| 11 | F-RES-13 | `donation`, `donation_item`, `donation_receipt(_item)`; `recordDirectDonation` (RECEIVED + 1 receipt, 1 transaction); `listDonations`, `getDonation`, `listMyContributions`; event `contribution.donation.recorded` | RES-12 | S5 |
| 12 | F-RES-11 | `approveCostOverrun` (LEADER); BR-32 chặn confirm khi vượt dự toán | RES-10 | S6 |
| 13 | F-RES-13b | Pledge: `listContributionNeeds`, `pledgeMaterialDonation`, `pledgeMonetarySupport`, `cancelDonationPledge`; pledge không tăng stock (BR-39) | RES-13 | S6 |
| 14 | F-RES-13c | `recordDonationReceipt`; BR-38 cộng dồn, PARTIALLY_RECEIVED → RECEIVED, nhận vượt → cảnh báo | RES-13b | S6 |
| 15 | F-RES-15 | `material_allocation(_item)`; `allocateMaterial`, `listMaterialAllocations`; BR-33; test 2 MO song song → 1 thành công, 1 `MATERIAL_INSUFFICIENT_AVAILABLE` | RES-04, task_projection | S6 |
| 16 | F-RES-16 | `createInventoryAdjustment`; damaged/lost/disposed không quay lại usable (BR-43) | RES-04 | S6 |
| 17 | F-RES-17 | `task_material_report(_item)`; submit (Volunteer), list, verify, revision (MO); BR-44..51 | RES-15, `task.completion.reviewed` | S7 |
| 18 | F-RES-18 | `borrowed_item`, `borrow_transaction`; loan offer (D-05) → accept/reject → issue/assign/return/incident; job nhắc hạn → `borrowed-item.return-due/overdue` | — | S7 |
| 19 | F-RES-20 | Consumer `support.material.approved` → TRANSFER_OUT; `POST /support-transfers/{id}/confirm` (MO nhận) → TRANSFER_IN; event `support.transfer.confirmed` | TSK-15b (Lâm) | S7 |
| 20 | F-RES-14 | `PATCH /donations/{id}` reconcile delta stock + contribution; event `contribution.donation.adjusted` | RES-13c | S8 (P2) |

### 4.3 Readiness — S6 → S8

| Thứ tự | Function | Việc chính | Phụ thuộc | Sprint |
|---|---|---|---|---|
| 0 | Setup | `Services/Readiness/*`, `readiness_db`, projection `work_area_scope` | PAR event | S6 |
| 1 | F-RDY-02 | `readiness_checklist`, `readiness_item` (`is_required`); `createReadinessCheck`, `replaceReadinessItems`, `getReadinessCheck`; chỉ sửa item khi DRAFT (D-04) | — | S6 |
| 2 | F-RDY-03 | `recordReadinessResult`, `attachReadinessEvidence`; failed → `readiness.item.failed` | NTF-01 | S7 |
| 3 | F-RDY-04 | `linkCorrectiveTask` → gọi `POST /internal/tasks` (typed HttpClient + Idempotency-Key) hoặc link task có sẵn; mandatory failed bắt buộc có corrective task | `/internal/tasks` (Lâm, hết S6) | S7 |
| 4 | F-RDY-05 | `recheckReadinessItem`, `confirmAreaReadiness` (BR-52 → 409), `reopenReadinessCheck` (BR-53 lý do); event `readiness.area.ready/reopened` | RDY-03/04 | S7 |
| 5 | F-RDY-06 | `getSeasonReadiness` | RDY-05 | S8 |

### 4.4 Reporting — S6 → S9

| Thứ tự | Function | Việc chính | Phụ thuộc | Sprint |
|---|---|---|---|---|
| 0 | Setup | `Services/Reporting/*`, `reporting_db`, inbox | — | S6 |
| 1 | F-RPT-01 | `contribution_type`, `recognition_rule` (version, `effective_from/to`); đổi điểm = version mới (BR-68) | — | S6 |
| 2 | F-RPT-02 | Consumer `task.completion.reviewed`, `attendance.hours.approved`, `purchase.delivery.confirmed`, `contribution.donation.recorded/adjusted`, `support.member.completed` → `contribution` + `recognition_point` (lưu rule version); unique theo eventId/reference (BR-54..56); event `recognition.entry.created` | Event từ Task/Resource | S7 |
| 3 | F-RPT-03 | `getMyRecognition`, `getSeasonRecognition` | RPT-02 | S7 |
| 4 | F-RPT-04 | Read-model dashboard (season, work area, material) cập nhật từ event; cache Redis; mục tiêu < 3s | Toàn bộ event | S8 |
| 5 | F-RPT-05 | 6 báo cáo (task, material, purchase, contribution, attendance, readiness); BR-57 ghi parish/season/thời gian/bộ lọc | RPT-04 | S8 |
| 6 | F-RPT-06 | `createReportExport` (202 + job), `getReportExport`; xlsx (ClosedXML) / pdf (QuestPDF) lưu MinIO | NTF-01 | S9 (P2) |

---

## 5. Lịch phụ thuộc giữa 2 người

| Hạn | Ai giao | Giao cái gì | Ai chờ | Nếu trễ thì |
|---|---|---|---|---|
| Hết S0 (18/10) | Lâm | docker-compose + BuildingBlocks.Web + template | Vũ | Vũ tạm dùng ProblemDetails mặc định, refactor sau |
| Giữa S1 (26/10) | Vũ | `login` phát JWT đúng claim | Lâm (Security, Gateway) | Lâm dùng JWT tự ký trong test |
| Hết S1 (01/11) | Lâm | Security + Messaging + Gateway | Vũ | Vũ chạy service trực tiếp không qua gateway |
| Hết S2 (15/11) | Lâm | `planning.work-area.*`, `planning.season.*` | Vũ (Resource) | Vũ seed `work_area_scope` bằng script |
| Hết S2 (15/11) | Vũ | `GET /internal/users` | Lâm (assign) | Lâm stub client trả user ACTIVE |
| Hết S3 (29/11) | Lâm | File upload + `/internal/files/{id}` | Cả hai | Cho phép fileId bỏ qua kiểm tra ở môi trường dev |
| Giữa S4 (06/12) | Vũ ↔ Lâm | `purchase.request.decided` + rule buyer = assignee task PURCHASE | Cả hai | — (phải chốt, chặn demo S4/S5) |
| Hết S4 (13/12) | Lâm | `task.materials.changed`, `task.completion.reviewed` | Vũ (allocation, report) | Vũ publish event giả bằng tool test |
| Hết S6 (10/01) | Lâm | `POST /internal/tasks` | Vũ (corrective task) | RDY-04 chỉ hỗ trợ link task có sẵn |
| Giữa S7 (18/01) | Lâm ↔ Vũ | `support.material.approved` / `support.transfer.confirmed` | Cả hai | — |

**Cách tránh chặn nhau:** event contract (record C# trong `.Contracts`) được merge **trước** phần implement, để bên consumer code song song với dữ liệu giả.

---

## 6. Kiểm thử và nghiệm thu

| Mức | Công cụ | Ai | Khi nào |
|---|---|---|---|
| Unit | xUnit + FluentAssertions | Mỗi người cho service mình | Trong từng PR |
| Integration | `WebApplicationFactory` + Testcontainers (PostgreSQL, RabbitMQ) | Mỗi người | Trong từng PR |
| Contract event | Test serialize/deserialize event giữa publisher và consumer | Người publish viết, người consume review | Khi đổi `.Contracts` |
| Concurrency | Test song song: tranh slot task (TSK-07), allocation (RES-15), transfer (RES-20) | Owner | S3, S6, S7 |
| E2E | Bộ `.http` / Postman collection chạy 16 kịch bản (Mục 8 spec) qua gateway | Lâm chủ trì, Vũ viết phần mình | S9 |
| Performance | k6: dashboard < 3s, list endpoint chính | Vũ (dashboard), Lâm (còn lại) | S9 |

Kịch bản nghiệm thu theo owner (Mục 8 spec): Lâm #1–4, #11, #16; Vũ #5–10, #12–14; cả hai #15.

---

## 7. Rủi ro và cách xử lý

| Rủi ro | Ảnh hưởng | Xử lý |
|---|---|---|
| Logic tồn kho sai (shortage, unallocated) | Chuỗi Resource sai dây chuyền | Viết `InventoryCalculator` + unit test bảng trước S3; mọi thay đổi stock chỉ qua ledger |
| Event payload lệch giữa 2 người | Consumer lỗi, DLQ đầy | `.Contracts` có `Version`, test contract, review chéo bắt buộc |
| Mất/trùng event | Điểm tính 2 lần, stock post 2 lần | Outbox + Inbox theo `eventId`; unique `(reference_type, reference_id)` |
| Race condition | Vượt capacity / vượt tồn kho | Optimistic concurrency (`version`/xmin) + lock row ở allocation/transfer |
| Trễ mốc phụ thuộc | Người kia bị chặn | Stub/fake theo cột "Nếu trễ" ở Mục 5; daily sync 15' |
| Khối lượng S6–S7 của Vũ dày (RES + RDY + RPT) | Trễ demo S7 | Ưu tiên P0/P1 Resource; RDY-06, RES-14, RPT-06 lùi S8–S9; Lâm hỗ trợ sau S7 |
| Tết + bảo vệ | Mất thời gian | Không lên function mới sau S8; S9 chỉ hardening |

---

## 8. Checklist tuần này (S0)

**Chung**
- [ ] Đọc lại BA v1.1, ERD v3.5, API Contract v1.2; comment chỗ chưa đồng ý.
- [ ] Tạo GitHub Project board: 1 function ID = 1 issue, label owner/priority/sprint.
- [ ] Thống nhất JWT claim + event envelope, ghi vào `BuildingBlocks/README.md`.

**Lâm**
- [ ] Merge `lam-dev-1`, xóa `weatherforecast`.
- [ ] Thêm `.Contracts` + `tests/` cho Parish; README tạo service mới.
- [ ] `docker-compose.yml` (PostgreSQL nhiều DB, RabbitMQ, Redis, MinIO).
- [ ] `BuildingBlocks.Web` v1.
- [ ] `getParish`, `updateParish`.

**Vũ**
- [ ] Tạo `Services/Identity` theo template Parish, thêm vào `GrottoWorks_BE.slnx`.
- [ ] Entity `AppUser`, `Role` + migration đầu, seed 5 role + ADMIN.
- [ ] Draft `login` phát JWT (để Lâm test Security từ đầu S1).
- [ ] Phác `InventoryCalculator` + bảng test case shortage từ BA 8.5 (chuẩn bị S3).

---

## 9. Theo dõi tiến độ

Đánh dấu ở đây hoặc trên board (board là nguồn chính). Ký hiệu: ⬜ chưa làm · 🟨 đang làm · ✅ xong.

| Service | Owner | P0 | P1 | P2 | Xong |
|---|---|---|---|---|---|
| Platform | Lâm | PLT-01..06 | PLT-07..09 | — | 0 / 9 |
| ParishCoordination | Lâm | PAR-01, 02, 05..08 | PAR-04, 09, 10 | — | 0 / 9 (PAR-01 một phần) |
| TaskExecution | Lâm | TSK-01..04, 06..11, 17 | TSK-05, 12..15, 15b | TSK-16 | 0 / 18 |
| NotificationAudit | Lâm | NTF-01 | NTF-02..04 | NTF-05, 06 | 0 / 6 |
| Identity | Vũ | IDN-01..03, 05..09 | IDN-04, 10 | — | 0 / 10 |
| Resource | Vũ | RES-01..07, 09, 10, 12, 13, 15, 17 | RES-11, 13b, 13c, 16, 18, 20 | RES-14 | 0 / 20 |
| Readiness | Vũ | — | RDY-02..06 | — | 0 / 5 |
| Reporting | Vũ | — | RPT-01..05 | RPT-06 | 0 / 6 |
