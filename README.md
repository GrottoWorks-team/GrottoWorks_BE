# GrottoWorks Backend

Hướng dẫn này dành cho frontend developer muốn chạy toàn bộ backend GrottoWorks bằng Docker trên máy local.

Frontend chỉ nên gọi API qua Gateway tại:

```text
http://localhost:6102
```

Các port của từng service được mở chủ yếu để xem Swagger, health check và debug. Không dùng các URL service trực tiếp làm API base URL của frontend.

## 1. Cần cài đặt

Bắt buộc:

- Git.
- Docker Desktop phiên bản mới, có Docker Compose V2.
- Trên Windows: bật WSL 2 backend cho Docker Desktop.

Khuyến nghị:

- Tối thiểu 8 GB RAM trống cho Docker khi chạy toàn bộ stack.
- `curl`, Postman hoặc Bruno để kiểm tra API.

Không cần cài riêng .NET SDK, PostgreSQL, RabbitMQ, Redis hoặc MinIO nếu chỉ chạy bằng Docker.

Kiểm tra Docker trước khi bắt đầu:

```bash
docker --version
docker compose version
```

## 2. Clone và cấu hình

```bash
git clone <repository-url>
cd GrottoWorks_BE
```

Tạo file `.env` từ file mẫu.

PowerShell:

```powershell
Copy-Item .env.example .env
```

Bash, Git Bash hoặc macOS/Linux:

```bash
cp .env.example .env
```

Mở `.env` và thay toàn bộ giá trị `CHANGE_ME_...`.

Ví dụ cấu hình local:

```dotenv
POSTGRES_USER=postgres
POSTGRES_PASSWORD=<local-postgres-password>

RABBITMQ_DEFAULT_USER=grottoworks
RABBITMQ_DEFAULT_PASS=<local-rabbitmq-password>

MINIO_ROOT_USER=grottoworks
MINIO_ROOT_PASSWORD=<local-minio-password>

JWT_SIGNING_KEY=<random-string-at-least-32-characters>
SEED_ADMIN_PASSWORD=<admin-password-at-least-8-characters>
INTERNAL_API_KEY=<random-string-at-least-32-characters>
```

Quy tắc quan trọng:

- `JWT_SIGNING_KEY` phải có ít nhất 32 ký tự và được dùng chung bởi Identity, Gateway và các service.
- `INTERNAL_API_KEY` phải có ít nhất 32 ký tự. Đây là key cho giao tiếp nội bộ, frontend không sử dụng key này.
- Không commit `.env`. File này đã được đưa vào `.gitignore`.
- Không dùng các giá trị ví dụ ở môi trường thật.

## 3. Khởi động backend

Tại thư mục gốc repository, chạy:

```bash
docker compose up --build -d
```

Lần đầu Docker phải tải image và build bốn API service cùng Gateway nên có thể mất vài phút.

Kiểm tra container:

```bash
docker compose ps
```

Theo dõi toàn bộ log:

```bash
docker compose logs -f
```

Hoặc chỉ xem một service:

```bash
docker compose logs -f identity.api
docker compose logs -f parishcoordination.api
docker compose logs -f resource.api
docker compose logs -f taskexecution.api
docker compose logs -f yarpapigateway
```

Khi khởi động, các service tự chạy EF Core migration. Identity và ParishCoordination cũng tự seed dữ liệu local. Readiness có thể tạm trả `503` trong lúc migration hoặc seed chưa hoàn tất.

## 4. URL và port local

| Thành phần | URL | Mục đích |
|---|---|---|
| API Gateway | `http://localhost:6102` | Base URL duy nhất cho frontend |
| Identity API | `http://localhost:6100` | Swagger, health check, debug |
| ParishCoordination API | `http://localhost:6101` | Swagger, health check, debug |
| Resource API | `http://localhost:6103` | Swagger, health check, debug |
| TaskExecution API | `http://localhost:6104` | Swagger, health check, debug |
| PostgreSQL | `localhost:5432` | Database local |
| RabbitMQ AMQP | `localhost:5672` | Message broker |
| RabbitMQ Management | `http://localhost:15672` | Giao diện quản trị RabbitMQ |
| Redis | `localhost:6379` | Cache local |
| MinIO API | `http://localhost:9000` | S3-compatible API |
| MinIO Console | `http://localhost:9001` | Giao diện quản trị object storage |

RabbitMQ Management dùng `RABBITMQ_DEFAULT_USER` và `RABBITMQ_DEFAULT_PASS` trong `.env`.

MinIO Console dùng `MINIO_ROOT_USER` và `MINIO_ROOT_PASSWORD` trong `.env`.

Cấu hình base URL ở frontend, không thêm `/api/v1` vào biến môi trường để có thể dùng chung cho mọi route.

Vite (`.env.local`):

```dotenv
VITE_API_BASE_URL=http://localhost:6102
```

Next.js (`.env.local`):

```dotenv
NEXT_PUBLIC_API_BASE_URL=http://localhost:6102
```

## 5. Kiểm tra service đã sẵn sàng

Mỗi service API có hai endpoint public:

- `/health/live`: process đang chạy.
- `/health/ready`: service kết nối được database và sẵn sàng nhận request.

Ví dụ:

```bash
curl http://localhost:6100/health/ready
curl http://localhost:6101/health/ready
curl http://localhost:6103/health/ready
curl http://localhost:6104/health/ready
```

Kết quả mong đợi:

```json
{ "status": "Healthy" }
```

Gateway hiện không expose health endpoint riêng. Có thể smoke test Gateway bằng API login ở phần tiếp theo.

## 6. Swagger và OpenAPI

Swagger chỉ bật trong môi trường `Development`, là môi trường Docker Compose đang sử dụng.

| Service | Swagger | OpenAPI JSON |
|---|---|---|
| Identity | `http://localhost:6100/swagger` | `http://localhost:6100/openapi/v1.json` |
| ParishCoordination | `http://localhost:6101/swagger` | `http://localhost:6101/openapi/v1.json` |
| Resource | `http://localhost:6103/swagger` | `http://localhost:6103/openapi/v1.json` |
| TaskExecution | `http://localhost:6104/swagger` | `http://localhost:6104/openapi/v1.json` |

Swagger gọi trực tiếp service tương ứng. Frontend vẫn phải dùng Gateway để kiểm tra routing và authorization giống môi trường tích hợp.

## 7. Đăng nhập và sử dụng JWT

Identity seed một tài khoản quản trị local:

```text
Email: admin@grottoworks.local
Password: giá trị SEED_ADMIN_PASSWORD trong .env
```

Đăng nhập qua Gateway:

```bash
curl -X POST http://localhost:6102/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "email": "admin@grottoworks.local",
    "password": "<SEED_ADMIN_PASSWORD>"
  }'
```

Response có envelope chung:

```json
{
  "data": {
    "accessToken": "...",
    "refreshToken": "...",
    "expiresIn": 900,
    "tokenType": "Bearer"
  },
  "meta": {
    "correlationId": "..."
  }
}
```

Gửi access token cho API cần đăng nhập:

```http
Authorization: Bearer <accessToken>
```

Ví dụ JavaScript:

```javascript
const API_BASE_URL = "http://localhost:6102";

const response = await fetch(`${API_BASE_URL}/api/v1/seasons`, {
  headers: {
    Authorization: `Bearer ${accessToken}`,
  },
});

if (!response.ok) {
  const problem = await response.json();
  throw new Error(problem.code ?? problem.title);
}

const body = await response.json();
console.log(body.data);
```

Access token mặc định có hiệu lực 15 phút. Khi hết hạn, gọi:

```http
POST http://localhost:6102/api/v1/auth/refresh
Content-Type: application/json

{
  "refreshToken": "<refreshToken>"
}
```

API đăng nhập bị giới hạn 5 request trong 10 giây cho mỗi địa chỉ IP. Nếu gọi liên tục có thể nhận `429 Too Many Requests`.

## 8. CORS cho frontend

Gateway mặc định cho phép các origin local sau:

```text
http://localhost:3000
http://localhost:5173
http://localhost:6100
http://localhost:6101
```

Hai origin đầu phù hợp với Create React App/Next.js và Vite. Origin phải khớp chính xác protocol, host và port.

Nếu frontend chạy ở origin khác, ví dụ `http://localhost:4200`, thêm cấu hình sau vào phần `environment` của service `yarpapigateway` trong `docker-compose.override.yml`:

```yaml
- Cors__AllowedOrigins__0=http://localhost:4200
```

Sau đó recreate Gateway:

```bash
docker compose up -d --force-recreate yarpapigateway
```

Không gọi frontend bằng `127.0.0.1` nếu CORS chỉ khai báo `localhost`; đây là hai origin khác nhau.

## 9. Dữ liệu seed dành cho frontend

ParishCoordination seed idempotent các dữ liệu sau:

| Dữ liệu | ID |
|---|---|
| Demo Parish | `11111111-1111-1111-1111-111111111111` |
| Demo Community North | `22222222-2222-2222-2222-222222222222` |
| Demo Community South | `33333333-3333-3333-3333-333333333333` |
| Demo Season 2026 | `44444444-4444-4444-4444-444444444444` |
| Demo Work Area North | `66666666-6666-6666-6666-666666666666` |

Identity chỉ seed tài khoản `ADMIN`. Các tài khoản `PARISH`, `LEADER`, `MO` và `VOLUNTEER` cần được tạo qua API quản trị hoặc đăng ký theo flow tương ứng.

Lưu ý: thay đổi `SEED_ADMIN_PASSWORD` sau khi admin đã được tạo không tự đổi mật khẩu trong database. Seeder là idempotent và không ghi đè user hiện có.

## 10. Response và error contract

Response thành công thường có dạng:

```json
{
  "data": {},
  "meta": {
    "correlationId": "..."
  }
}
```

Response phân trang có thêm `page` hoặc page metadata theo endpoint.

Lỗi dùng `application/problem+json` và có stable error code:

```json
{
  "title": "...",
  "status": 403,
  "detail": "...",
  "code": "PARISH_ACCESS_DENIED",
  "correlationId": "...",
  "traceId": "..."
}
```

Frontend nên:

- Xử lý theo HTTP status và `code`, không so sánh nội dung `detail`.
- Ghi lại `correlationId` khi báo lỗi cho backend.
- Xử lý `401` bằng refresh/login lại.
- Hiển thị `403` là không đủ quyền, không tự động login lại.
- Với API có optimistic concurrency, lưu ETag và gửi lại bằng header `If-Match` khi cập nhật.

## 11. Các lệnh Docker thường dùng

Dừng container nhưng giữ dữ liệu:

```bash
docker compose stop
```

Khởi động lại:

```bash
docker compose start
```

Xóa container/network nhưng giữ volume database:

```bash
docker compose down
```

Build lại sau khi pull code mới:

```bash
docker compose up --build -d
```

Build và chạy lại một service:

```bash
docker compose build taskexecution.api
docker compose up -d taskexecution.api yarpapigateway
```

Xem 200 dòng log gần nhất:

```bash
docker compose logs --tail 200 identity.api
```

## 12. Reset toàn bộ dữ liệu local

Cảnh báo: lệnh sau xóa toàn bộ PostgreSQL và MinIO data trong Docker volume:

```bash
docker compose down -v
docker compose up --build -d
```

Chỉ dùng khi chắc chắn không cần dữ liệu local. PostgreSQL init script chỉ tạo database ở lần đầu khi volume còn trống.

## 13. Trạng thái tích hợp hiện tại

- Frontend không được gọi `/internal/**`. Các endpoint này dùng `X-Internal-Api-Key`, không đi qua Gateway và không xuất hiện trong Swagger.
- TaskExecution và Resource đang giữ local scope projection. Một số flow liên service chưa tự đồng bộ vì consumer/outbox messaging chưa hoàn tất.
- Dữ liệu demo của ParishCoordination không tự động trở thành projection trong TaskExecution hoặc Resource. Vì vậy một số API Task/Resource có thể trả `WORK_AREA_NOT_FOUND` hoặc `SEASON_NOT_FOUND` cho tới khi projection tương ứng được cấp qua internal feed hoặc messaging được hoàn thiện.
- Không dùng trực tiếp database của service khác để workaround projection.

## 14. Troubleshooting

### Compose báo thiếu `JWT_SIGNING_KEY`

Đảm bảo file tên chính xác là `.env`, nằm cùng cấp với `docker-compose.yml`, và key có ít nhất 32 ký tự.

Kiểm tra cấu hình Compose đã resolve:

```bash
docker compose config
```

Không đăng output lệnh này lên chat công khai vì có thể chứa secret.

### Port đã được sử dụng

Các port thường xung đột: `5432`, `5672`, `6379`, `9000`, `9001`, `6100` đến `6104` và `15672`.

Tắt ứng dụng/container đang dùng port hoặc đổi host port ở `docker-compose.override.yml`. Nếu đổi port `6102`, frontend phải cập nhật API base URL.

### Service chạy nhưng readiness trả `503`

Xem log service và PostgreSQL:

```bash
docker compose logs --tail 200 postgres
docker compose logs --tail 200 identity.api
```

Nguyên nhân thường gặp:

- PostgreSQL chưa healthy.
- Sai `POSTGRES_PASSWORD` trên volume đã được tạo bằng mật khẩu cũ.
- Migration thất bại.

Nếu đây chỉ là dữ liệu local có thể xóa, reset volume theo mục 12.

### Login admin thất bại

Kiểm tra:

- Email là `admin@grottoworks.local`.
- Password là giá trị đã dùng khi database được seed lần đầu.
- Identity readiness đang `Healthy`.
- Không bị rate limit do gọi login quá nhanh.

Nếu đã thay `SEED_ADMIN_PASSWORD` sau lần chạy đầu, password trong database không tự thay đổi.

### Frontend bị CORS

Kiểm tra request đang gửi tới Gateway `http://localhost:6102`, không phải service trực tiếp. Sau đó kiểm tra origin của frontend đã nằm trong `Cors:AllowedOrigins` và recreate Gateway sau khi đổi cấu hình.

### API trả `401`

Kiểm tra header có đúng định dạng:

```http
Authorization: Bearer <accessToken>
```

Đồng thời bảo đảm tất cả service và Gateway đang dùng cùng `JWT_SIGNING_KEY`.

### API trả `404` từ Gateway nhưng Swagger có endpoint

Route có thể chưa được khai báo trong YARP Gateway. Frontend không nên chuyển sang gọi service trực tiếp; báo endpoint và operation ID cho backend để bổ sung route đúng service.
