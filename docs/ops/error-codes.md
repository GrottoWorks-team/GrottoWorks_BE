# Mã lỗi API ↔ i18n client (FE web + mobile)

> Cập nhật 12/11/2026. Liên quan: `api-contracts.md` (ProblemDetails), `tools/audit-error-codes.mjs`.

## 1. Hợp đồng lỗi

Mọi lỗi nghiệp vụ BE trả về `application/problem+json`:

```json
{
  "type": "https://api.grottoworks.example/problems/work-area-not-in-readiness-review",
  "title": "Conflict",
  "status": 409,
  "detail": "Readiness can be confirmed only while the work area is in READINESS_REVIEW (every non-cancelled task COMPLETED, BR-73).",
  "code": "WORK_AREA_NOT_IN_READINESS_REVIEW",
  "correlationId": "...",
  "traceId": "..."
}
```

- `code` là **mã ổn định** để client dịch và người dùng đối chiếu log.
- `detail` là tiếng Anh, kỹ thuật — client **không** hiển thị trực tiếp nếu đã có câu dịch.
- Lỗi validation trả `errors` (map field → messages); client hiển thị cạnh field hoặc gộp thành toast.

## 2. Thứ tự dịch ở client

Cả web (`GrottoWorks_FE/src/lib/api.ts` → `translateCode`) và mobile
(`GrottoWokrs_Mobile/src/api/client.ts` → `translateApiCode`) dùng đúng một chuỗi tra:

1. `errors.<CODE>` — câu soạn riêng, sát ngữ cảnh nhất (247 mã ở web, 145 mã ở mobile).
2. `errorsByKind.<kind>` — câu theo **nhóm hậu tố**, để mã BE mới thêm vẫn ra tiếng Việt:

| kind | Hậu tố khớp | Ví dụ |
|---|---|---|
| `notFound` | `_NOT_FOUND`, `_UNKNOWN` | `TASK_NOT_FOUND` |
| `required` | `_REQUIRED`, `_ID_REQUIRED`, `_REASON_REQUIRED`, `_NOTE_REQUIRED` | `SEASON_NAME_REQUIRED` |
| `invalidState` | `_INVALID_STATE`, `_NOT_OPEN`, `_NOT_READY`, `_NOT_COMPLETED`, `_NOT_ACCEPTED`, `_NOT_APPROVED`, `_NOT_LOCKED` | `ATTENDANCE_NOT_OPEN` |
| `denied` | `_ACCESS_DENIED`, `_SCOPE_DENIED`, `_DENIED`, `FORBIDDEN`, `UNAUTHENTICATED` | `WORK_AREA_SCOPE_DENIED` |
| `conflict` | `_EXISTS`, `_DUPLICATE`, `_CONFLICT`, `_ALREADY_*`, `_RESERVED`, `_ATTACHED` | `PURCHASE_TASK_EXISTS` |
| `versionConflict` | `_VERSION_MISMATCH`, `INVALID_IF_MATCH`, `SOURCE_VERSION_INVALID` | `TASK_VERSION_MISMATCH` |
| `unavailable` | `_UNAVAILABLE`, `_SERVICE_UNAVAILABLE`, `_STORAGE_UNAVAILABLE` | `FILE_STORAGE_UNAVAILABLE` |
| `tooMany` | `_TOO_LONG`, `_TOO_MANY`, `_LIMIT`, `_EXCEEDED`, `CAPACITY` | `TASK_CAPACITY_EXCEEDED` |
| `pending` | `_PENDING`, `_IN_PROGRESS`, `_SYNC_PENDING` | `ATTENDANCE_CORRECTION_PENDING` |
| `invalid` | `_INVALID`, `_MISMATCH`, `_INVALID_RANGE`, `_OUT_OF_SEASON`, `_IN_FUTURE`, `_IN_PAST`, `INVALID_*` | `TASK_DATE_RANGE_INVALID` |

3. Không khớp gì → client hiển thị `detail` của BE (tiếng Anh) và giữ `code` để đối chiếu.

> Mobile: `ApiError` giữ thêm `code` và `detail` (`src/api/client.ts`), nhờ vậy mọi màn hình
> dùng `error.message` tự động ra câu tiếng Việt — không phải sửa từng màn.

## 3. Kiểm tra đồng bộ (bắt buộc sau khi thêm mã BE mới)

```bash
# trong GrottoWorks_BE
node tools/audit-error-codes.mjs                    # báo mã chưa có câu dịch ở FE + mobile
node tools/audit-error-codes.mjs --json             # dùng cho CI
node tools/audit-error-codes.mjs --fail-on-missing   # exit 1 nếu còn mã lộ detail (dùng trong CI)
node tools/audit-error-codes.mjs --fe=../GrottoWorks_FE --mobile=../GrottoWokrs_Mobile
```

Kết quả 12/11/2026: **440 mã BE**, FE 0 mã lộ `detail`, mobile 0 mã lộ `detail`.

## 4. Quy ước khi thêm mã lỗi mới

1. BE: mã dạng `<DOMAIN>_<MEANING>` (vd `PURCHASE_RECEIPT_REQUIRED`), `detail` tiếng Anh nêu rõ
   điều kiện nghiệp vụ + mã BR nếu có.
2. Nếu mã rơi vào một trong các nhóm hậu tố ở §2 → không cần sửa client, client tự dịch.
3. Nếu là mã mới **không** thuộc nhóm nào (vd `_SUPERSEDED`, `_OVERLAP`, `_EXPIRED`) → thêm
   `errors.<CODE>` cho **cả** `vi.json`/`en.json` ở FE **và** mobile (giữ đúng thứ tự 2 ngôn ngữ).
4. Chạy lại `tools/audit-error-codes.mjs` — phải về 0 mã lộ `detail`.
