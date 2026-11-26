# BE Changelog

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