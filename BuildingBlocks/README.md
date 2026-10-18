# BuildingBlocks — chốt contract dùng chung

Tài liệu này là nguồn chốt cho các contract dùng chung giữa các service. Mọi service
(published qua Gateway) phải bám đúng các quy ước ở đây. Thay đổi contract → sửa file này
**trước**, tag người còn lại review (plan.md §4.1, "Contract").

## 1. JWT access token

Do Identity phát hành; Gateway/service khác validate. **Ký hiệu HMAC-SHA256**, issuer/audience/key
đọc từ config (`Jwt:Issuer`, `Jwt:Audience`, `Jwt:SigningKey` — key qua env/provider, không commit).

| Claim | Kiểu | Ý nghĩa |
|---|---|---|
| `sub` | `uuid` | `user_id` của account |
| `role` | `string` | Role code: `ADMIN`, `PARISH`, `LEADER`, `MO`, `VOLUNTEER` |
| `parishId` | `uuid?` | Tenant parish; **chỉ null cho ADMIN** (BR-67) |
| `communityId` | `uuid?` | Community hiện tại của volunteer (nếu có) |
| `jti` | `uuid` | Token id, chống replay |

Quy ước:
- Tên claim **viết thường theo đúng bảng trên** — mọi service đọc literal claim, không map
  inbound claim type (JWT bearer dùng `MapInboundClaims = false`, `RoleClaimType = "role"`,
  `NameClaimType = "sub"`).
- Access token ngắn hạn: **15 phút** (F-IDN-03). Refresh token **14 ngày**, rotate mỗi lần dùng,
  detect reuse → revoke cả family.

## 2. Event envelope (outbox / messaging)

`BuildingBlocks.Messaging` (F-PLT-05) publish các event theo envelope:

| Field | Kiểu | Ghi chú |
|---|---|---|
| `eventType` | `string` | `{domain}.{entity}.{action}` — ví dụ `identity.user.registered` |
| `version` | `int` | Version payload; đổi payload → bump version |
| `payload` | `object` | Record trong `<Service>.Contracts.Events` |
| `aggregateType` | `string` | `user`, `skill`, ... |
| `aggregateId` | `uuid` | Id aggregate |
| `occurredAt` | `DateTimeOffset` | UTC |

Event contract-first: record + `EventType` + `Version` khai báo trong `<Service>.Contracts`
trước khi hạ tầng publish có, để consumer code sớm.

## 3. API response envelope

Mọi endpoint trả JSON dạng `{ "data": ..., "meta": { "correlationId": "uuid" } }`.
Lỗi: RFC 9457 ProblemDetails kèm `code` ổn định + `correlationId` + `traceId`.

## 4. Interrim của Vũ (sẽ thay bằng BuildingBlocks.Web)

`BuildingBlocks.Web` (F-PLT-03) **đã có** và `ParishCoordination.Api` đã dùng chung
(`AddGrottoWorksWeb` / `UseGrottoWorksDefaults` / `ApiResults` / `IReadinessProbe`).
Tuy nhiên `Identity.Api` **vẫn còn** bản địa phương `Identity.Api.Common`
(ApiResponse/PagedResponse, CorrelationIdMiddleware, ExceptionHandlingMiddleware,
CurrentUser) — di chuyển sang package chung được ghi nhận ở đây để không phá 49 test
unit đang xanh; refactor Identity sang `BuildingBlocks.Web` + `BuildingBlocks.Security`
là việc còn lại của F-PLT-03/04, xóa bản địa phương sau khi test xanh.

## 5. Gọi service-to-service (`/internal/**`) — đề xuất của Vũ, cần Lâm review

Áp dụng cho `GET /internal/users` (F-IDN-09, đã có) và sau này `POST /internal/tasks`,
`POST /internal/support-transfers`, `GET /internal/files/{id}`.

| Mục | Quy ước |
|---|---|
| Xác thực | Header `X-Internal-Api-Key: <key>`; key chung cấu hình `InternalApi:ApiKey` (env `InternalApi__ApiKey`, compose lấy từ `INTERNAL_API_KEY` trong `.env`), tối thiểu 32 ký tự, so sánh constant-time. Thiếu cấu hình → mọi call bị 401 `INTERNAL_AUTH_REQUIRED` |
| Mạng | Gateway **không** có route `/internal/**` (gọi qua gateway → 404); chỉ service trong compose network gọi trực tiếp `http://identity.api:8080/internal/...` |
| OpenAPI | Endpoint internal `ExcludeFromDescription()` — không lộ cho FE/Mobile |
| Response | Cùng envelope `{ data, meta }` và ProblemDetails như API public |
| Client | Typed `HttpClient`, timeout, retry chỉ cho GET, circuit breaker (spec §6.2) |

### `GET /internal/users?ids=<uuid>,<uuid>` (Identity)

`ids` phân tách bằng dấu phẩy và/hoặc lặp tham số; 1–100 id (sai → 400). Id không tồn tại bị **bỏ qua**
(caller tự so sánh danh sách trả về). Thứ tự theo thứ tự `ids` gửi lên.

```json
{
  "data": [
    {
      "userId": "uuid",
      "displayName": "Nguyễn Văn A",
      "status": "ACTIVE",
      "role": "LEADER",
      "parishId": "uuid",
      "communityId": "uuid | null",
      "skillIds": ["uuid"]
    }
  ],
  "meta": { "correlationId": "uuid" }
}
```

Parish (F-PAR-08) kiểm tra `status == ACTIVE`, `role` đúng (LEADER/MO) và `parishId` khớp; Task (F-TSK-06)
dùng thêm `communityId`, `skillIds`. Không trả email/sđt (giảm dữ liệu cá nhân qua service khác).
