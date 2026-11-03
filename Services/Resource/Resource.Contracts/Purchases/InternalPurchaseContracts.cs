namespace Resource.Contracts.Purchases;

public sealed record InternalPurchaseRequestLine(Guid PurchaseRequestItemId, Guid MaterialId, decimal RequestedQuantity, decimal? EstimatedCost);
public sealed record InternalPurchaseDelivery(Guid PurchaseId, Guid TaskId, string Status, DateTimeOffset? ConfirmedAt, long Version,
    Guid? ConfirmedByUserId=null,Guid? BuyerUserId=null);
public sealed record InternalPurchaseRequestSnapshot(Guid Id, Guid PurchaseRequestId, Guid WorkAreaId, Guid SeasonId, Guid ParishId,
    string Status, long Version, Guid? ApprovedByUserId, DateTimeOffset? ApprovedAt,
    IReadOnlyList<InternalPurchaseRequestLine> Items, IReadOnlyList<InternalPurchaseDelivery> Purchases);
public sealed record InternalPurchaseRequestPage(IReadOnlyList<InternalPurchaseRequestSnapshot> Items, Guid? NextAfterId);
public sealed record InternalTaskMaterialLine(Guid MaterialId, decimal RequiredQuantity, decimal AllocatedQuantity,
    decimal? UsedQuantity, decimal? ReturnedQuantity, decimal? DamagedQuantity, decimal? LostQuantity);
public sealed record InternalTaskMaterialSummary(Guid TaskId, Guid WorkAreaId, long? RequirementVersion,
    string? ReportStatus, IReadOnlyList<InternalTaskMaterialLine> Materials);
