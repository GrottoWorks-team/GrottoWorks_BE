using Microsoft.EntityFrameworkCore;
using Readiness.Application.Checklists;
using Readiness.Domain;
using Readiness.Domain.Checklists;
using Readiness.Infrastructure.Data;
using Readiness.Infrastructure.Data.Configurations;

namespace Readiness.Infrastructure.Checklists;

public sealed class ReadinessCheckStore(ReadinessDbContext dbContext) : IReadinessCheckStore
{
    public Task<ReadinessChecklist?> GetAsync(Guid checkId, CancellationToken cancellationToken = default) =>
        dbContext.Checklists.Include(checklist => checklist.Items)
            .FirstOrDefaultAsync(checklist => checklist.ReadinessChecklistId == checkId, cancellationToken);

    public Task<bool> ExistsForWorkAreaAsync(Guid workAreaId, CancellationToken cancellationToken = default) =>
        dbContext.Checklists.AnyAsync(checklist => checklist.WorkAreaId == workAreaId, cancellationToken);

    public async Task<IReadOnlyList<ReadinessChecklist>> ListByWorkAreasAsync(IReadOnlyCollection<Guid> workAreaIds, CancellationToken cancellationToken = default)
    {
        if (workAreaIds.Count == 0)
        {
            return [];
        }

        return await dbContext.Checklists.AsNoTracking().Include(checklist => checklist.Items)
            .Where(checklist => workAreaIds.Contains(checklist.WorkAreaId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReadinessChecklist>> ListByCorrectiveTaskAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        await dbContext.Checklists.Include(checklist => checklist.Items)
            .Where(checklist => checklist.Items.Any(item => item.CorrectiveTaskId == taskId))
            .ToListAsync(cancellationToken);

    public void Add(ReadinessChecklist checklist) => dbContext.Checklists.Add(checklist);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation(ReadinessChecklistConfiguration.WorkAreaIndex))
        {
            throw new DomainRuleException("READINESS_CHECK_EXISTS", "This work area already has a readiness checklist.");
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainRuleException(
                "READINESS_VERSION_MISMATCH",
                "The checklist was changed by someone else. Reload it and try again.",
                DomainErrorKind.PreconditionFailed);
        }
    }
}
