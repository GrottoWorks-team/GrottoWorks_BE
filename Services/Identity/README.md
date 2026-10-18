# Identity Service

Dịch vụ xác thực, hồ sơ và quản trị người dùng (F-IDN-01 → F-IDN-10, xem `GrottoWorks_BE_Task_Assignment_Spec_v1.md`).

## Kiến trúc

Clean Architecture 4 lớp + project contract — copy cấu trúc từ `ParishCoordination`:

```
Services/Identity/
├── Identity.Domain/          # Entities + invariants (DomainException, BR-67, normalize email, policy)
├── Identity.Contracts/       # Integration events (contract-first): identity.user.registered/locked/unlocked
├── Identity.Application/     # Handlers (plain classes, no MediatR), abstractions, DTOs
├── Identity.Infrastructure/  # EF Core + Npgsql, JWT, PasswordHasher, seeder, stores
└── Identity.Api/             # Minimal API endpoints, middleware, Program.cs
tests/Identity.UnitTests/     # xUnit + FluentAssertions (chạy local, không cần DB/Docker)
```

## Chạy local

1. Cài .NET 10 SDK, PostgreSQL.
2. Set user-secrets (key/hash **không commit**):
   ```sh
   dotnet user-secrets set "Jwt:SigningKey" "<dev-key >= 32 ký tự>" --project Services/Identity/Identity.Api/Identity.Api.csproj
   dotnet user-secrets set "Seed:AdminPassword" "<mật khẩu admin dev>" --project Services/Identity/Identity.Api/Identity.Api.csproj
   ```
3. Tạo DB `identity_db`, connection string trong `appsettings.json` (`ConnectionStrings:IdentityDatabase`).
4. `dotnet run --project Services/Identity/Identity.Api` — migrate + seed chạy lúc startup
   (seed 5 role + ADMIN mặc định `admin@grottoworks.local`).
5. Smoke test: file `Identity.Api.http` (REST Client).

## Quy ước (xem `BuildingBlocks/README.md`)

- JWT claim: `sub`, `role`, `parishId`, `communityId` (literal, không map inbound).
- Envelope `{ data, meta: { correlationId } }`; lỗi ProblemDetails kèm `code`.
- Enum JSON snake_case (`ACTIVE`, `INACTIVE`).
- Mọi thời gian UTC (`DateTimeOffset`).
- `parish_id`, `community_id` là cross-service ref — lưu uuid, **không FK** (spec §2.1).

## Endpoint (S0+S1)

| Endpoint | operationId | Quyền |
|---|---|---|
| `POST /api/v1/auth/register` | `registerUser` | PUBLIC |
| `POST /api/v1/auth/login` | `login` | PUBLIC |
| `POST /api/v1/auth/refresh` | `refreshToken` | PUBLIC |
| `POST /api/v1/auth/logout` | `logout` | USER |
| `GET /api/v1/users/me` | `getMyProfile` | USER |
| `PATCH /api/v1/users/me` | `updateMyProfile` | USER |
| `GET /api/v1/skills` | `listSkills` | USER |
| `POST /api/v1/skills` | `createSkill` | ADMIN |
| `PATCH /api/v1/skills/{skillId}` | `updateSkill` | ADMIN |
| `GET /health/live`, `GET /health/ready` | — | PUBLIC |

## Endpoint (S2)

| Endpoint | operationId | Quyền | Ghi chú |
|---|---|---|---|
| `POST /api/v1/auth/password/forgot` | `requestPasswordReset` | PUBLIC | Luôn 202; chỉ account ACTIVE nhận mã; mã mới vô hiệu mã cũ |
| `POST /api/v1/auth/password/reset` | `resetPassword` | PUBLIC | 204; mã 1 lần, hết hạn 30'; revoke mọi refresh token |
| `PUT /api/v1/users/me/skills` | `replaceMySkills` | VOLUNTEER | `{ "skillIds": [...] }`; skill mới phải ACTIVE |
| `PUT /api/v1/users/me/availability` | `replaceMyAvailability` | VOLUNTEER | Mảng `AvailabilitySlot`; BR-70, không chồng lấn, lưu UTC |
| `GET /api/v1/users` | `listParishUsers` | PARISH, LEADER, MO | Chỉ parish trong token; filter `role,status,communityId,skillId,q` |
| `GET /api/v1/admin/users` | `listUsers` | ADMIN | + filter `parishId` |
| `POST /api/v1/admin/users` | `createUser` | ADMIN | BR-67; `password` tùy chọn (không có → owner đặt qua reset) |
| `PATCH /api/v1/admin/users/{id}` | `updateUser` | ADMIN | email/tên/sđt/parish/status ACTIVE↔INACTIVE; `version`/`If-Match` → 412 |
| `POST /api/v1/admin/users/{id}/lock` | `lockUser` | ADMIN | `ReasonCommand`; revoke refresh token; event `identity.user.locked` |
| `POST /api/v1/admin/users/{id}/unlock` | `unlockUser` | ADMIN | `ReasonCommand`; event `identity.user.unlocked` |
| `PUT /api/v1/admin/users/{id}/role` | `assignUserRole` | ADMIN | `{ role, parishId? }` — 1 role/account |
| `GET /api/v1/roles` | `listAssignableRoles` | ADMIN | 5 role theo thứ tự chuẩn |
| `GET /internal/users?ids=a,b` | — | Service (`X-Internal-Api-Key`) | ≤ 100 id; không qua gateway, ẩn khỏi OpenAPI |

Concurrency: `app_user` dùng cột hệ thống `xmin` của PostgreSQL làm `version` (không thêm cột ERD).
Admin không tự khóa / tự vô hiệu hóa / tự đổi role của chính mình (đảm bảo luôn còn ≥ 1 ADMIN).
## Việc còn mở (cập nhật 18/10, sau S2)

- `PendingParishDirectory` là bản **tạm**: chấp nhận mọi `communityId` và log warning. Cần thay bằng
  internal endpoint hoặc projection community của ParishCoordination (F-PAR-02, Lâm) trước demo S1.
- Denylist access token (Redis, theo `jti`) khi logout/lock chưa làm — access token cũ còn hiệu lực tối đa 15'.
- Chưa có integration test tự động (WebApplicationFactory + Testcontainers Postgres). S2 đã chạy
  smoke test E2E 63 kịch bản trên PostgreSQL 16 thật (migration, filter/paging, xmin 412, khóa → refresh 401,
  reset đồng thời → đúng 1 thành công), nhưng chưa đưa vào CI.
- **Kênh gửi mã reset**: chưa có email sender (NotificationAudit — Lâm). `LoggingPasswordResetNotifier` chỉ log
  "đã phát mã"; mã chỉ hiện trong log khi `PasswordReset:ExposeTokenInLogs=true` (bật sẵn trong
  `appsettings.Development.json`). Cần chốt với Lâm: event riêng hay HTTP nội bộ tới NotificationAudit.
- `ParishExistsAsync` cũng đang dùng `PendingParishDirectory` (chấp nhận mọi parish) — thay khi Parish có
  internal endpoint.
- Lý do khóa tài khoản không lưu ở Identity (ERD không có cột) — đi theo event `identity.user.locked` để
  Activity Log (F-NTF-04) ghi.
- Đổi role chưa có event (catalog API Contract mục 8 chưa có) — đề xuất `identity.user.role-assigned` nếu
  Activity Log cần.
- Identity chưa chuyển sang `BuildingBlocks.Web/Security` (xem BuildingBlocks README §4).
