# Reporting Service

Contribution, điểm ghi nhận, dashboard, báo cáo và export (owner: Vũ — spec §5.4, tag `Reports`).
Trạng thái: F-RPT-01 — S6; F-RPT-02, 03 — S7 (làm 27/10).

## Kiến trúc

```
Services/Reporting/
├── Reporting.Domain/          # Recognition (ContributionType, RecognitionRule, Contribution, RecognitionPoint), Inbox, Scope
├── Reporting.Contracts/       # Event contract-first (recognition.entry.created)
├── Reporting.Application/     # Handler thường (không MediatR); Ingestion (IntegrationEventHandler, payload event của Task)
├── Reporting.Infrastructure/  # EF Core + Npgsql (reporting_db), store, migration (seed contribution type hệ thống)
└── Reporting.Api/             # Minimal API + BuildingBlocks.Web/Security
tests/Reporting.UnitTests/     # xUnit + FluentAssertions, fake store (không cần DB)
```

Cùng khuôn với Resource / Readiness: `DomainRuleException(code, kind)` → 422/409/404/403/400/412, enum
`SNAKE_CASE_UPPER`, optimistic concurrency bằng `xmin`.

## Chạy local

`dotnet run --project Services/Reporting/Reporting.Api` (port 5186) với `ConnectionStrings:ReportingDatabase`,
`Jwt:SigningKey`, `InternalApi:ApiKey`; migrate tự động lúc startup. Docker: `reporting.api` port 6106.
Mẫu request: `Reporting.Api.http`.

## Endpoint

| Endpoint | operationId | Quyền | Function |
|---|---|---|---|
| `GET /api/v1/contribution-types` | `listContributionTypes` | ADMIN, PARISH (`status`) | F-RPT-01 |
| `POST /api/v1/contribution-types` | `createContributionType` | ADMIN | F-RPT-01 |
| `PATCH /api/v1/contribution-types/{id}` | `updateContributionType` | ADMIN (tên, mô tả, status; code cố định) | F-RPT-01 |
| `GET /api/v1/recognition-rules` | `listRecognitionRules` | ADMIN, PARISH (`status`, `contributionTypeId`) | F-RPT-01 |
| `POST /api/v1/recognition-rules` | `createRecognitionRule` | ADMIN (rule mới hoặc version mới qua `previousRuleId`) | F-RPT-01 |
| `POST /api/v1/recognition-rules/{id}/deactivate` | `deactivateRecognitionRule` | ADMIN (lý do bắt buộc) | F-RPT-01 |
| `GET /api/v1/users/me/recognition` | `getMyRecognition` | VOLUNTEER (`seasonId` tùy chọn) | F-RPT-03 |
| `GET /api/v1/seasons/{id}/recognition` | `getSeasonRecognition` | PARISH cùng parish | F-RPT-03 |
| `POST /internal/events` | — | `X-Internal-Api-Key` (feed event tạm, BuildingBlocks README §5.5) | F-RPT-02 |

## Contribution type & recognition rule (F-RPT-01, BA 13, BR-56, BR-68)

- Danh mục dùng chung toàn hệ thống (không theo parish). 6 type hệ thống được seed với id cố định vì consumer event map
  vào: `TASK_COMPLETION`, `VOLUNTEER_HOURS`, `PURCHASE_ASSISTANCE`, `MONETARY_SUPPORT`, `MATERIAL_DONATION`,
  `SUPPORT_ASSISTANCE`. Code `A–Z0–9_`, unique, không đổi. Type INACTIVE: không tạo rule mới, contribution vẫn được ghi
  (là sự kiện đã xảy ra) nhưng không phát sinh điểm mới.
- Rule = một dòng mỗi version, `(rule_name, rule_version)` unique. Không có endpoint sửa: đổi điểm = POST với
  `previousRuleId` → version + 1, cùng tên và type; version cũ kết thúc ngày trước `effectiveFrom` mới (không chồng
  khoảng). Chỉ version mới nhất, ACTIVE mới được tạo version tiếp; tạo song song → một 201, còn lại 409.
- Một type chỉ có một rule hiệu lực tại mỗi ngày (rule mới chồng khoảng với rule ACTIVE khác của type → 409
  `RECOGNITION_RULE_OVERLAP`), nên mỗi contribution nhận điểm của đúng một rule — muốn đổi điểm thì tạo version.
- Điểm 1–10000, không có giá trị tiền (BR-56). `deactivate` kết thúc version hôm nay (giờ Việt Nam); version chưa bắt
  đầu thì bị hủy. Rule chỉ áp dụng khi ACTIVE và ngày contribution nằm trong khoảng hiệu lực.

## Contribution & điểm (F-RPT-02, BR-54..56, BR-68)

| Event | Contribution type | Reference / người được ghi nhận |
|---|---|---|
| `task.completion.reviewed` (APPROVED, không phải PURCHASE) | `TASK_COMPLETION` | `TASK` taskId, mỗi volunteer một dòng |
| `attendance.hours.approved` | `VOLUNTEER_HOURS` | `ATTENDANCE` attendanceId, volunteer |
| `purchase.delivery.confirmed` | `PURCHASE_ASSISTANCE` | `PURCHASE` purchaseId, buyer |
| `contribution.receipt.confirmed` | `MONETARY_SUPPORT` / `MATERIAL_DONATION` | `donation_id` (một dòng mỗi donation), điểm cho `donorUserId` |
| `contribution.donation.adjusted` | (giữ type của donation) | cập nhật mô tả, không thêm điểm |
| `support.member.completed` | `SUPPORT_ASSISTANCE` | `SUPPORT_RESPONSE_MEMBER` id, volunteer |

- Inbox `processed_event` cùng transaction với contribution/điểm: gửi lại cùng `eventId` → DUPLICATE. Contribution unique
  theo donation, hoặc theo (reference, user) → cùng nghiệp vụ gửi bằng event khác cũng không tính lại (BR-55).
- Điểm: rule ACTIVE của type ACTIVE có hiệu lực vào ngày contribution (giờ Việt Nam) → `recognition_point` lưu đúng rule
  version (BR-68), unique (contribution, rule, user); event `recognition.entry.created` cho mỗi điểm. Donation chỉ tính một
  lần dù nhận nhiều đợt; donor ngoài (không account) có contribution nhưng không có điểm — nếu sau đó donor được liên kết
  account, receipt kế tiếp cấp điểm còn thiếu.
- Cột kỹ thuật ngoài ERD: `contribution.parish_id` (phân quyền parish, BR-57/67), `work_area_id`, `donor_user_id`.

## Recognition (F-RPT-03)

- `getMyRecognition`: contribution được ghi nhận cho chính mình (làm việc hoặc donation của donor liên kết account) trong
  parish, tổng điểm, theo type, từng dòng điểm kèm rule + version.
- `getSeasonRecognition`: xếp hạng account theo điểm (bằng điểm cùng hạng 1, 1, 3), số contribution, số contribution của
  donor ngoài; season ngoài parish → 404. Chưa trả tên người dùng (FE lấy từ Identity).

## Việc còn mở

- Lý do `deactivateRecognitionRule` chưa có cột lưu (ERD không có) — chờ activity log F-NTF-04.
- ERD cho phép `recognition_rule.contribution_type_id` null; API bắt buộc có type.
- Payload `task.completion.reviewed`, `attendance.hours.approved`, `support.member.completed` là đề nghị (BuildingBlocks
  README §5.5) — chờ Lâm chốt. Consumer MassTransit chờ outbox F-PLT-05; hiện nhận qua `/internal/events`.
- BA "mỗi 5 giờ làm = 20 điểm": rule hiện tính theo contribution (mỗi lần duyệt giờ), chưa có điểm theo số giờ.
