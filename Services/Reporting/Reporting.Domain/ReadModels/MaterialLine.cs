namespace Reporting.Domain.ReadModels;

/// <summary>
/// <c>rm_material_line</c> (read model, F-RPT-04/05): one material of one work area. Required / available / shortage come from
/// <c>material.shortage.changed</c> (as of the last shortage move); pledged, donated and purchased quantities accumulate from
/// <c>contribution.*</c> and <c>purchase.delivery.confirmed</c>. Never a source of truth for stock (Resource owns the ledger).
/// </summary>
public sealed class MaterialLine
{
    private MaterialLine()
    {
    }

    public Guid WorkAreaId { get; private set; }

    public Guid MaterialId { get; private set; }

    public Guid SeasonId { get; private set; }

    public Guid ParishId { get; private set; }

    public string? MaterialCode { get; private set; }

    public string? MaterialName { get; private set; }

    public string? Unit { get; private set; }

    public decimal Required { get; private set; }

    public decimal ConfirmedAvailable { get; private set; }

    public decimal Shortage { get; private set; }

    public decimal Pledged { get; private set; }

    public decimal DonatedUsable { get; private set; }

    public decimal DonatedUnusable { get; private set; }

    public decimal PurchasedUsable { get; private set; }

    public decimal PurchasedUnusable { get; private set; }

    /// <summary>Optimistic concurrency token (<c>xmin</c>): concurrent events on the same row → one is redelivered.</summary>
    public uint Version { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static MaterialLine Create(Guid workAreaId, Guid materialId, Guid seasonId, Guid parishId) =>
        new() { WorkAreaId = workAreaId, MaterialId = materialId, SeasonId = seasonId, ParishId = parishId };

    public void SetBalance(decimal required, decimal confirmedAvailable, decimal shortage, string? code, string? name, string? unit, DateTimeOffset at)
    {
        Required = required;
        ConfirmedAvailable = confirmedAvailable;
        Shortage = Math.Max(0, shortage);
        MaterialCode = string.IsNullOrWhiteSpace(code) ? MaterialCode : code;
        MaterialName = string.IsNullOrWhiteSpace(name) ? MaterialName : name;
        Unit = string.IsNullOrWhiteSpace(unit) ? Unit : unit;
        Touch(at);
    }

    public void AddPledged(decimal quantity, DateTimeOffset at)
    {
        Pledged += quantity;
        Touch(at);
    }

    public void AddDonated(decimal usable, decimal unusable, DateTimeOffset at)
    {
        DonatedUsable += usable;
        DonatedUnusable += unusable;
        Touch(at);
    }

    public void AddPurchased(decimal usable, decimal unusable, DateTimeOffset at)
    {
        PurchasedUsable += usable;
        PurchasedUnusable += unusable;
        Touch(at);
    }

    private void Touch(DateTimeOffset at) => UpdatedAt = at.ToUniversalTime() > UpdatedAt ? at.ToUniversalTime() : UpdatedAt;
}
