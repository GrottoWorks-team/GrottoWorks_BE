# Identity Service

Dịch vụ xác thực & hồ sơ người dùng (F-IDN-01 → F-IDN-06, xem `GrottoWorks_BE_Task_Assignment_Spec_v1.md`).

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
## Việc còn mở (sau review 07/10)

- `PendingParishDirectory` là bản **tạm**: chấp nhận mọi `communityId` và log warning. Cần thay bằng
  internal endpoint hoặc projection community của ParishCoordination (F-PAR-02, Lâm) trước demo S1.
- Denylist access token (Redis, theo `jti`) khi logout/lock chưa làm — access token cũ còn hiệu lực tối đa 15'.
- Chưa có integration test (WebApplicationFactory + Testcontainers Postgres) cho rotation đồng thời
  và unique violation → 409.
