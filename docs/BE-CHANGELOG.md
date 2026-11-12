# BE CHANGELOG — những gì đã sửa trong code BE

> Mục đích: mỗi lần AI/agent sửa code BE, ghi lại **ngay tại đây** để team BE biết
> đã đổi gì, ở service nào, có phá vỡ contract hay cần migration không.
> Ghi **mới nhất lên trên**. Không ghi việc chỉ sửa tài liệu (trừ khi tài liệu đó
> là contract).
>
> Quy ước cột: `Ngày · Branch · Commit` → `Service/File` → `Thay đổi` →
> `Ảnh hưởng client` → `Cần làm thêm`.

---

## 2026-11-12 (lần 10) · `phongnltse/task-team-leader-progress`

### 14. Huỷ việc đi mua không còn chặn việc tạo lại (PURCHASE_TASK_EXISTS / đơn kẹt BUYER_ASSIGNED)

- **Yêu cầu (12/11/2026)**: "việc đã huỷ rồi mà vào tạo việc mua mới thì lại báo là đã có việc cho đơn mua ấy rồi".
- **Tái hiện**: `POST /api/v1/purchase-requests/{id}/purchase-tasks` → `409 PURCHASE_TASK_EXISTS` dù
  task mua duy nhất của đơn đã `CANCELLED` (đơn vẫn `APPROVED`).
- **Nguyên nhân**: `SpecialTaskStore.FindPurchaseAsync` lọc **chỉ theo `purchase_request_id`**, không xét
  trạng thái task ⇒ task `CANCELLED` vẫn bị coi là "đã tồn tại".
- **Sửa**:
  - `TaskExecution.Infrastructure/Tasks/SpecialTaskStore.cs` — `FindPurchaseAsync` bỏ qua task
    `CANCELLED` (và `SingleOrDefault` → `FirstOrDefault`).
  - `TaskExecution.Application/Tasks/SpecialTaskService.cs` — chấp nhận đơn ở `APPROVED` **hoặc
    `BUYER_ASSIGNED`** (đơn bị kẹt ở `BUYER_ASSIGNED` sau khi huỷ task đã giao vẫn tạo lại được việc thay thế;
    chống trùng vẫn do `FindPurchaseAsync` bảo đảm). Đơn `PURCHASED/COMPLETED/…` vẫn bị chặn.
  - `Resource.Domain/Purchases/PurchaseRequest.ReleaseBuyer` (mới) + `Resource.Application/Purchases/PurchaseHandlers.cs`
    `PurchaseBuyerSync`: khi task mua chuyển `CANCELLED`, đơn `BUYER_ASSIGNED` **trả về `APPROVED`**
    (ghi state change với `Guid.Empty` = hệ thống). Đơn đã `PURCHASED`/`COMPLETED` giữ nguyên.
- **Ảnh hưởng client**: không đổi route/DTO. `GET /purchase-requests/{id}/history` có thêm bản ghi
  `BUYER_ASSIGNED → APPROVED` khi huỷ việc mua (giống thao tác hệ thống).
- **Không migration**, không đổi schema.
- **Test**: `TaskExecution.UnitTests` **378/378** (+`Buyer_assigned_purchase_can_get_a_replacement_task`);
  `Resource.UnitTests` **257/257** (+`Cancelling_the_purchase_task_returns_the_request_to_approved`,
  `Cancelling_a_purchase_task_after_a_recorded_purchase_keeps_the_request_status`).
- **Kiểm chứng thật**: sau khi rebuild, tạo lại việc mua cho đơn đã có task `CANCELLED` → `201`
  (task `Mua 10 bóng Bóng đèn LED 9W`, sau đó TNV nhận việc → đơn `BUYER_ASSIGNED`); tạo thêm task
  khi đang có task sống vẫn trả `409 PURCHASE_TASK_EXISTS` (đúng).

---

## 2026-11-12 (lần 9) · `phongnltse/task-team-leader-progress`

### 13. Runtime seeder của ParishCoordination không tự tạo lại "GrottoWorks Demo Season 2026"

- **Yêu cầu (12/11/2026)**: "seed lại data nhưng chỉ dùng lại ở mùa giáng sinh 2025".
- **Vấn đề**: `ParishCoordinationDataSeeder.SeedAsync` chạy mỗi lần service khởi động
  (`Database:MigrateOnStartup=true`, Development) và luôn đảm bảo có giáo xứ demo + cộng đoàn +
  mùa `GrottoWorks Demo Season 2026` + 1 khu vực. Sau khi nạp bộ seed `SeedingData`
  (chỉ còn mùa Giáng sinh 2025), seeder này sẽ **thêm lại mùa 2026** ⇒ demo có 2 mùa.
- **Sửa** (`ParishCoordination.Infrastructure/Data/ParishCoordinationDataSeeder.cs`):
  `SeedAsync` thoát sớm `if (await dbContext.Parishes.AnyAsync(ct))` + log
  `"ParishCoordination seed skipped: database already has parishes."`. Nghĩa là seeder chỉ
  chạy khi database **hoàn toàn trống** (dev lần đầu), không xen vào dữ liệu thật/seed.
- **Ảnh hưởng client**: không đổi API/contract. Dev đang có dữ liệu: mùa duy nhất do
  `SeedingData` quyết định. Dev reset DB trắng: hành vi cũ (tạo demo parish/season 2026).
- **Không migration**, không đổi schema.
- **Cần làm thêm**: sau khi `SeedingData` chạy `--reset`, phải restart **theo thứ tự**
  `parishcoordination.api` → (chờ `task_db.work_area_scope` = 8) → `taskexecution.api`, để
  `PlanningSnapshotReconciliationWorker` / `TaskSnapshotReconciliation` phát lại snapshot ⇒ các read model
  `season_scope`, `work_area_scope`, `task_projection` được dựng lại (nếu không, gọi task theo khu sẽ 404
  `WORK_AREA_NOT_FOUND`; nếu task service khởi động trước khi có scope, nó bỏ qua 10 task và chỉ thử lại
  sau 30 phút). Quy trình đầy đủ ghi ở `SeedingData/README.md` §3.1.

---

## 2026-11-12 (lần 8) · `phongnltse/task-team-leader-progress`

### 12. Quy trình sẵn sàng (§8.12): PARISH đặt tiêu chí & duyệt khu vực, LEADER nộp minh chứng

- **Yêu cầu (12/11/2026)**: "readiness do parish tạo cho từng khu vực, leader chỉ cung cấp minh chứng trước khi
  xác nhận đạt, cuối cùng parish duyệt tổng thể tiêu chí từng khu vực; khu đạt → mùa chuyển SẴN SÀNG".
  Hai điểm đã chốt với người dùng: (a) **LEADER tự đánh dấu Đạt/Không đạt** sau khi nộp minh chứng;
  (b) **LEADER vẫn được tạo phiếu nếu khu chưa có** (phương án dự phòng) — PARISH vẫn là người duyệt cuối.
- **Sửa quyền route** (`Readiness.Api/Endpoints/ChecklistEndpoints.cs`):
  - `createReadinessCheck`: `Leader` → **`Parish, Leader`**.
  - `replaceReadinessItems`: `Leader` → **`Parish, Leader`** (ban hành giáo đặt/sửa tiêu chí).
  - `confirmAreaReadiness`: `Leader` → **`Parish`** (duyệt tổng thể khu vực).
  - `reopenReadinessCheck`: `Parish, Leader` → **`Parish`**.
  - Giữ nguyên: `recordReadinessResult`/`recheck`/`linkCorrectiveTask` = LEADER,
    `attachReadinessEvidence` = LEADER/VOLUNTEER, các endpoint đọc = PARISH/LEADER/MO.
- **Sửa handler** (`Readiness.Application/Checklists/ChecklistHandlers.cs`):
  - `CreateReadinessCheckCommandHandler` + `ChecklistDefinitionHandler`: PARISH ghi theo phạm vi giáo xứ
    (`ForReadAsync` + `WorkAreaAccess.EnsureWritable`), LEADER vẫn qua `ForLeaderWriteAsync`.
  - `InspectionCommandHandler.InspectAsync`: **bắt buộc có ≥1 minh chứng trước khi đánh `PASSED`** →
    `422 READINESS_EVIDENCE_REQUIRED`; kiểm tra **trước khi ghi** nên mục bị từ chối vẫn ở `PENDING`.
  - `ReadinessDecisionHandler`: `confirm`/`reopen` chỉ PARISH (`403 READINESS_PARISH_APPROVAL_REQUIRED`
    ở tầng handler, route đã chặn trước).
- **Không đổi**: domain (trạng thái mục/checklist), event `readiness.area.ready` → ParishCoordination đặt khu
  `READY`; khi **mọi khu READY** thì `Season.ReconcileReadiness` tự chuyển mùa sang `READY` (đã có unit test
  `SeasonReadinessAggregationTests`), không cần sửa thêm.
- **Test**: `Readiness.UnitTests` **54/54** (thêm `Leader_cannot_give_the_final_area_approval`,
  `Passing_an_item_requires_evidence_first`; `Confirm_publishes_area_ready_and_parish_can_reopen` →
  `Parish_confirms_area_ready_and_can_reopen`). `Platform.UnitTests` 5/5.
- **Kiểm chứng thật qua gateway** (checklist *Cổng chào và biển báo*, khu `READINESS_REVIEW`):
  LEADER `reopen` **403** · PARISH `reopen` **200 REOPENED** · LEADER (đúng khu) `PASSED` không minh chứng
  **422 `READINESS_EVIDENCE_REQUIRED`** · LEADER `confirm` **403** · PARISH `confirm` **200 READY**
  (`completedBy` = user ban hành giáo) · PARISH tạo phiếu cho *Đèn LED khuôn viên* **201** (2 tiêu chí),
  LEADER khu đó đọc được tiêu chí và bị chặn đúng như trên. Dữ liệu test đã xoá.
- **Ảnh hưởng client**: FE đã đổi — `/community/checklist` (ban hành giáo) có nút **"Duyệt khu vực sẵn sàng"**
  + gợi ý quy trình; `/leader/checklist` bỏ nút xác nhận, nút **Đạt bị khoá** khi mục chưa có minh chứng.

---

## 2026-11-12 (lần 7) · `phongnltse/task-team-leader-progress`

### 9b. Quyền mùa Giáng sinh: **chỉ ban hành giáo (PARISH) được ghi**, ADMIN chỉ đọc

- **Yêu cầu (12/11/2026)**: "chỉ có parish mới được tạo và tương tác, còn admin chỉ có quyền xem".
  Đồng bộ với quy tắc đã áp cho khu vực công tác (11/11/2026).
- **Sửa** (`ParishCoordination.Api/Endpoints/SeasonEndpoints.cs`): 6 endpoint ghi đổi từ
  `RequireRole(Admin, Parish)` → **`RequireRole(Parish)`**:
  `POST /seasons` · `POST /seasons/{id}/clone` · `PATCH /seasons/{id}` ·
  `POST /seasons/{id}/activate` · `POST /seasons/{id}/complete` · `POST /seasons/{id}/archive`.
  `GET /seasons` và `GET /seasons/{id}` **giữ nguyên** (mọi user đã đăng nhập) → ADMIN vẫn xem được.
- **Không đổi payload**; handler giữ nguyên logic (route policy là chốt chặn).
- **Test**: `ParishCoordination.UnitTests` **313/313** — `RouteAuthorizationTests` cập nhật kỳ vọng
  role của 6 route trên thành `PARISH` đơn lẻ.
- **Kiểm chứng thật qua gateway**: `hoa.nguyen` (ADMIN) → `POST /seasons` **403**, `POST …/clone` **403**,
  `GET /seasons` **200**; `bao.tran` (PARISH) → cả hai **201** (dữ liệu probe đã xoá).
- **Ảnh hưởng client**: FE đã đổi theo — ADMIN chỉ thấy tab "Mùa Giáng sinh" **read-only** trong hub
  "Giáo xứ & cộng đoàn" (`/parish/communities?tab=seasons`); ban hành giáo vẫn dùng
  `/community/seasons` với đầy đủ thao tác.

### 10. `POST /api/v1/seasons/{sourceSeasonId}/clone` — tạo mùa mới từ mùa cũ (1 request, 1 transaction)

- **Vấn đề**: "Tạo mùa mới từ mùa cũ" nếu làm ở client sẽ là `1 × POST /seasons` +
  `N × POST /seasons/{id}/work-areas` + `2N × PUT .../leader|material-officer` = **22 request** với 7 khu;
  lỗi giữa đường để lại mùa thiếu khu vực/thiếu người phụ trách nên không `activate` được.
- **Thêm** (ParishCoordination, không phá vỡ contract cũ):
  - `Application/Seasons/Commands/CloneSeason/*` — `CloneSeasonCommandHandler`: đọc mùa nguồn → `ParishAccess`
    → validate ngày (`SEASON_DATE_INVALID` 422) + unique `name`/`seasonYear` (409) → `Season.Create` (PLANNING)
    → copy các khu vực được chọn (`areas` rỗng = tất cả), `dateShift` `NONE` (bỏ trống ngày) hoặc
    `SAME_DAY_OF_SEASON` (giữ offset, clamp trong mùa mới) → **một** `SaveChangesAsync` cho season + N areas + outbox.
  - `Users/IIdentityUserLookup.cs` — thêm `IIdentityUserBatchLookup.GetByIdsAsync` (`GET /internal/users?ids=`),
    `Infrastructure/Identity/IdentityUserLookupClient` implement batch (chunk 100 id). Phân công chỉ được copy khi
    user **ACTIVE + đúng role + cùng giáo xứ**; ngược lại bỏ + trả `warnings[]`
    (`WORK_AREA_LEADER_NOT_VERIFIED:{khu}`, `WORK_AREA_MATERIAL_OFFICER_NOT_VERIFIED:{khu}`).
  - `WorkAreas/IWorkAreaRepository.ListBySeasonIdAsync` + EF implementation.
  - `Api/Requests/CloneSeasonRequest.cs`, `Api/Endpoints/SeasonEndpoints.cs` (`cloneSeason`, ADMIN|PARISH),
    DI `CloneSeasonCommandHandler`. Gateway đã phủ bởi route catch-all `parish-seasons-route`.
  - Response `201 ApiResponse<CloneSeasonResultDto>{season, areas[], warnings[]}` — client **không** cần GET lại.
- **Lỗi có mã**: `404 SOURCE_SEASON_NOT_FOUND` · `409 SEASON_NAME_CONFLICT|SEASON_YEAR_CONFLICT` ·
  `422 SEASON_DATE_INVALID|CLONE_AREA_NOT_IN_SOURCE|CLONE_AREA_DUPLICATE|CLONE_SOURCE_TOO_LARGE` ·
  `403 PARISH_ACCESS_DENIED|SEASON_CLONE_FORBIDDEN`.
- **Test**: `ParishCoordination.UnitTests` **313/313** (13 test mới `CloneSeasonHandlerTests`, route auth cập nhật).
  Kiểm chứng thật qua gateway: clone mùa 2026 (8 khu) → `201`, mùa mới PLANNING, 8 khu có ngày dịch đúng,
  phân công copy, `warnings` rỗng; dữ liệu test đã xoá.

### 11. Chi tiết vật tư: `specification`, `supplierName`, `referenceUnitPrice`, `minimumStock`

- **Vấn đề**: `material` chỉ có code/name/unit/description → trưởng ban vật tư không ghi được thông số,
  nhà cung cấp quen, đơn giá tham khảo hay mức tồn tối thiểu để lập dự toán mua.
- **Thêm** (Resource):
  - `Domain/Catalog/Material.cs` — 4 field mới + validate (`MATERIAL_SPECIFICATION_INVALID` ≤300 ký tự,
    `MATERIAL_SUPPLIER_INVALID` ≤150, `MATERIAL_REFERENCE_PRICE_INVALID` / `MATERIAL_MINIMUM_STOCK_INVALID` ≥ 0).
    `Create(...)`/`Update(...)` nhận thêm tham số optional → **không phá vỡ** call site cũ.
  - `Application/Catalog/CatalogDtos.cs` (`MaterialDto` +4 field), `CatalogHandlers.cs` (command + truyền qua).
  - `Api/Requests/CatalogRequests.cs` + `Api/Endpoints/CatalogEndpoints.cs` — nhận/trả thêm 4 field cho
    `POST /api/v1/materials` và `PATCH /api/v1/materials/{id}` (PATCH: `null` = giữ nguyên, chuỗi rỗng = xoá).
  - `Infrastructure/Data/Configurations/CatalogConfigurations.cs` + migration `20261112130506_AddMaterialDetails`
    (`material_specification` varchar(300), `material_supplier_name` varchar(150),
    `material_reference_unit_price` numeric(18,2), `material_minimum_stock` numeric(18,3) — đều nullable).
- **Ảnh hưởng client**: payload mở rộng, client cũ vẫn chạy (field optional). FE đã thêm ô nhập + cột bảng + CSV.
- **Test**: `Resource.UnitTests` **255/255** (3 test mới). Kiểm chứng thật: `POST` 201 giữ đủ 4 field,
  `PATCH` cập nhật từng phần, giá âm → `422`, list trả đúng; dữ liệu test đã xoá.

---

## 2026-11-11 (lần 6) · `phongnltse/task-team-leader-progress`

### 9. Cho `MO` đọc 3 báo cáo cấp giáo xứ (§11 / BR-57)

- **Vấn đề**: MO (`anh.pham`) bị `403` ở `GET /dashboards/season`, `GET /seasons/{id}/recognition`,
  `GET /seasons/{id}/support-summary` — trưởng ban vật tư không xem được số liệu mùa / hỗ trợ chéo để
  lập kế hoạch vật tư, dù vẫn đọc được `GET /work-areas/{id}/shortages`.
- **Sửa** (mở rộng quyền đọc, không đổi payload):
  - `Reporting.Api/Endpoints/ReportEndpoints.cs` — `viewSeasonDashboard`: `RequireRole(PARISH, MO)`.
  - `Reporting.Api/Endpoints/RecognitionEndpoints.cs` — `getSeasonRecognition`: `RequireRole(PARISH, MO)`.
  - `TaskExecution.Api/Endpoints/SupportSummaryEndpoints.cs` — `getCrossCommunitySupportSummary`: `RequireRole(PARISH, MO)`.
  - `TaskExecution.Application/Support/SupportSummary.cs` — `GetSupportSummaryHandler` chấp nhận role `MO`
    (giữ nguyên kiểm tra `user.ParishId == season.ParishId` nên vẫn **không** đọc chéo giáo xứ;
    `VOLUNTEER`/`LEADER` vẫn `403`).
- **Test**: `TaskExecution.UnitTests` **377/377** (cập nhật `SupportResponseRouteTests` cho role của
  `getCrossCommunitySupportSummary`), `Reporting.UnitTests` **71/71**. Kiểm chứng thật: MO → `200` cả 3
  endpoint; VOLUNTEER → `403`.
- **Ảnh hưởng client**: FE không cần đổi; các trang dashboard/báo cáo của MO hết 403.

---

## 2026-11-11 (lần 5) · `phongnltse/task-team-leader-progress`

### 8. Thêm `GET /api/v1/support-response-materials/{itemId}/support-transfer` — sửa lỗi 412 khi nhận vật tư hỗ trợ

- **Vấn đề**: `POST /api/v1/support-response-materials/{itemId}/confirm-receipt` kiểm tra `Version` của
  **`support_transfer`** (Resource, token `xmin` — ví dụ `2088`), nhưng FE chỉ có `version` của
  `support_response_material` bên TaskExecution (ví dụ `3`). Kết quả: MO bấm "Xác nhận đã nhận" luôn nhận
  `412 SUPPORT_TRANSFER_VERSION_MISMATCH`, luồng BR-64 không thể hoàn tất từ UI (đã kiểm chứng: gửi đúng
  `xmin` → `200 RECEIVED`).
- **Sửa** (additive, không đổi contract cũ):
  - `Resource.Application/Inventory/SupportTransferHandlers.cs` — thêm `GetSupportTransferQueryHandler`
    (đọc phiếu chuyển theo `support_response_material_id`, scope theo MO của work area **đích**;
    không tồn tại → `404 SUPPORT_TRANSFER_NOT_FOUND`).
  - `Resource.Application/DependencyInjection.cs` — đăng ký handler (**bắt buộc**, thiếu đăng ký thì
    minimal API ném lỗi `RequestDelegateFactory` lúc build endpoint và service không khởi động).
  - `Resource.Api/Endpoints/InventoryEndpoints.cs` — `GET /api/v1/support-response-materials/{itemId:guid}/support-transfer`
    (`getSupportMaterialTransfer`, role `MATERIAL_OFFICER`, trả `SupportTransferDto` có `version`).
  - `ApiGateway/YarpApiGateway/appsettings.json` — route `resource-support-material-transfer-route`
    (Path `/api/v1/support-response-materials/{itemId}/support-transfer`, GET, Order -1) → cluster `resource`.
- **Ảnh hưởng client**: FE `MaterialReceiptsPage` gọi endpoint này để lấy `version` thật rồi mới
  `confirm-receipt`; `confirm-receipt` không đổi payload.
- **Cần làm thêm**: nếu service khác cũng cần version phiếu chuyển, dùng `GET /internal/support-transfers?ids=`
  (đã có sẵn, API-key) thay vì thêm endpoint public.

---

## 2026-11-11 (lần 4) · `phongnltse/task-team-leader-progress`

### 7. Cho phép tạo task theo loại (`taskType`) để hoàn tất BR-66

- **Vấn đề**: `CreateTaskCommand`/`WorkTask.CreateOpen` hard-code `TaskType.Work` ⇒ không có cách nào
  sinh task `CORRECTIVE`, trong khi readiness yêu cầu “Link an open CORRECTIVE task of this work area”
  ⇒ mục bắt buộc fail **không thể** hoàn tất quy trình khắc phục.
- **Files**
  - `TaskExecution.Domain/Tasks/WorkTask.cs` — `CreateOpen(..., TaskType type = TaskType.Work)`;
    bỏ hard-code `Type = TaskType.Work`.
  - `TaskExecution.Application/Tasks/CreateTaskCommand.cs` — thêm `TaskType Type = TaskType.Work` (cuối record,
    nên mọi caller cũ vẫn biên dịch) + truyền vào `CreateOpen`.
  - `TaskExecution.Api/Requests/CreateTaskRequest.cs` — thêm `TaskType? TaskType = null`.
  - `TaskExecution.Api/Endpoints/TaskEndpoints.cs` — `request.TaskType ?? TaskType.Work`.
- **Contract**: `POST /api/v1/work-areas/{id}/tasks` nhận thêm field optional `taskType`
  (`WORK` | `PURCHASE` | `CORRECTIVE`) — **additive**, client cũ không gửi thì mặc định `WORK`.
- **Test**: `TaskExecution.UnitTests` **377/377 pass**; kiểm chứng thật:
  tạo task `CORRECTIVE` → `201` (`taskType: CORRECTIVE`), gắn vào readiness item bằng
  `PUT /readiness-checks/{id}/items/{itemId}/corrective-task` → `200`, `correctiveTaskId` được set.
- **Lưu ý**: endpoint gắn corrective dùng **PUT**, không phải POST (POST trả `405`).

---

## 2026-11-11 (lần 3) · `phongnltse/task-team-leader-progress`

### 6. Rule thông báo cho `attendance.session.closed-by-leader`

- **Service**: `NotificationAudit`
- **Files**
  - `NotificationAudit.Application/Notifications/NotificationRuleCatalog.cs` — thêm rule
    `71000000-0000-0000-0000-000000000034` = `attendance.session.closed-by-leader`
    (ID mới, không sửa ID cũ — ID là contract của màn admin).
  - `NotificationAudit.Application/Events/IntegrationEventIngestion.cs` — map event →
    **1 notification cho TNV**: title “Attendance session closed”, body nêu số giờ
    được ghi + lý do trưởng khu nhập, `referenceType = TASK`, `referenceId = taskId`.
    Thiếu `volunteerUserId` → `REQUIRES_CONTEXT` (không tạo notification rác).
- **Seeding**: `NotificationRuleSeeder` tự thêm rule mới khi service khởi động (đã xác nhận
  `notification_rule` có row 34, `in_app_enabled = true`).
- **Test**: `tests/NotificationAudit.UnitTests/TaskEventIngestionTests.cs` thêm
  `Session_closed_by_leader_notifies_the_volunteer_with_hours_and_reason`.
  Kết quả: 154 pass, **1 fail có sẵn từ trước** (`ObjectStorageTests.R2_upload_url_is_path_style_https_and_signs_the_content_type`
  — test dùng mốc thời gian cứng `2026-11-06` trong khi presign dùng đồng hồ thật; không liên quan thay đổi này).
- **Cần làm thêm**: chưa có push server (chưa lưu Expo push token) — client mobile hiện
  tự poll `/notifications` và phát notification cục bộ ra khay hệ điều hành.

---

## 2026-11-11 (lần 2) · `phongnltse/task-team-leader-progress`

### 4. LEADER đóng phiên điểm danh hộ TNV (mới)

- **Service**: `TaskExecution` + `ApiGateway`
- **Files**
  - `TaskExecution.Application/Attendance/AttendanceSessionClose.cs` (**mới**) —
    `AttendanceService.CloseSessionAsync(attendanceId, reason, version)`: kiểm tra
    quyền trưởng khu của khu chứa task (`GetWorkAreaForLeaderWriteAsync`), `reason`
    bắt buộc (`CLOSE_REASON_REQUIRED`), đóng phiên bằng giờ server, tính giờ công
    như check-out bình thường.
  - `TaskExecution.Api/Endpoints/AttendanceReviewEndpoints.cs` — route mới
    `POST /api/v1/attendance/{attendanceId}/close` (tên endpoint
    `closeAttendanceSession`, role **LEADER**, `If-Match`/`version`,
    body `{ version, reason }`).
  - `TaskExecution.Contracts/Events/TaskIntegrationEvent.cs` — event mới
    `attendance.session.closed-by-leader` (payload có `reason`, `closedByUserId`,
    giờ vào/ra, `approvedMinutes`). **Chưa map notification** — NotificationAudit
    chỉ bỏ qua event chưa có rule (an toàn); cần thêm rule nếu muốn báo TNV.
  - `ApiGateway/YarpApiGateway/appsettings.json` — thêm route
    `task-attendance-close-route` (thiếu route này gateway trả **404** dù service đã có endpoint).
- **Contract**: endpoint mới — **additive**, không ảnh hưởng client cũ.
- **Test**: `tests/.../Application/AttendanceSessionCloseTests.cs` (3 case: đóng
  thành công + có event, thiếu lý do, đóng 2 lần) và `AttendanceRouteTests` thêm
  `closeAttendanceSession` vào danh sách route phải có. Tổng **377/377 pass**.
- **Cần làm thêm**: nếu muốn TNV nhận thông báo khi bị đóng hộ → thêm rule
  notification cho `attendance.session.closed-by-leader`.

### 5. Sửa dữ liệu seed chấm công (SeedingData — không thuộc git repo nào)

- **Vấn đề**: đơn `cf20fa92…` (PENDING) nằm cùng assignment với phiên `09e78e07…`
  (OPEN) ⇒ bấm **Duyệt** luôn `409 ATTENDANCE_OVERLAP`; và task của assignment đó
  đang `REVISION_REQUIRED` ⇒ `409 TASK_INVALID_STATE`.
- **Đã sửa** trong `SeedingData/data/04-task-execution/`
  - `04-attendance-request.json`: đơn PENDING chuyển sang assignment
    `c86cf0d5…` (task `e839cccb…` đang `IN_PROGRESS`, chưa có phiên), giờ xin
    `2026-11-11T07:28+07:00`; thêm đơn APPROVED `3f0a5b11…` cho assignment
    `c89de5d0…`.
  - `05-attendance.json`: phiên OPEN `09e78e07…` chuyển sang assignment
    `c89de5d0…`, `check_in_at = 2026-11-11T05:30+07:00`.
- **Áp dụng vào DB**: seeder dùng `ON CONFLICT DO NOTHING` nên chạy lại không ghi
  đè — đã `UPDATE` trực tiếp `task_db` bằng psql cho khớp file JSON.

---

## 2026-11-11 · `phongnltse/task-team-leader-progress` · `4d8167c`

### 1. Đơn xin check-in trả kèm "đơn của ai, nhiệm vụ nào"

- **Service**: `TaskExecution`
- **Files**
  - `Services/TaskExecution/TaskExecution.Application/Attendance/AttendanceService.cs`
    — `AttendanceRequestDto` thêm 3 field **optional ở cuối**:
    `volunteerId`, `taskId`, `taskName` + factory `WithContext(...)`.
  - `Services/TaskExecution/TaskExecution.Application/Attendance/IAttendanceStore.cs`
    — thêm record `AttendanceRequestTarget(TaskId, TaskName)` và method
    `RequestTargetsAsync(assignmentIds)` (tra batch, tránh N+1).
  - `Services/TaskExecution/TaskExecution.Infrastructure/Attendance/AttendanceRequestQueries.cs`
    — implement `RequestTargetsAsync` (join `task_assignment` × `task`).
  - `Services/TaskExecution/TaskExecution.Application/Attendance/AttendanceRequestReview.cs`
    — `RequestsAsync` (leader) và `MyRequestsAsync` (TNV) enrich qua `WithContextAsync`.
- **Contract**: `GET /api/v1/work-areas/{id}/attendance-requests` và
  `GET /api/v1/users/me/attendance-requests` trả thêm 3 field — **additive, không
  breaking**; mobile cũ vẫn chạy (bỏ qua field lạ).
- **Ảnh hưởng**: web hiện cột "Tình nguyện viên" + "Nhiệm vụ" thay vì chỉ giờ.
- **Cần làm thêm**: không có migration (chỉ join). `volunteerName` **không** trả
  từ BE — web tự map `volunteerId → tên` bằng `GET /users`.

### 2. `GET /attendance/open-sessions` lọc theo khu + trả ngữ cảnh

- **Service**: `TaskExecution`
- **Files**
  - `TaskExecution.Api/Endpoints/LongOpenAttendanceEndpoints.cs` — thêm query
    param **`workAreaId`** (optional).
  - `TaskExecution.Application/Attendance/LongOpenAttendance.cs` — `HandleAsync(hours, workAreaId)`;
    DTO trả kèm `volunteerId`/`taskId`/`taskName` (inject optional
    `IAssignmentStore` + `ITaskRepository`, không đổi DI đăng ký).
  - `TaskExecution.Infrastructure/Attendance/LongOpenAttendanceStore.cs` —
    `ListAsync(..., workAreaId)` thêm điều kiện `task.WorkAreaId == workAreaId`.
  - `TaskExecution.Application/Attendance/AttendanceService.cs` —
    `AttendanceDto` thêm 3 field optional cuối (`volunteerId`, `taskId`, `taskName`)
    + `WithContext(...)`. Áp dụng cho cả `GET /users/me/attendance` (không đổi hành vi).
- **Contract**: query mới `?workAreaId=<guid>` — **additive**; response thêm field.
- **Ảnh hưởng**: card "Phiên điểm danh mở quá lâu" ở web giờ theo khu đang chọn và
  nói rõ phiên của ai / nhiệm vụ nào.
- **Cần làm thêm**: chưa có endpoint cho LEADER đóng phiên hộ (hiện chỉ
  `VOLUNTEER` được `POST /task-assignments/{id}/attendance/check-out`) — nếu cần thì
  phải thêm endpoint + audit + lý do.

### 3. Test

- `tests/TaskExecution.UnitTests/Application/AttendanceTestStore.cs` — thêm
  `RequestTargetsAsync` (trả rỗng).
- `tests/TaskExecution.UnitTests/Application/LongOpenAttendanceTests.cs` — cập nhật
  `AlertStore.ListAsync` + test mới `Work_area_filter_is_forwarded_to_the_store`.
- Kết quả: `dotnet test tests/TaskExecution.UnitTests` → **374/374 pass** (SDK 10
  container). Không chạy migration mới.

---

## Trước đó (đã có trên branch, ghi lại cho đủ)

- **2026-11-10/11** · `phongnltse/task-team-leader-progress` ·
  `TaskExecution.Application/Tasks/TaskReviewService.AuthorizeVolunteerAsync`:
  chặn TNV không phải trưởng nhóm gửi tiến độ/nộp hoàn thành
  (`TASK_TEAM_LEADER_ONLY`, `TASK_ASSIGNMENT_REQUIRED`); thêm
  `PUT/DELETE /tasks/{taskId}/team-leader`. Chi tiết: `docs/BE-TODO-demo-2026-11-10.md`.
