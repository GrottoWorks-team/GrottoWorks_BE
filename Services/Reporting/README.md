# Reporting Service

Contribution, điểm ghi nhận, dashboard, báo cáo và export (owner: Vũ — spec §5.4, tag `Reports`).
Trạng thái: F-RPT-01 — S6 (làm 27/10).

## Kiến trúc

```
Services/Reporting/
├── Reporting.Domain/          # Recognition (ContributionType, RecognitionRule), Scope (work_area_scope, task_projection)
├── Reporting.Contracts/       # Event contract-first (recognition.entry.created)
├── Reporting.Application/     # Handler thường (không MediatR)
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

## Contribution type & recognition rule (F-RPT-01, BA 13, BR-56, BR-68)

- Danh mục dùng chung toàn hệ thống (không theo parish). 6 type hệ thống được seed với id cố định vì consumer event map
  vào: `TASK_COMPLETION`, `VOLUNTEER_HOURS`, `PURCHASE_ASSISTANCE`, `MONETARY_SUPPORT`, `MATERIAL_DONATION`,
  `SUPPORT_ASSISTANCE`. Code `A–Z0–9_`, unique, không đổi. Type INACTIVE: không tạo rule mới, contribution vẫn được ghi
  (là sự kiện đã xảy ra) nhưng không phát sinh điểm mới.
- Rule = một dòng mỗi version, `(rule_name, rule_version)` unique. Không có endpoint sửa: đổi điểm = POST với
  `previousRuleId` → version + 1, cùng tên và type; version cũ kết thúc ngày trước `effectiveFrom` mới (không chồng
  khoảng). Chỉ version mới nhất, ACTIVE mới được tạo version tiếp; tạo song song → một 201, còn lại 409.
- Điểm 1–10000, không có giá trị tiền (BR-56). `deactivate` kết thúc version hôm nay (giờ Việt Nam); version chưa bắt
  đầu thì bị hủy. Rule chỉ áp dụng khi ACTIVE và ngày contribution nằm trong khoảng hiệu lực.

## Việc còn mở

- Lý do `deactivateRecognitionRule` chưa có cột lưu (ERD không có) — chờ activity log F-NTF-04.
- ERD cho phép `recognition_rule.contribution_type_id` null; API bắt buộc có type.
