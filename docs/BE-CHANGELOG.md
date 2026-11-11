# BE CHANGELOG — những gì đã sửa trong code BE

> Mục đích: mỗi lần AI/agent sửa code BE, ghi lại **ngay tại đây** để team BE biết
> đã đổi gì, ở service nào, có phá vỡ contract hay cần migration không.
> Ghi **mới nhất lên trên**. Không ghi việc chỉ sửa tài liệu (trừ khi tài liệu đó
> là contract).
>
> Quy ước cột: `Ngày · Branch · Commit` → `Service/File` → `Thay đổi` →
> `Ảnh hưởng client` → `Cần làm thêm`.

---

## 2026-11-11 · `phongnltse/task-team-leader-progress` · (commit của PR "thêm ngữ cảnh đơn chấm công")

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
