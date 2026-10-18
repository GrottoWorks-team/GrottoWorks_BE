namespace Resource.Domain.Catalog;

/// <summary>Catalog entries referenced by business data are deactivated, never deleted (BA 15).</summary>
public enum CatalogStatus
{
    Active,
    Inactive
}
