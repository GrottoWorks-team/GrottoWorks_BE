namespace Reporting.Domain.ReadModels;

/// <summary>
/// <c>rm_parish_material_line</c> (read model, F-RPT-04/05, 26/11/2026): one material of the parish central warehouse for
/// one season, kept from <c>parish.material.stock.changed</c>. Only the confirmed balance is known here — required /
/// shortage belong to a work area. Never a source of truth for stock (Resource owns the parish ledger).
/// </summary>
public sealed class ParishMaterialLine
{
    private ParishMaterialLine()
    {
    }

    public Guid ParishId { get; private set; }

    public Guid SeasonId { get; private set; }

    public Guid MaterialId { get; private set; }

    public string? MaterialCode { get; private set; }

    public string? MaterialName { get; private set; }

    public string? Unit { get; private set; }

    public decimal ConfirmedAvailable { get; private set; }

    /// <summary>Optimistic concurrency token (<c>xmin</c>): concurrent events on the same row → one is redelivered.</summary>
    public uint Version { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static ParishMaterialLine Create(Guid parishId, Guid seasonId, Guid materialId) =>
        new() { ParishId = parishId, SeasonId = seasonId, MaterialId = materialId };

    public void SetBalance(decimal confirmedAvailable, string? code, string? name, string? unit, DateTimeOffset at)
    {
        ConfirmedAvailable = confirmedAvailable;
        MaterialCode = string.IsNullOrWhiteSpace(code) ? MaterialCode : code;
        MaterialName = string.IsNullOrWhiteSpace(name) ? MaterialName : name;
        Unit = string.IsNullOrWhiteSpace(unit) ? Unit : unit;
        var utc = at.ToUniversalTime();
        UpdatedAt = utc > UpdatedAt ? utc : UpdatedAt;
    }
}
