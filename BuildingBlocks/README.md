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

Tới khi `BuildingBlocks.Web` (F-PLT-03) có, Identity.Api dùng bản địa phương:
`Identity.Api.Common` (ApiResponse/PagedResponse, CorrelationIdMiddleware,
ExceptionHandlingMiddleware, CurrentUser). Khi F-PLT-03 xong → refactor sang package chung, xóa bản địa phương.