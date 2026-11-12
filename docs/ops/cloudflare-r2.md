# Cloudflare R2 — lưu file (ảnh tiến độ, minh chứng, thiết kế 3D)

File service (`NotificationAudit`) nói chuyện với object storage qua **S3 API** (`AWSSDK.S3`), nên cùng một code chạy được:

| Môi trường | Provider | Endpoint |
|---|---|---|
| Local (`docker compose up`) | `MinIO` (mặc định) | `http://minio:9000`, URL trình duyệt `http://localhost:9000` |
| Staging / Production | `R2` | `https://<ACCOUNT_ID>.r2.cloudflarestorage.com` |

Luồng upload không đổi: `POST /api/v1/files/upload-requests` → trình duyệt `PUT` presigned URL lên `pending/...` → `POST /files/{id}/complete` (BE copy sang `completed/...`, kiểm tra size/type/SHA-256) → gắn nghiệp vụ → `GET .../download-url` trả presigned GET.

Kế hoạch tổng: `GrottoWorks_Documents/GrottoWorks_Cloudflare_R2_Storage_Plan_v1.md`.

---

## 1. Tạo bucket (làm 1 lần / môi trường)

1. Cloudflare Dashboard → **R2 Object Storage** → **Create bucket**
   - Tên: `grottoworks-files` (prod), `grottoworks-files-staging` (staging).
   - Location hint: Asia-Pacific (APAC).
   - **Không** bật Public access / `r2.dev`, **không** gắn custom domain public: bucket private, mọi truy cập qua presigned URL.
2. Bucket → **Settings → CORS policy** (bắt buộc — trình duyệt PUT/GET trực tiếp vào R2):
   ```json
   [
     {
       "AllowedOrigins": [
         "https://<domain-fe>",
         "http://localhost:5173",
         "http://localhost:5174",
         "http://localhost:5175",
         "http://localhost:8081",
         "http://localhost:8082"
       ],
       "AllowedMethods": ["GET", "PUT", "HEAD"],
       "AllowedHeaders": ["Content-Type"],
       "ExposeHeaders": ["ETag", "Content-Length"],
       "MaxAgeSeconds": 3600
     }
   ]
   ```
   Thiếu `localhost:5175` = Vite dev server của FE — upload minh chứng sẽ chết với lỗi
   `CORS not configured for this bucket` / `No 'Access-Control-Allow-Origin' header`.
   Kiểm tra nhanh bucket đã có CORS chưa:

   ```bash
   curl -i -X OPTIONS "https://<ACCOUNT_ID>.r2.cloudflarestorage.com/<bucket>/pending/x" \
     -H "Origin: http://localhost:5175" \
     -H "Access-Control-Request-Method: PUT" \
     -H "Access-Control-Request-Headers: content-type"
   ```

   - `204 No Content` + `Access-Control-Allow-Origin: http://localhost:5175` ⇒ đã cấu hình đúng.
   - `403` + `<Message>CORS not configured for this bucket</Message>` ⇒ **chưa có CORS policy**.

   Lưu ý: token R2 quyền **Object Read & Write** **không** đặt được CORS qua S3 API
   (`PUT /<bucket>?cors` → `403 AccessDenied`), phải làm ở dashboard (hoặc tạo token
   **Admin Read & Write** cho bucket). Trong lúc chưa cấu hình, local dev dùng MinIO
   (`OBJECT_STORAGE_PROVIDER=MinIO`, xem `docker-compose.override.yml`) — MinIO đã bật
   CORS cho `:5173-:5175`, `:8081-:8082`.
3. Bucket → **Settings → Object lifecycle rules**:
   - `pending/` — Delete objects after **1 day** (upload bỏ dở).
   - Abort incomplete multipart uploads after **1 day**.
   - `SystemObjectCleanupWorker` vẫn chạy song song làm lớp đối soát với DB.

## 2. Tạo API token

R2 → **Manage R2 API Tokens** → **Create API token**:
- Permission: **Object Read & Write**.
- Specify bucket: chỉ bucket của môi trường đó (staging token ≠ prod token).
- Lưu `Access Key ID` + `Secret Access Key` vào secret của server deploy. **Không commit, không dán vào issue/chat/log CI.**

Token giới hạn theo bucket nên không tạo được bucket: BE tự đặt `ManageBucket=false` khi `Provider=R2` và chỉ kiểm tra bucket truy cập được lúc khởi động (log lỗi nếu không).

## 3. Cấu hình service

`.env` (đọc bởi `docker-compose.override.yml`, áp cho `notificationaudit.api` và backup worker):

```dotenv
OBJECT_STORAGE_PROVIDER=R2
OBJECT_STORAGE_ENDPOINT=https://<ACCOUNT_ID>.r2.cloudflarestorage.com
OBJECT_STORAGE_ACCESS_KEY=<access key id>
OBJECT_STORAGE_SECRET_KEY=<secret access key>
OBJECT_STORAGE_BUCKET=grottoworks-files-staging
```

Hoặc biến môi trường trực tiếp (`ObjectStorage__*`):

| Key | Ý nghĩa |
|---|---|
| `ObjectStorage__Provider` | `MinIO` \| `R2` |
| `ObjectStorage__InternalEndpoint` | Endpoint S3. Với R2 có thể để trống và đặt `ObjectStorage__AccountId` |
| `ObjectStorage__PublicEndpoint` | Chỉ MinIO (host trình duyệt truy cập được). R2 bỏ qua — presigned URL của R2 chỉ hợp lệ trên endpoint S3 |
| `ObjectStorage__Region` | Mặc định `auto` (R2) / `us-east-1` (MinIO) |
| `ObjectStorage__AccessKey`, `ObjectStorage__SecretKey` | Credential |
| `ObjectStorage__Bucket` | Tên bucket |
| `ObjectStorage__ManageBucket` | Mặc định `true` cho MinIO, `false` cho R2 |

Ghi chú kỹ thuật (đã xử lý trong `ObjectStorageClientFactory`): path-style URL, `AuthenticationRegion=auto`, tắt checksum CRC mặc định của AWSSDK v4 (`WHEN_REQUIRED`), `PutObject` qua HTTPS dùng `UNSIGNED-PAYLOAD` vì R2 không nhận chữ ký streaming.

## 4. Kiểm tra sau khi cấu hình

1. `GET /health/ready` của NotificationAudit OK, log không có `Object storage bucket ... is not accessible`.
2. Trên FE: checklist → tải ảnh minh chứng (luồng `ReadinessEvidence`), hoặc nộp tiến độ task kèm ảnh từ mobile → mở ảnh xem được.
3. Lỗi CORS trên trình duyệt (`PUT` bị chặn) → kiểm tra lại `AllowedOrigins` và header `Content-Type` (được ký trong URL — client phải gửi đúng giá trị BE trả trong `headers`).
4. `403 SignatureDoesNotMatch` → sai secret, sai endpoint (phải là endpoint S3 có account ID), hoặc client gửi `Content-Type` khác.

**Đã kiểm chứng end-to-end 12/11/2026** (máy dev, `Provider=R2`, bucket `grottoworks`):

| Bước | Kết quả |
|---|---|
| `POST /api/v1/files/upload-requests` (`purpose=ProgressEvidence`, `image/png`) | `201`, `uploadUrl` trỏ `*.r2.cloudflarestorage.com` |
| `PUT` presigned (header ký `Content-Type: image/png`) | `200` |
| `POST /api/v1/files/{id}/complete` | OK, `sizeBytes = 70` (BE đọc lại object bằng HEAD trên R2) |
| `GET /api/v1/files/{id}/download-url` rồi `GET` | `200`, đúng 70 byte, host R2 |
| `DELETE /api/v1/files/{id}` | `204` |

Lưu ý khi đổi provider trên máy đã dùng MinIO: object cũ nằm ở bucket MinIO `grottoworks-files`,
nên `download-url` của file cũ sẽ `404` (metadata còn, object không còn) — cần copy object sang R2 trước khi đổi,
hoặc chỉ đổi provider ở môi trường mới.

Khôi phục backup vào R2 (`tools/Verification restore-objects`): tạo trước bucket rỗng tên `grottoworks-restore-*`, token có quyền trên bucket đó, rồi chạy với
`VERIFY_STORAGE_PROVIDER=R2 VERIFY_STORAGE_ENDPOINT=... VERIFY_STORAGE_ACCESS_KEY=... VERIFY_STORAGE_SECRET_KEY=...`.

## 5. Chi phí

Free tier mỗi tháng: 10 GB lưu trữ, 1 triệu thao tác Class A (PUT/COPY/LIST), 10 triệu Class B (GET/HEAD); egress miễn phí. Vượt: ~$0,015/GB-tháng. Bật billing notification trong Cloudflare.
