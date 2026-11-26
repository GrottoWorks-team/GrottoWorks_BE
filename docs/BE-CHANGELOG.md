# BE Changelog

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