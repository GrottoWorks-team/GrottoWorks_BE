namespace Resource.Contracts.Events;

/// <summary>
/// <c>parish.material.stock.changed</c> — publisher Resource, consumer Reporting. Raised after every parish
/// warehouse movement so Reporting can label and total the central store without calling Resource.
/// </summary>
public sealed record ParishMaterialStockChanged(
    Guid ParishId,
    Guid SeasonId,
    Guid MaterialId,
    decimal ConfirmedAvailable,
    DateTimeOffset OccurredAt,
    string MaterialCode,
    string MaterialName,
    string Unit)
{
    public const string EventType = "parish.material.stock.changed";

    public const int Version = 1;
}

/// <summary><c>parish.distribution.issued</c> — the target work-area officer has stock to receive.</summary>
public sealed record ParishDistributionIssued(
    Guid ParishDistributionId,
    Guid ParishId,
    Guid SeasonId,
    Guid TargetWorkAreaId,
    Guid? TargetMaterialOfficerUserId,
    int LineCount,
    Guid IssuedByUserId,
    DateTimeOffset IssuedAt)
{
    public const string EventType = "parish.distribution.issued";

    public const int Version = 1;
}

/// <summary><c>parish.distribution.received</c> — the officer confirmed what arrived.</summary>
public sealed record ParishDistributionReceived(
    Guid ParishDistributionId,
    Guid ParishId,
    Guid SeasonId,
    Guid TargetWorkAreaId,
    Guid ConfirmedByUserId,
    Guid IssuedByUserId,
    decimal ReceivedUsableQuantity,
    decimal ReceivedUnusableQuantity,
    DateTimeOffset ReceivedAt)
{
    public const string EventType = "parish.distribution.received";

    public const int Version = 1;
}
