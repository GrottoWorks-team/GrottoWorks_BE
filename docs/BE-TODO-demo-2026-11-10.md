# BE TODO — chạy thật 3 thay đổi demo (10/11/2026)

**Bối cảnh:** ngày 10/11/2026, FE web + app mobile đã làm **demo** cho 3 thay đổi
nghiệp vụ. Demo chạy được trên dữ liệu cục bộ (localStorage/IndexedDB) và một
trang chia sẻ công khai; **chưa có dữ liệu thật trong BE**. Tài liệu này liệt kê
đúng những gì BE phải bổ sung để demo thành tính năng thật, kèm chỗ cần sửa ở FE
khi API có.

Đối chiếu liên quan: `GrottoWorks_FE/docs/specs/2026-11-03-clone-season-and-parish-map.md`
(§2 bản đồ giáo xứ), `GrottoWorks_FE/docs/specs/2026-10-27-3d-layer-builder.md`
(§6 API scene), `API-GAP-STATUS-2026-11-04.md` mục A6.

---

## 0. Tóm tắt 3 thay đổi

| # | Nghiệp vụ chốt | Demo hiện tại (FE/mobile) | Thiếu ở BE |
|---|---|---|---|
| 1 | **ADMIN** là người tải ảnh sơ đồ giáo xứ và tạo/quản khu vực (ban hành giáo chỉ xem) | `ParishMapPage.canEdit = role === 'PARISH'`; ẩn nút Tạo/Sửa/Phân công khu vực với `COMMUNITY` | Không có entity/endpoint bản đồ; policy tạo khu vực còn cho `PARISH` |
| 2 | TNV xem được **mô hình 3D gắn với nhiệm vụ đã nhận** | Mobile: thẻ "Mô hình 3D" ở `TaskDetailScreen` → mở web `/share/scene/:areaId?task=…` (scene demo nếu chưa có scene) | Không có API `design-scene` / token chia sẻ; scene chỉ nằm localStorage |
| 3 | **Tạo khu vực + ghim vị trí trong cùng một lần lưu** | `AreaFormDialog` có khối "Vị trí trên sơ đồ" (bấm lên ảnh để ghim; tuỳ chọn) | Không có API lưu pin; pin đang ở localStorage key `grotto-parish-map-v1` |

---

## 1. Bản đồ giáo xứ (`ParishCoordination`)

### 1.1. Migration

```sql
ALTER TABLE parish
  ADD COLUMN map_file_id          UUID          NULL,
  ADD COLUMN map_file_name        VARCHAR(255)  NULL,
  ADD COLUMN map_image_width      INT           NULL,
  ADD COLUMN map_image_height     INT           NULL,
  ADD COLUMN map_frame_version    INT           NOT NULL DEFAULT 1,
  ADD COLUMN map_meters_per_pixel NUMERIC(10,6) NULL,
  ADD COLUMN map_pins             JSONB         NULL, -- { "<workAreaId>": { "x": 0.42, "y": 0.61 } }
  ADD COLUMN map_updated_at       TIMESTAMPTZ   NULL;
```

Lý do **không** thêm `map_x/map_y` vào `work_area`: `SaveChanges` sinh outbox cho
mọi `WorkArea` bị sửa → mỗi lần kéo ghim bump `Version` và đẩy snapshot sang 4
service (xem spec §2.3).

### 1.2. Endpoint

```http
GET    /api/v1/parishes/{parishId}/map   → 200 { fileId, fileName, imageWidth, imageHeight,
                                                 frameVersion, metersPerPixel, pins[], updatedAt } | 204
PUT    /api/v1/parishes/{parishId}/map   → 200  (CHỈ ADMIN)
DELETE /api/v1/parishes/{parishId}/map   → 204  (CHỈ ADMIN)
```

- `PUT` nhận **nguyên trạng thái** (idempotent), không PATCH từng ghim.
- `expectedFrameVersion` lệch → `409 MAP_FRAME_CONFLICT`.
- Validate: `0 <= x,y <= 1`, không trùng `workAreaId`, mọi `workAreaId` thuộc một
  mùa của `parishId`.
- Ảnh: dùng `POST /api/v1/files/upload-requests` + `POST /api/v1/files/{id}/complete`
  đã có; BE chỉ lưu `fileId` (không proxy file).

### 1.3. Policy (thay đổi so với spec cũ)

| Hành động | Policy đích |
|---|---|
| `PUT`/`DELETE /parishes/{id}/map` | **ADMIN** (`GrottoWorksRoles.Admin`) |
| `GET /parishes/{id}/map` | ADMIN + PARISH + LEADER (+ VOLUNTEER nếu cần xem sơ đồ khi làm việc) |

⚠️ Spec `2026-11-03-clone-season-and-parish-map.md` §2.4 hiện ghi "ADMIN + ban
hành giáo của chính giáo xứ" — **phải sửa thành ADMIN-only** cho khớp yêu cầu
10/11/2026 (ban hành giáo chỉ xem).

### 1.4. FE sửa khi có API

- `src/features/map/api.ts`: thay `mapStore.*` bằng `api()`; giữ nguyên hook
  surface (`useParishMap`, `useSaveParishMap`, `useMapPins`, `useSetMapPin`).
- `src/features/map/localStore.ts`: xoá sau khi chuyển xong.
- `src/features/map/pages/ParishMapPage.tsx`: bỏ ghi chú "lưu trên máy này";
  upload ảnh đổi sang flow 3 request (presigned).
- `src/features/areas/pages/AreaFormDialog.tsx`: khối ghim gọi cùng endpoint
  `PUT /parishes/{id}/map` (batch) thay vì `useSetMapPin` cục bộ.

---

## 2. Quyền tạo/quản khu vực về ADMIN

Hiện `WorkAreaEndpoints.cs` cho `Admin, Parish` ở `POST /seasons/{id}/work-areas`
và các `PUT .../leader|material-officer|community`. FE đã ẩn nút với
`COMMUNITY` (UI), nhưng BE vẫn cho gọi trực tiếp.

| Endpoint | Policy hiện tại | Policy đích |
|---|---|---|
| `POST /seasons/{seasonId}/work-areas` | ADMIN, PARISH | **ADMIN** |
| `PATCH /work-areas/{id}` | ADMIN, PARISH | **ADMIN** |
| `PUT /work-areas/{id}/leader` \| `/material-officer` \| `/community` | ADMIN, PARISH | **ADMIN** |
| `PUT /work-areas/{id}/season` | ADMIN, PARISH | **ADMIN** (nếu đang có) |
| `GET /work-areas/**`, `/work-areas/{id}/progress` | giữ nguyên | giữ nguyên |

Ghi chú: LEADER vẫn cần `PUT .../leader`? Hiện LEADER không được phép — giữ nguyên.

---

## 3. Mô hình 3D gắn với nhiệm vụ (TaskExecution + Readiness/Design)

### 3.1. Dữ liệu

Scene đang có cấu trúc JSON (`ConstructionScene`, `schemaVersion = 1` — xem
`GrottoWorks_FE/src/features/builder/types.ts`): `layers[]` (có `taskIds[]`) +
`items[]` (assetId, transform). Cần:

```sql
CREATE TABLE work_area_scene (
  work_area_id   UUID PRIMARY KEY,
  document       JSONB       NOT NULL,
  revision       INT         NOT NULL,
  updated_by     UUID        NOT NULL,
  updated_at     TIMESTAMPTZ NOT NULL
);
CREATE TABLE scene_version (
  id           UUID PRIMARY KEY,
  work_area_id UUID        NOT NULL,
  revision     INT         NOT NULL,
  document     JSONB       NOT NULL,
  created_by   UUID        NOT NULL,
  created_at   TIMESTAMPTZ NOT NULL
);
```

### 3.2. Endpoint (LEADER sửa, TNV đọc)

```http
GET /api/v1/work-areas/{workAreaId}/design-scene
    → 200 { revision, document, updatedAt } | 204
PUT /api/v1/work-areas/{workAreaId}/design-scene      If-Match: <revision>
    → 200 | 409 SCENE_REVISION_CONFLICT      (LEADER của khu, ADMIN)
GET /api/v1/work-areas/{workAreaId}/design-scene/versions                (LEADER, ADMIN)
```

Quyền đọc cho **VOLUNTEER**: chỉ khi TNV có `task_assignment` ở trạng thái
`ACCEPTED`/`COMPLETED` cho một task thuộc khu vực đó (join
`task_projection.work_area_id` + `task_assignment.volunteer_id`). Trả **scene đã
lọc**: chỉ các lớp có `taskIds` giao với task của TNV, hoặc toàn bộ nếu nghiệp vụ
cho xem cả công trình (cần chốt — hiện demo cho xem cả scene, tô sáng lớp của TNV).

### 3.3. Token chia sẻ (để mobile mở bằng trình duyệt, không cần Bearer)

Demo hiện mở `GET {WEB}/share/scene/{areaId}?task={taskId}` — trang công khai.
Khi chạy thật, **không** để areaId trần (đoán được id là xem được). Chọn 1 trong 2:

- **A. Mobile gọi API trực tiếp** (khuyến nghị): bỏ trang share công khai, mobile
  gọi `GET /work-areas/{id}/design-scene` bằng Bearer rồi render 3D trong app
  (cần `expo-gl` + `three`).
- **B. Giữ trang share**: thêm `POST /api/v1/tasks/{taskId}/scene-share-link`
  (VOLUNTEER đã nhận việc) → `{ token, expiresAt }`; trang công khai gọi
  `GET /api/v1/share/scenes/{token}` (token có hạn, chỉ trả scene đã lọc).

### 3.4. FE/mobile sửa khi có API

- `src/features/builder/repository.ts`: thêm `RemoteSceneRepository` (giữ
  `LocalSceneRepository` cho nháp); chọn implementation theo biến môi trường.
- `src/features/builder/pages/SceneSharePage.tsx`: thay `sceneRepository.load` +
  `buildDemoScene` bằng API/token.
- `GrottoWokrs_Mobile/src/features/volunteer/TaskDetailScreen.tsx`: đổi
  `Linking.openURL(...)` sang màn hình 3D native (nếu chọn phương án A) hoặc
  truyền token (phương án B).
- `GrottoWokrs_Mobile/src/api/client.ts`: giữ `WEB_BASE`/`webUrl` nếu chọn B.

---

## 4. Lưu ghim cùng lúc tạo khu vực (1 request)

Demo: `AreaFormDialog` tạo khu vực (`POST /work-areas`) rồi mới ghi pin cục bộ.
Khi có API map, có 2 lựa chọn — **chốt 1**:

- **A. Giữ 2 bước phía client**: `POST /work-areas` → `PUT /parishes/{id}/map`
  (batch toàn bộ pin). Đơn giản, không sửa contract tạo khu vực.
- **B. 1 request**: thêm `mapPin: { x, y } | null` (optional) vào
  `CreateWorkAreaRequest`; handler tạo khu vực và ghi pin vào `parish.map_pins`
  **trong cùng transaction**. Nhanh hơn cho người dùng, nhưng handler
  ParishCoordination phải đọc/ghi `parish` (thêm lock nhẹ để tránh mất pin khi 2
  admin tạo cùng lúc — dùng `SELECT ... FOR UPDATE` hoặc `jsonb_set` nguyên tử).

Yêu cầu chung: **pin phải optional** — chưa có ảnh sơ đồ vẫn tạo được khu vực
(giữ quyết định form-first, spec §2.2).

---

## 5. Thứ tự & ước lượng

| Bước | Việc | Ước lượng |
|---|---|---|
| 1 | Map migration + `GET/PUT/DELETE /parishes/{id}/map` + validate `frameVersion` | 4–5 h |
| 2 | Đổi policy khu vực sang ADMIN (mục 2) + test 403 | 1 h |
| 3 | `work_area_scene` + `GET/PUT design-scene` (LEADER) | 3–4 h |
| 4 | Quyền đọc scene cho VOLUNTEER + lọc lớp theo task | 2 h |
| 5 | Token chia sẻ (nếu chọn phương án B) | 1–2 h |
| 6 | Ghim trong `POST /work-areas` (nếu chọn 4-B) | 2–3 h |
| 7 | FE thay `mapStore`/`SceneRepository` bằng API | 3–4 h |

## 6. Kiểm chứng tối thiểu

- `PUT /parishes/{id}/map` bằng tài khoản PARISH (ban hành giáo) → **403**;
  bằng ADMIN → 200. `GET` bằng PARISH → 200.
- `POST /seasons/{id}/work-areas` bằng PARISH → **403**; ADMIN → 201.
- `PUT` 5 ghim liên tiếp **không** làm tăng `work_area.version`.
- `GET /work-areas/{id}/design-scene` bằng VOLUNTEER chưa được phân công → **403**;
  đã `ACCEPTED` task thuộc khu → 200 và chỉ thấy lớp liên quan.
- `PUT design-scene` với `If-Match` cũ → `409 SCENE_REVISION_CONFLICT`.
- Tạo khu vực khi chưa có ảnh sơ đồ → 201 (pin = null).
