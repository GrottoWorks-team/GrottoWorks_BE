namespace Resource.Domain.Inventory;

public enum RequirementStatus
{
    Active,

    /// <summary>No longer needed; kept for history and excluded from shortage (records are not deleted).</summary>
    Cancelled
}

/// <summary>
/// <c>material_requirement</c> (ERD v3.6, F-RES-03): how much of a material a work area needs.
/// Unique per (work area, material); <c>existing_quantity</c> is display-only — shortage comes from the ledger.
/// </summary>
public sealed class MaterialRequirement
{
    private MaterialRequirement()
    {
    }

    public static MaterialRequirement Create(
        Guid workAreaId,
        Guid materialId,
        decimal requiredQuantity,
        decimal existingQuantity,
        string? note,
        DateTimeOffset now)
    {
        return new MaterialRequirement
        {
            MaterialRequirementId = Guid.NewGuid(),
            WorkAreaId = workAreaId,
            MaterialId = materialId,
            RequiredQuantity = Quantity.Positive(requiredQuantity, "REQUIREMENT_QUANTITY_INVALID", "Required quantity"),
            ExistingQuantity = NonNegative(existingQuantity),
            Status = RequirementStatus.Active,
            Note = Clean(note),
            CreatedAt = now.ToUniversalTime()
        };
    }

    public Guid MaterialRequirementId { get; private set; }

    public Guid WorkAreaId { get; private set; }

    public Guid MaterialId { get; private set; }

    public decimal RequiredQuantity { get; private set; }

    /// <summary>Informational only (ERD note) — never used in shortage.</summary>
    public decimal ExistingQuantity { get; private set; }

    public RequirementStatus Status { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token mapped to PostgreSQL <c>xmin</c>.</summary>
    public uint Version { get; private set; }

    public void EnsureVersion(long? expected)
    {
        if (expected is { } value && value != Version)
        {
            throw new DomainRuleException(
                "REQUIREMENT_VERSION_MISMATCH",
                "The requirement was changed by someone else. Reload it and try again.",
                DomainErrorKind.PreconditionFailed);
        }
    }

    /// <summary>PATCH semantics: <c>null</c> = unchanged; an empty note clears it.</summary>
    public void Update(decimal? requiredQuantity, decimal? existingQuantity, string? note, RequirementStatus? status, DateTimeOffset now)
    {
        if (requiredQuantity is { } quantity)
        {
            RequiredQuantity = Quantity.Positive(quantity, "REQUIREMENT_QUANTITY_INVALID", "Required quantity");
        }

        if (existingQuantity is { } existing)
        {
            ExistingQuantity = NonNegative(existing);
        }

        if (note is not null)
        {
            Note = Clean(note);
        }

        if (status is { } newStatus)
        {
            Status = newStatus;
        }

        UpdatedAt = now.ToUniversalTime();
    }

    private static decimal NonNegative(decimal value)
    {
        if (value < 0 || value > Quantity.Max || decimal.Round(value, 2) != value)
        {
            throw DomainRuleException.Validation(
                "REQUIREMENT_EXISTING_QUANTITY_INVALID",
                "Existing quantity must be between 0 and the maximum with at most 2 decimals.");
        }

        return value;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
