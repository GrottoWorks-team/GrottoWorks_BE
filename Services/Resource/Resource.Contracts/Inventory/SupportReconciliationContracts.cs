namespace Resource.Contracts.Inventory;

public sealed record SupportTransferSnapshot(Guid SupportResponseMaterialId,Guid SourceWorkAreaId,Guid TargetWorkAreaId,
    Guid ParishId,Guid SeasonId,Guid MaterialId,decimal Quantity,string Status,Guid ApprovedByUserId,DateTimeOffset TransferredAt,
    decimal? ReceivedUsableQuantity,decimal? ReceivedUnusableQuantity,Guid? ReceivedConfirmedByUserId,DateTimeOffset? ReceivedConfirmedAt);

public sealed record AvailableSupportMaterial(Guid WorkAreaId,Guid ParishId,Guid SeasonId,Guid? MaterialOfficerUserId,
    long ScopeVersion,Guid MaterialId,string MaterialStatus,decimal UnallocatedQuantity,DateTimeOffset CalculatedAt);
