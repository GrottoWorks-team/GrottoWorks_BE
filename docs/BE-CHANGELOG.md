# BE CHANGELOG — những gì đã sửa trong code BE

> Mục đích: mỗi lần AI/agent sửa code BE, ghi lại **ngay tại đây** để team BE biết
> đã đổi gì, ở service nào, có phá vỡ contract hay cần migration không.
> Ghi **mới nhất lên trên**. Không ghi việc chỉ sửa tài liệu (trừ khi tài liệu đó
> là contract).
>
> Quy ước cột: `Ngày · Branch · Commit` → `Service/File` → `Thay đổi` →
> `Ảnh hưởng client` → `Cần làm thêm`.

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
