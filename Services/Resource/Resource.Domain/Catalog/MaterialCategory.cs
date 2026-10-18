namespace Resource.Domain.Catalog;

/// <summary><c>material_category</c> (ERD v3.6): global catalog, unique code.</summary>
public sealed class MaterialCategory
{
    public const int MaxCodeLength = 50;
    public const int MaxNameLength = 100;

    private MaterialCategory()
    {
    }

    public static MaterialCategory Create(string code, string name, string? description)
    {
        return new MaterialCategory
        {
            MaterialCategoryId = Guid.NewGuid(),
            Code = NormalizeCode(code),
            Name = CatalogText.Required(name, MaxNameLength, "MATERIAL_CATEGORY_NAME_INVALID", "Name"),
            Description = CatalogText.Optional(description),
            Status = CatalogStatus.Active
        };
    }

    public static string NormalizeCode(string? code) =>
        CatalogText.Code(code, MaxCodeLength, "MATERIAL_CATEGORY_CODE_INVALID");

    public Guid MaterialCategoryId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public CatalogStatus Status { get; private set; }

    /// <summary>PATCH semantics: <c>null</c> = unchanged; an empty description clears it.</summary>
    public void Update(string? name, string? description, CatalogStatus? status)
    {
        if (name is not null)
        {
            Name = CatalogText.Required(name, MaxNameLength, "MATERIAL_CATEGORY_NAME_INVALID", "Name");
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
}
