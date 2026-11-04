namespace Resource.Contracts.Inventory;

public sealed record SupportTransferOperationResult(Guid OperationId, Guid SupportResponseMaterialId,
    Guid SourceWorkAreaId, Guid TargetWorkAreaId, Guid MaterialId, decimal Quantity, Guid ApprovedByUserId,
    string Status, string? ErrorCode, DateTimeOffset DecidedAt);
