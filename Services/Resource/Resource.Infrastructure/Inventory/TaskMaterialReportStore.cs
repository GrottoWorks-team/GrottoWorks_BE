using Microsoft.EntityFrameworkCore;
using Resource.Application.Inventory;
using Resource.Domain;
using Resource.Domain.Inventory;
using Resource.Infrastructure.Data;
using Resource.Infrastructure.Data.Configurations;

namespace Resource.Infrastructure.Inventory;

public sealed class TaskMaterialReportStore(ResourceDbContext dbContext) : ITaskMaterialReportStore
{
    public Task<TaskMaterialReport?> GetAsync(Guid reportId, CancellationToken cancellationToken = default) =>
        dbContext.TaskMaterialReports.Include(report => report.Items)
            .FirstOrDefaultAsync(report => report.TaskMaterialReportId == reportId, cancellationToken);

    public Task<TaskMaterialReport?> GetByTaskAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        dbContext.TaskMaterialReports.Include(report => report.Items)
            .FirstOrDefaultAsync(report => report.TaskId == taskId, cancellationToken);

    public void Add(TaskMaterialReport report) => dbContext.TaskMaterialReports.Add(report);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation(TaskMaterialReportConfiguration.TaskIndex))
        {
            throw new DomainRuleException("MATERIAL_REPORT_ALREADY_SUBMITTED", "This task already has a material report.");
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation(MaterialTransactionConfiguration.ReferenceIndex))
        {
            throw new DomainRuleException("MATERIAL_REPORT_ALREADY_POSTED", "This report was already returned to stock.");
        }
        catch (DbUpdateConcurrencyException exception) when (exception.Entries.Any(entry => entry.Entity is TaskMaterialReport))
        {
            throw new DomainRuleException(
                "MATERIAL_REPORT_VERSION_MISMATCH",
                "The material report was changed by someone else. Reload it and try again.",
                DomainErrorKind.PreconditionFailed);
        }
    }
}
