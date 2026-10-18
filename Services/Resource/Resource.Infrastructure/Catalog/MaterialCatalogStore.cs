using Microsoft.EntityFrameworkCore;
using Resource.Application;
using Resource.Application.Catalog;
using Resource.Domain;
using Resource.Domain.Catalog;
using Resource.Domain.Purchases;
using Resource.Infrastructure.Data;
using Resource.Infrastructure.Data.Configurations;

namespace Resource.Infrastructure.Catalog;

public sealed class MaterialCatalogStore(ResourceDbContext dbContext) : IMaterialCatalogStore
{
    public async Task<IReadOnlyList<MaterialCategory>> ListCategoriesAsync(
        CatalogStatus? status,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.MaterialCategories.AsNoTracking();
        if (status is { } filter)
        {
            query = query.Where(category => category.Status == filter);
        }

        return await query.OrderBy(category => category.Name).ThenBy(category => category.Code).ToListAsync(cancellationToken);
    }

    public Task<MaterialCategory?> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
        dbContext.MaterialCategories.FirstOrDefaultAsync(category => category.MaterialCategoryId == categoryId, cancellationToken);

    public Task<bool> CategoryCodeExistsAsync(string normalizedCode, CancellationToken cancellationToken = default) =>
        dbContext.MaterialCategories.AnyAsync(category => category.Code == normalizedCode, cancellationToken);

    public Task<bool> CategoryHasActiveMaterialsAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
        dbContext.Materials.AnyAsync(
            material => material.MaterialCategoryId == categoryId && material.Status == CatalogStatus.Active,
            cancellationToken);

    public void AddCategory(MaterialCategory category) => dbContext.MaterialCategories.Add(category);

    public async Task<PagedResult<Material>> SearchMaterialsAsync(
        MaterialSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Materials.AsNoTracking();

        if (criteria.CategoryId is { } categoryId)
        {
            query = query.Where(material => material.MaterialCategoryId == categoryId);
        }

        if (criteria.Status is { } status)
        {
            query = query.Where(material => material.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            var pattern = "%" + LikePattern.Escape(criteria.Search.Trim()) + "%";
            query = query.Where(material =>
                EF.Functions.ILike(material.Name, pattern, "\\") ||
                EF.Functions.ILike(material.Code, pattern, "\\"));
        }

        var total = await query.LongCountAsync(cancellationToken);

        var ordered = (criteria.SortField, criteria.SortDescending) switch
        {
            (MaterialSortField.Code, false) => query.OrderBy(material => material.Code),
            (MaterialSortField.Code, true) => query.OrderByDescending(material => material.Code),
            (_, false) => query.OrderBy(material => material.Name),
            (_, true) => query.OrderByDescending(material => material.Name)
        };

        var items = await ordered
            .ThenBy(material => material.MaterialId)
            .Skip((criteria.Page - 1) * criteria.Size)
            .Take(criteria.Size)
            .ToListAsync(cancellationToken);

        return new PagedResult<Material>(items, total);
    }

    public Task<Material?> GetMaterialAsync(Guid materialId, CancellationToken cancellationToken = default) =>
        dbContext.Materials.FirstOrDefaultAsync(material => material.MaterialId == materialId, cancellationToken);

    public async Task<IReadOnlyList<Material>> GetMaterialsAsync(
        IReadOnlyCollection<Guid> materialIds,
        CancellationToken cancellationToken = default)
    {
        if (materialIds.Count == 0)
        {
            return [];
        }

        return await dbContext.Materials.AsNoTracking()
            .Where(material => materialIds.Contains(material.MaterialId))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> MaterialCodeExistsAsync(string normalizedCode, CancellationToken cancellationToken = default) =>
        dbContext.Materials.AnyAsync(material => material.Code == normalizedCode, cancellationToken);

    public async Task<bool> IsMaterialReferencedAsync(Guid materialId, CancellationToken cancellationToken = default) =>
        await dbContext.MaterialRequirements.AnyAsync(requirement => requirement.MaterialId == materialId, cancellationToken) ||
        await dbContext.MaterialTransactions.AnyAsync(transaction => transaction.MaterialId == materialId, cancellationToken) ||
        await dbContext.Set<PurchaseRequestItem>().AnyAsync(item => item.MaterialId == materialId, cancellationToken);

    public void AddMaterial(Material material) => dbContext.Materials.Add(material);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation(MaterialCategoryConfiguration.CodeIndex))
        {
            throw new DomainRuleException("MATERIAL_CATEGORY_CODE_ALREADY_EXISTS", "A material category with this code already exists.");
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation(MaterialConfiguration.CodeIndex))
        {
            throw new DomainRuleException("MATERIAL_CODE_ALREADY_EXISTS", "A material with this code already exists.");
        }
    }
}
