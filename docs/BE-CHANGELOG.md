# BE Changelog

## 2026-11-26 (rev 5) — seeder demo không còn tự thêm dữ liệu vào DB đã seed
- Branch: `feat/backend-workflow-improvements`
- Service: ParishCoordination
- Files: `ParishCoordination.Infrastructure/Data/ParishCoordinationDataSeeder.cs`
- Change: `ParishCoordinationDataSeeder` chỉ tạo dữ liệu demo khi **bảng `parish` còn rỗng**. Trước đây nó kiểm tra theo id cố định nên sau khi nạp dữ liệu demo thật (`SeedingData`, mùa 2025) mỗi lần restart service lại thêm `GrottoWorks Demo Parish` + `Demo Season 2026` + `Demo Work Area North` → rác trong màn hình demo (mùa 2026 xuất hiện lại, khu vực thứ 4).
- Contract impact: Không.
- Migration: Không.
- Verify: `docker compose down -v && build && up -d` → `npm run seed:2025:demo` (mùa 2025 + 3 khu) → restart parish → `work_area_scope`=3, `parish`=2, `work_area`=3; API qua gateway: 1 mùa 2025 COMPLETED, 3 khu READY, 4 task COMPLETED; `GET /parish-inventory/purchases` + `GET /dashboards/season` OK.
- Follow-up: seeder các service khác (Identity/Readiness/…) có thể vẫn seed khi bảng rỗng — nếu cần demo sạch hoàn toàn thì nên áp dụng cùng nguyên tắc "chỉ seed khi DB rỗng".

## 2026-11-26 (rev 4) — merge `main` + clone copies the map pin
- Branch: `feat/backend-workflow-improvements`
- Service: ParishCoordination
- Files: `ParishCoordination.Application/Seasons/Commands/CloneSeason/CloneSeasonCommand.cs`
- Change: `CloneSeason` now copies **`MapX/MapY`** (ghim khu trên sơ đồ) sang khu vực của mùa mới, cùng với `Latitude/Longitude`. Trước đó FE phải ghim lại từng khu sau khi clone (localStorage) — giờ BE clone nguyên tử.
- Contract impact: Không đổi shape. `WorkAreaDto.mapX/mapY` + `mapX/mapY` trong POST/PATCH work-area là tính năng của `main` (migration `20261126040000_AddWorkAreaMapPin`).
- Migration: Không (dùng migration của `main`).
- Tests: `dotnet build GrottoWorks_BE.slnx` 0 warning; toàn bộ unit test xanh — Resource 260, ParishCoordination 334, TaskExecution 405, Identity 164, NotificationAudit 164, Reporting 76, Readiness 52, Platform 12.
- Follow-up (FE đã làm cùng đợt): ghim đọc/ghi qua `WorkArea.mapX/mapY` thay cho localStorage; màn ADMIN bỏ gọi `/dashboards/season` (endpoint nay chỉ cho PARISH); hiển thị thông báo khi lưu sơ đồ trả 503 `FILE_ATTACHMENT_PENDING`.

## 2026-11-26 (rev 3) — parish buys, no cost estimate, area material estimate
- Branch: `feat/backend-workflow-improvements`
- Service: Resource
- Files: `Resource.Domain/Inventory/ParishPurchaseReceipt.cs` (new), `Resource.Domain/Inventory/MaterialTransaction.cs` (`TransactionReferenceType.ParishPurchaseItem`), `Resource.Infrastructure/Data/Configurations/ParishPurchaseConfigurations.cs` (new), `Resource.Infrastructure/Data/ResourceDbContext.cs`, `Resource.Infrastructure/Inventory/ParishPurchaseStore.cs` (new), `Resource.Infrastructure/Inventory/InventoryStore.cs`, `Resource.Infrastructure/DependencyInjection.cs`, `Resource.Application/Inventory/IParishPurchaseStore.cs` (new), `Resource.Application/Inventory/ParishPurchaseHandlers.cs` (new), `Resource.Application/Requirements/RequirementHandlers.cs`, `Resource.Application/DependencyInjection.cs`, `Resource.Api/Requests/ParishPurchaseRequests.cs` (new), `Resource.Api/Endpoints/ParishWarehouseEndpoints.cs`, `Resource.Api/Endpoints/RequirementEndpoints.cs`, `Data/Migrations/20261126150936_ParishPurchaseReceipts*.cs` (+ snapshot), `tests/Resource.UnitTests/Inventory/ParishPurchaseTests.cs` (new), `tests/Resource.UnitTests/Requirements/RequirementTests.cs`, `tests/Resource.UnitTests/Fakes.cs`.
- Change:
  - **Ban phụ trách nhập vật tư đã mua (có hoá đơn):** `POST /api/v1/parish-inventory/purchases` (PARISH, 201) + `GET /api/v1/parish-inventory/purchases?seasonId=&limit=` (PARISH/ADMIN). Body: `seasonId`, `buyerName` (bắt buộc), `supplierName`, `purchaseDate`, `items[{materialId, quantity, unitPrice}]`, `invoices[{id}]` (**bắt buộc**), `note`. Tổng tiền tính ở server, mỗi dòng post `PURCHASE_DELIVERY` vào **ledger kho tổng** (reference `PARISH_PURCHASE_ITEM`), phát `ParishMaterialStockChanged` để các màn khác thấy tồn mới → sau đó phân bổ xuống khu vực.
  - **Ước lượng vật tư của khu vực được bật lại:** `POST /work-areas/{id}/material-requirements` và `PATCH /material-requirements/{id}` trả về nghiệp vụ thật (bỏ `MANUAL_MATERIAL_REQUIREMENT_DISABLED`). Người ghi: **MO của khu** (khi mùa chưa archived) hoặc **PARISH/ADMIN khi mùa còn `PLANNING`** (`WORK_AREA_SCOPE_DENIED` nếu khác). Vật tư phải ACTIVE, mỗi (khu, vật tư) 1 dòng (`REQUIREMENT_EXISTS`), PATCH có optimistic version.
  - **"Cần" = max(ước lượng khu, tổng vật tư task):** `IInventoryStore.GetRequiredTotalsAsync` gộp `material_requirement` (ACTIVE) với `task_material_plan` theo từng (khu, vật tư) và lấy **max** → ước lượng có tác dụng khi chưa có task, task khai nhiều hơn thì theo task, không đếm trùng.
  - **Bỏ ước tính chi phí khỏi yêu cầu bổ sung:** BE giữ cột `estimated_cost` (nullable) để tương thích, nhưng client đã ngừng gửi → `EstimatedTotal = 0`, BR-32 (duyệt vượt dự toán) không còn kích hoạt vì không có dự toán.
- Contract impact: Additive 2 endpoint (`parish-inventory/purchases` GET/POST). `material-requirements` POST/PATCH đổi từ luôn 422 sang nghiệp vụ thật (thay đổi hành vi, không đổi shape).
- Migration: Yes — `20261126150936_ParishPurchaseReceipts` (`parish_purchase_receipt`, `parish_purchase_item`, unique `ux_parish_purchase_item_material`, index theo parish/season/ngày). Apply trước khi start Resource.
- Tests: `dotnet test tests/Resource.UnitTests` → **260/260 pass** (thêm `ParishPurchaseTests`, viết lại `RequirementTests`, `MigrationSnapshotTests` xác nhận snapshot khớp model).
- Follow-up: dashboard/báo cáo tài chính nên cộng thêm "vật tư Ban phụ trách đã mua" (hiện chỉ có self-purchase); FE web đã có dialog nhập + danh sách gần đây ở `/community/warehouse`.

## 2026-11-26
- Branch: `feat/backend-workflow-improvements`
- Service: Resource, TaskExecution, ApiGateway
- Files: `Resource.Domain/Purchases/SelfPurchaseReceipt.cs`, `Resource.Infrastructure/Data/Configurations/SelfPurchaseConfigurations.cs`, `Resource.Application/Inventory/SelfPurchaseHandlers.cs`, `Resource.Api/Requests/EmergencyPurchaseRequests.cs` (new), `Resource.Api/Endpoints/SelfPurchaseEndpoints.cs`, `Resource.Application/DependencyInjection.cs`, `Data/Migrations/20261126142831_EmergencyPurchaseFields.cs` (+ designer + snapshot), `TaskExecution.Application/Assignments/AssignmentService.cs`, `Resource.Application/Purchases/PurchaseHandlers.cs`, `ApiGateway/YarpApiGateway/appsettings.json`, `tests/Resource.UnitTests/Purchases/PurchaseTests.cs`, `tests/Resource.UnitTests/Inventory/SelfPurchaseTests.cs` (new), `tests/Resource.UnitTests/Fakes.cs` (`FakeSelfPurchaseStore`), `tests/TaskExecution.UnitTests/Application/AssignmentTestFixture.cs` (parish buyer), `tests/TaskExecution.UnitTests/Application/TaskRecruitmentTests.cs`, `tests/TaskExecution.UnitTests/Application/AttendanceReviewTests.cs`.
- Change:
  - **Mua khẩn cấp của TNV trong lúc làm việc:** new `POST /api/v1/tasks/{taskId}/emergency-purchases` (VOLUNTEER, 201). The volunteer must be an assignee of the task (`EMERGENCY_PURCHASE_NOT_ASSIGNEE`), the task must be `IN_PROGRESS` (`TASK_INVALID_STATE`), the invoice file(s) are mandatory, and the draft total must be **under 500,000 VND** (`EMERGENCY_PURCHASE_LIMIT`). The receipt is stored with `source = VOLUNTEER_EMERGENCY` and `task_id`, posts the same `PURCHASE_DELIVERY` ledger rows as MO self-purchases and publishes shortage signals.
  - **`self_purchase_receipt` gains `task_id` (nullable, indexed) and `source` (`MO_SELF_PURCHASE` | `VOLUNTEER_EMERGENCY`)** so finance/reporting can separate the two origins; the domain rejects any other source (`SELF_PURCHASE_SOURCE_INVALID`).
  - **Việc đi mua trở lại do Ban phụ trách đảm nhận:** a `PURCHASE` task assignee must be a PARISH user again (`EnsureActiveAsync(..., Parish)`) and the purchase buyer must be PARISH (`PURCHASE_BUYER_NOT_PARISH`) — volunteers no longer receive purchase tasks; large shortages stay MO → supplementary supply request → PARISH.
- Contract impact: Additive endpoint + 2 additive fields on the self-purchase receipt DTO (`taskId`, `source`). `POST /purchase-requests/{id}/purchases` reverts to parish-only buyer (restores the documented rule from before `075ea73`).
- Migration: Yes — `20261126142831_EmergencyPurchaseFields` (`task_id`, `source` default `MO_SELF_PURCHASE`, index `ix_self_purchase_task`). Apply before starting Resource.
- Tests: `SelfPurchaseTests` covers assignee-only, task must be in progress, < 500,000 VND (boundary), mandatory invoice, unknown material and the happy path (ledger + `VOLUNTEER_EMERGENCY`). `TaskRecruitmentTests`/`AttendanceReviewTests` now assert a purchase task cannot be offered to a volunteer (`USER_ROLE_MISMATCH`); the old "first purchase check-in starts the task" case was removed with the volunteer purchase flow. Resource 246 → 251 passing, TaskExecution 405 passing (Docker `dotnet test`).
- Follow-up: FE/mobile read the new `source` field when listing self-purchases (mobile now shows an emergency-purchase screen in the task detail); Reporting still needs an aggregate for volunteer emergency purchases.

## 2026-11-26
- Branch: `feat/backend-workflow-improvements`
- Service: ParishCoordination, Resource
- Files: `ParishCoordination.Application/Seasons/Commands/CloneSeason/CloneSeasonCommand.cs` (new), `ParishCoordination.Api/Requests/CloneSeasonRequest.cs` (new), `ParishCoordination.Api/Endpoints/SeasonEndpoints.cs`, `ParishCoordination.Application/DependencyInjection.cs`, `Resource.Application/Purchases/PurchaseRequestHandlers.cs`, `docs/BE-CHANGELOG.md`.
- Change:
  - **`POST /api/v1/seasons/{sourceSeasonId}/clone`** (ADMIN/PARISH, 1 transaction): creates a PLANNING season and copies the selected work areas with name, description, category, coordinates, optionally dates shifted by the season window (`SAME_DAY_OF_SEASON`) and leader/MO/community (`copyAssignments`). Returns `{ season, areas, warnings }` so the FE can seed its cache and copy its map pins by area id. New season status is always PLANNING and readiness/checklist is not copied.
  - **Supplementary-supply requests only for real shortages**: `PurchaseShortageGuard` now rejects any line whose material has no shortage in the work area (`PURCHASE_NO_SHORTAGE`) — the manual override cannot bypass the rule.
- Contract impact: Additive endpoint `POST /seasons/{id}/clone` (shape already specified in `GrottoWorks_FE/docs/specs/2026-11-03-clone-season-and-parish-map.md`). `createPurchaseRequest` now enforces an existing business rule (no new fields).
- Migration: No.
- Follow-up: FE clone dialog copies map pins client-side right after the response; when map pins become server-side (`work_area.map_x/map_y`) the clone handler should copy them in the same transaction.

## 2026-11-26
- Branch: `feat/backend-workflow-improvements`
- Service: Resource, TaskExecution, ApiGateway
- Files: `Resource.Domain/Purchases/SelfPurchaseReceipt.cs` (new), `Resource.Infrastructure/Data/Configurations/SelfPurchaseConfigurations.cs` (new), `Resource.Infrastructure/Inventory/SelfPurchaseStore.cs` (new), `Resource.Application/Inventory/ISelfPurchaseStore.cs` + `SelfPurchaseHandlers.cs` (new), `Resource.Api/Requests/SelfPurchaseRequests.cs` + `Endpoints/SelfPurchaseEndpoints.cs` (new), `Resource.Domain/Inventory/MaterialTransaction.cs`, `Resource.Domain/Purchases/Purchase.cs`, `Resource.Infrastructure/Data/ResourceDbContext.cs`, DI + `ResourceEndpoints.cs`, `Data/Migrations/20261126131758_SelfPurchaseReceipts.cs` (+ snapshot), `TaskExecution.Application/Users/IIdentityUserLookup.cs`, `TaskExecution.Application/Assignments/AssignmentService.cs`, `Resource.Application/Purchases/PurchaseHandlers.cs`, `ApiGateway/YarpApiGateway/appsettings.json`, unit tests (`PurchaseTests`, `ShortageTests`, `DonationTests`).
- Change:
  - **Vật tư người khác tự mua (ngoài luồng purchase request):** new tables `self_purchase_receipt` + `self_purchase_item`; MO of the work area records buyer name, supplier, date, lines and **mandatory invoice photos**; each line posts a `PURCHASE_DELIVERY` ledger row referenced by `SELF_PURCHASE_ITEM` so stock and cost stay linked (`POST /api/v1/work-areas/{workAreaId}/self-purchases`, `GET /api/v1/self-purchases`).
  - **Mua trong lúc thực hiện task:** a `PURCHASE` task may again be carried by a VOLUNTEER as well as a PARISH user (`EnsureActiveAnyAsync`, `TaskScopeAccess.GetWorkAreaForAssigneeWriteAsync`, buyer resolution accepts both roles).
  - **Chứng từ bắt buộc:** `Purchase.ConfirmDelivery` now rejects a delivery with no receipt file (`PURCHASE_RECEIPT_REQUIRED`).
- Contract impact: Additive (2 new endpoints + new tables). `confirmPurchaseDelivery` now enforces an existing documented rule; buy-task assignment accepts VOLUNTEER again (additive for callers).
- Migration: Yes — `20261126131758_SelfPurchaseReceipts` (2 tables, 4 indexes).
- Tests: Resource 246 pass, TaskExecution 405 pass (via `mcr.microsoft.com/dotnet/sdk:10.0` container). Two stale tests updated: manual requirement handlers are disabled (shortage test now drives the aggregate) and BR-37 material-donation-without-area now requires PARISH.
- Follow-up: Apply the migration, rebuild/restart Resource + TaskExecution + gateway, extend Reporting read models with self-purchase lines (finance report currently sums via the list API), and update `docs/api-contract-parish-warehouse.md` when the contract is published.

## 2026-11-26
- Branch: `feat/backend-workflow-improvements`
- Service: Identity, Reporting, Resource, TaskExecution, NotificationAudit, ApiGateway
- Files: `Services/Identity/*`, `Services/Reporting/*`, `Services/Resource/*`, `Services/TaskExecution/*`, `Services/NotificationAudit/*`, `ApiGateway/YarpApiGateway/appsettings.json`, unit tests, and parish warehouse docs
- Change: Completed pending workflow and reporting improvements, added parish warehouse inventory/distribution APIs and read models, expanded identity scope handling, and aligned purchase/donation/task notification behavior.
- Contract impact: Additive parish warehouse endpoints and scope/read-model data; existing contracts remain backward-compatible.
- Migration: Yes. Reporting and Resource database snapshots/migrations add parish warehouse and read-model structures.
- Follow-up: Build and run unit tests, apply reporting/resource migrations, restart affected services, then verify warehouse, purchase, donation, and notification flows through the API gateway.

## 2026-11-26
- Branch: `main`
- Service: ParishCoordination, NotificationAudit
- Files: `Services/ParishCoordination/ParishCoordination.Domain/Entities/Parish.cs`, `ParishCoordination.Application/Parishes/*`, `ParishCoordination.Infrastructure/Data/Configurations/ParishConfiguration.cs`, `ParishCoordination.Infrastructure/Data/Migrations/20261126020000_AddParishMapImage.cs`, `ParishCoordination.Infrastructure/Files/ParishFileAttachmentClient.cs`, `ParishCoordination.Api/Endpoints/ParishEndpoints.cs`, `Services/NotificationAudit/NotificationAudit.Application/Files/FileUpload.cs`, `NotificationAudit.Application/Files/FileAttachmentHandler.cs`, `GrottoWorks_FE/src/features/map/api.ts`
- Change: Made parish map metadata server-backed per parish, added MapImage attachment/finalization against the parish record, and allowed same-parish/ADMIN download of attached map images.
- Contract impact: Additive `GET/PUT /api/v1/parishes/{parishId}/map`; existing parish DTO gains optional `mapImageFileId` and `mapImageFileName` fields.
- Migration: Yes. Adds `map_image_file_id` and `map_image_file_name` to `parish`.
- Follow-up: Build/restart ParishCoordination and NotificationAudit, re-upload the map, and verify the parish account can resolve the download URL. Existing pending upload records can be cleaned after migration.

## 2026-11-26
- Branch: `main`
- Service: NotificationAudit, ApiGateway
- Files: `Services/NotificationAudit/NotificationAudit.Application/DependencyInjection.cs`, `NotificationAudit.Application/Files/FileUpload.cs`, `NotificationAudit.Infrastructure/Files/S3FileObjectStorage.cs`, `NotificationAudit.Api/Controllers/FilesController.cs`, `NotificationAudit.Api/Endpoints/NotificationAuditEndpoints.cs`, `NotificationAudit.Api/Program.cs`
- Change: Added same-origin server-side R2 proxy upload flow for MapImage/DesignAsset; browser no longer performs direct presigned PUT, and pending objects are uploaded, copied, inspected, and completed through NotificationAudit.
- Contract impact: Additive `PUT /api/v1/files/{fileId}/proxy-upload`; existing presigned upload contract remains available.
- Migration: No.
- Follow-up: Retain R2 CORS for direct client flows still used elsewhere until those flows are migrated; remove failed pending upload records/objects through cleanup jobs.

## 2026-11-26
- Branch: `main`
- Service: ApiGateway, NotificationAudit, frontend map
- Files: `ApiGateway/YarpApiGateway/appsettings.Development.json`, `docker-compose.override.yml`, `docs/ops/cloudflare-r2.md`, `GrottoWorks_FE/src/features/map/pages/ParishMapPage.tsx`, locale files
- Change: Added 5175 local origins to gateway/MinIO/R2 CORS documentation and moved the hidden file input outside embedded-header mode so the admin map upload button opens the file picker.
- Contract impact: None.
- Migration: No.
- Follow-up: Apply both 5175 origins to the Cloudflare R2 bucket CORS policy, then re-test direct browser upload.

## 2026-11-26
- Branch: `main`
- Service: Reporting, NotificationAudit/Frontend map handling
- Files: `Services/Reporting/Reporting.Api/Endpoints/ReportEndpoints.cs`, `Services/Reporting/Reporting.Application/Reports/DashboardHandlers.cs`, `GrottoWorks_FE/src/features/dashboards/api.ts`, `GrottoWorks_FE/src/features/dashboards/ParishOverview.tsx`, `GrottoWorks_FE/src/features/map/localStore.ts`, `GrottoWorks_FE/src/features/map/pages/ParishMapPage.tsx`, `GrottoWorks_FE/src/features/areas/pages/AreaFormDialog.tsx`
- Change: Added ADMIN access and explicit parish scope to season dashboards; stopped legacy/non-GUID map keys from calling the file download endpoint and added orphan map cleanup.
- Contract impact: Additive dashboard query parameter `parishId`; season dashboard now accepts ADMIN and PARISH. No existing request shape is removed.
- Migration: No.
- Follow-up: Restart Reporting and frontend dev server, then re-login and verify the 403/404 logs disappear.

## 2026-11-26
- Branch: `main`
- Service: ApiGateway, NotificationAudit, local object storage
- Files: `ApiGateway/YarpApiGateway/appsettings.Development.json`, `docker-compose.override.yml`, `docs/ops/cloudflare-r2.md`
- Change: Added `http://127.0.0.1:5173` to local gateway and MinIO CORS origins, and documented the same origin for Cloudflare R2 bucket CORS.
- Contract impact: None; local browser-origin configuration only.
- Migration: No.
- Follow-up: Apply the R2 CORS policy in the Cloudflare dashboard and verify a direct browser upload.

## 2026-11-26
- Branch: `main`
- Service: NotificationAudit
- Files: `Services/NotificationAudit/NotificationAudit.Contracts/Files/FileContracts.cs`, `Services/NotificationAudit/NotificationAudit.Domain/Files/FileObject.cs`, `Services/NotificationAudit/NotificationAudit.Application/Files/FileUpload.cs`
- Change: Added `MapImage` and `DesignAsset` upload purposes for Cloudflare R2, with purpose-specific MIME/size validation.
- Contract impact: Additive enum values `MAP_IMAGE`, `DESIGN_ASSET`; existing upload contract unchanged.
- Migration: No.
- Follow-up: Configure R2 CORS on the bucket and rotate the supplied R2 API token into a secret before production.

## 2026-11-26
- Branch: `main`
- Service: Resource
- Files: `Services/Resource/Resource.Infrastructure/Requirements/MaterialRequirementStore.cs`, `Services/Resource/Resource.Infrastructure/Inventory/InventoryStore.cs`, `Services/Resource/Resource.Application/Requirements/RequirementHandlers.cs`, `tests/Resource.UnitTests/Requirements/RequirementTests.cs`
- Change: Task material plans now derive work-area material requirements and shortage totals; manual create/update requirement handlers are disabled.
- Contract impact: Additive enforcement: manual POST/PATCH requirement writes return `MANUAL_MATERIAL_REQUIREMENT_DISABLED` (422); GET requirement/shortage values now aggregate real task materials.
- Migration: No.
- Follow-up: Rebuild and restart Resource API/gateway; ensure task material change events have been consumed into `task_material_plan` before validating shortages.

## 2026-11-26
- Branch: `main`
- Service: Resource
- Files: `Services/Resource/Resource.Domain/Inventory/ParishMaterialTransaction.cs`
- Change: Qualify the `Inventory.Quantity` helper to avoid collision with the entity's `Quantity` property and unblock Resource API compilation.
- Contract impact: None.
- Migration: No.
- Follow-up: Rebuild and restart `resource.api` and `yarpapigateway` so parish warehouse routes are available in local development.