namespace Resource.Contracts.Events;

/// <summary>
/// <c>material.shortage.changed</c> (API Contract v1.3 §8, spec §6.1) — publisher Resource (Vũ),
/// consumer Reporting. Raised when the calculated shortage of one material in one work area changes
/// (requirement created/changed/cancelled or a stock movement). Quantities are in <see cref="Unit"/>; code, name and unit
/// let Reporting label its material read-model without calling Resource.
/// </summary>
public sealed record MaterialShortageChanged(
    Guid WorkAreaId,
    Guid SeasonId,
    Guid ParishId,
    Guid MaterialId,
    decimal PreviousShortage,
    decimal Shortage,
    decimal RequiredQuantity,
    decimal ConfirmedAvailable,
    DateTimeOffset OccurredAt,
    string MaterialCode,
    string MaterialName,
    string Unit)
{
    public const string EventType = "material.shortage.changed";

    public const int Version = 1;
}
