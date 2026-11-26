using Microsoft.EntityFrameworkCore;
using Resource.Application.Requirements;
using Resource.Domain;
using Resource.Domain.Inventory;
using Resource.Domain.Scope;
using Resource.Infrastructure.Data;
using Resource.Infrastructure.Data.Configurations;

namespace Resource.Infrastructure.Requirements;

public sealed class MaterialRequirementStore(ResourceDbContext dbContext) : IMaterialRequirementStore
{
    public async Task<IReadOnlyList<MaterialRequirement>> ListByWorkAreaAsync(Guid workAreaId, CancellationToken cancellationToken = default)
    {
        var plans = await dbContext.Set<TaskMaterialPlan>().AsNoTracking()
            .Include(plan => plan.Items)
            .Where(plan => plan.WorkAreaId == workAreaId)
            .ToListAsync(cancellationToken);

        var scope = await dbContext.Set<WorkAreaScope>().AsNoTracking()
            .FirstOrDefaultAsync(row => row.WorkAreaId == workAreaId, cancellationToken);
        if (scope is not null)
        {
            plans = plans.Where(plan => plan.SeasonId == scope.SeasonId).ToList();
        }

        var now = DateTimeOffset.UtcNow;
        return plans
            .SelectMany(plan => plan.Items)
            .GroupBy(item => item.MaterialId)
            .Select(group => MaterialRequirement.Create(workAreaId, group.Key, group.Sum(item => item.RequiredQuantity), 0, null, now))
            .ToList();
    }

    public Task<MaterialRequirement?> GetAsync(Guid requirementId, CancellationToken cancellationToken = default) =>
        dbContext.MaterialRequirements.FirstOrDefaultAsync(r => r.MaterialRequirementId == requirementId, cancellationToken);

    public Task<bool> ExistsAsync(Guid workAreaId, Guid materialId, CancellationToken cancellationToken = default) =>
        dbContext.MaterialRequirements.AnyAsync(r => r.WorkAreaId == workAreaId && r.MaterialId == materialId, cancellationToken);

    public void Add(MaterialRequirement requirement) => dbContext.MaterialRequirements.Add(requirement);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation(MaterialRequirementConfiguration.WorkAreaMaterialIndex))
        {
            throw new DomainRuleException(
                "REQUIREMENT_ALREADY_EXISTS",
                "This work area already has a requirement for the material. Update it instead.");
        }
        catch (DbUpdateConcurrencyException exception) when (exception.Entries.Any(entry => entry.Entity is MaterialRequirement))
        {
            throw new DomainRuleException(
                "REQUIREMENT_VERSION_MISMATCH",
                "The requirement was changed by someone else. Reload it and try again.",
                DomainErrorKind.PreconditionFailed);
        }
    }
}
