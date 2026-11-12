# BE CHANGELOG — những gì đã sửa trong code BE

> Mục đích: mỗi lần AI/agent sửa code BE, ghi lại **ngay tại đây** để team BE biết
> đã đổi gì, ở service nào, có phá vỡ contract hay cần migration không.
> Ghi **mới nhất lên trên**. Không ghi việc chỉ sửa tài liệu (trừ khi tài liệu đó
> là contract).
>
> Quy ước cột: `Ngày · Branch · Commit` → `Service/File` → `Thay đổi` →
> `Ảnh hưởng client` → `Cần làm thêm`.

---

## 2026-11-12 (lần 7) · `phongnltse/task-team-leader-progress`

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
