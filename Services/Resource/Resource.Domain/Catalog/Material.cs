namespace Resource.Domain.Catalog;

/// <summary>
/// <c>material</c> (ERD v3.6): global catalog entry. The unit of measure lives here — quantities everywhere
/// else (requirements, ledger, allocations) are expressed in this unit (spec F-RES-02).
/// </summary>
public sealed class Material
{
    public const int MaxCodeLength = 50;
    public const int MaxNameLength = 150;
    public const int MaxUnitLength = 30;

    private Material()
    {
    }

    public static Material Create(MaterialCategory category, string code, string name, string unit, string? description)
    {
        ArgumentNullException.ThrowIfNull(category);
        EnsureCategoryUsable(category);

        return new Material
        {
            MaterialId = Guid.NewGuid(),
            MaterialCategoryId = category.MaterialCategoryId,
            Code = NormalizeCode(code),
            Name = CatalogText.Required(name, MaxNameLength, "MATERIAL_NAME_INVALID", "Name"),
            Unit = CatalogText.Required(unit, MaxUnitLength, "MATERIAL_UNIT_INVALID", "Unit"),
            Description = CatalogText.Optional(description),
            Status = CatalogStatus.Active
        };
    }

    public static string NormalizeCode(string? code) =>
        CatalogText.Code(code, MaxCodeLength, "MATERIAL_CODE_INVALID");

    public Guid MaterialId { get; private set; }

    public Guid MaterialCategoryId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string Unit { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public CatalogStatus Status { get; private set; }

    public void MoveToCategory(MaterialCategory category)
    {
        ArgumentNullException.ThrowIfNull(category);
        if (category.MaterialCategoryId == MaterialCategoryId)
        {
            return;
        }

        EnsureCategoryUsable(category);
        MaterialCategoryId = category.MaterialCategoryId;
    }

    /// <summary>
    /// Changing the unit after quantities were recorded would silently change their meaning, so the caller
    /// passes whether the material is already referenced by requirements or the stock ledger.
    /// </summary>
    public void Update(string? name, string? unit, string? description, CatalogStatus? status, bool isReferenced)
    {
        if (name is not null)
        {
            Name = CatalogText.Required(name, MaxNameLength, "MATERIAL_NAME_INVALID", "Name");
        }

        if (unit is not null)
        {
            var normalizedUnit = CatalogText.Required(unit, MaxUnitLength, "MATERIAL_UNIT_INVALID", "Unit");
            if (!string.Equals(normalizedUnit, Unit, StringComparison.Ordinal))
            {
                if (isReferenced)
                {
                    throw new DomainRuleException(
                        "MATERIAL_UNIT_LOCKED",
                        "The unit cannot change once quantities were recorded for this material.");
                }

                Unit = normalizedUnit;
            }
        }

        if (description is not null)
        {
            Description = CatalogText.Optional(description);
        }

        if (status is not null)
        {
            Status = status.Value;
        }
    }

    private static void EnsureCategoryUsable(MaterialCategory category)
    {
        if (category.Status != CatalogStatus.Active)
        {
            throw DomainRuleException.Validation(
                "MATERIAL_CATEGORY_INACTIVE",
                "Materials can only be added to an active category.");
        }
    }
}
