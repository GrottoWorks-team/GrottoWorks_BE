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
    public const int MaxSpecificationLength = 300;
    public const int MaxSupplierNameLength = 150;

    private Material()
    {
    }

    public static Material Create(
        MaterialCategory category,
        string code,
        string name,
        string unit,
        string? description,
        string? specification = null,
        string? supplierName = null,
        decimal? referenceUnitPrice = null,
        decimal? minimumStock = null)
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
            Specification = OptionalText(specification, MaxSpecificationLength, "MATERIAL_SPECIFICATION_INVALID", "Specification"),
            SupplierName = OptionalText(supplierName, MaxSupplierNameLength, "MATERIAL_SUPPLIER_INVALID", "Supplier"),
            ReferenceUnitPrice = NonNegative(referenceUnitPrice, "MATERIAL_REFERENCE_PRICE_INVALID", "Reference unit price"),
            MinimumStock = NonNegative(minimumStock, "MATERIAL_MINIMUM_STOCK_INVALID", "Minimum stock"),
            Status = CatalogStatus.Active
        };
    }

    private static string? OptionalText(string? value, int maxLength, string errorCode, string field)
    {
        var trimmed = CatalogText.Optional(value);
        if (trimmed is not null && trimmed.Length > maxLength)
        {
            throw DomainRuleException.Validation(
                errorCode,
                $"{field} must not exceed {maxLength} characters.");
        }

        return trimmed;
    }

    private static decimal? NonNegative(decimal? value, string errorCode, string field)
    {
        if (value is < 0)
        {
            throw DomainRuleException.Validation(errorCode, $"{field} must not be negative.");
        }

        return value;
    }

    public static string NormalizeCode(string? code) =>
        CatalogText.Code(code, MaxCodeLength, "MATERIAL_CODE_INVALID");

    public Guid MaterialId { get; private set; }

    public Guid MaterialCategoryId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string Unit { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>Detail shown to the buying team, e.g. "nhựa PP, 1m8 x 0.8m, chân inox".</summary>
    public string? Specification { get; private set; }

    /// <summary>Preferred supplier, free text — no supplier aggregate exists yet.</summary>
    public string? SupplierName { get; private set; }

    /// <summary>Planning price per unit in VND, used to estimate purchase cost.</summary>
    public decimal? ReferenceUnitPrice { get; private set; }

    /// <summary>Stock level that should trigger a re-purchase suggestion.</summary>
    public decimal? MinimumStock { get; private set; }

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
    public void Update(
        string? name,
        string? unit,
        string? description,
        CatalogStatus? status,
        bool isReferenced,
        string? specification = null,
        string? supplierName = null,
        decimal? referenceUnitPrice = null,
        decimal? minimumStock = null)
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

        if (specification is not null)
        {
            Specification = OptionalText(specification, MaxSpecificationLength, "MATERIAL_SPECIFICATION_INVALID", "Specification");
        }

        if (supplierName is not null)
        {
            SupplierName = OptionalText(supplierName, MaxSupplierNameLength, "MATERIAL_SUPPLIER_INVALID", "Supplier");
        }

        if (referenceUnitPrice is not null)
        {
            ReferenceUnitPrice = NonNegative(referenceUnitPrice, "MATERIAL_REFERENCE_PRICE_INVALID", "Reference unit price");
        }

        if (minimumStock is not null)
        {
            MinimumStock = NonNegative(minimumStock, "MATERIAL_MINIMUM_STOCK_INVALID", "Minimum stock");
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
