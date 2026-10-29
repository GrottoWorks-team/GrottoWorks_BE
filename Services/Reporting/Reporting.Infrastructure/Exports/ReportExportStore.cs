using Microsoft.EntityFrameworkCore;
using Reporting.Application.Exports;
using Reporting.Domain;
using Reporting.Domain.Exports;
using Reporting.Infrastructure.Data;
using Reporting.Infrastructure.Data.Configurations;

namespace Reporting.Infrastructure.Exports;

public sealed class ReportExportStore(ReportingDbContext dbContext) : IReportExportStore
{
    public Task<ReportExportJob?> GetAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        dbContext.ReportExportJobs.FirstOrDefaultAsync(job => job.ReportExportJobId == jobId, cancellationToken);

    public Task<ReportExportJob?> FindByIdempotencyKeyAsync(Guid userId, string key, CancellationToken cancellationToken = default) =>
        dbContext.ReportExportJobs.FirstOrDefaultAsync(job => job.RequestedByUserId == userId && job.IdempotencyKey == key, cancellationToken);

    public async Task<ReportExportJob?> ClaimNextAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var job = await dbContext.ReportExportJobs
            .Where(candidate => candidate.Status == ExportStatus.Queued)
            .OrderBy(candidate => candidate.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (job is null)
        {
            return null;
        }

        job.Start(now);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return job;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another worker claimed it first (xmin changed).
            dbContext.Entry(job).State = EntityState.Detached;
            return null;
        }
    }

    public async Task<int> RecoverStaleAsync(DateTimeOffset startedBefore, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var stale = await dbContext.ReportExportJobs
            .Where(job => job.Status == ExportStatus.Running && job.StartedAt < startedBefore)
            .ToListAsync(cancellationToken);
        stale.ForEach(job => job.Recover(now));
        await dbContext.SaveChangesAsync(cancellationToken);
        return stale.Count;
    }

    public void Add(ReportExportJob job) => dbContext.ReportExportJobs.Add(job);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation(ReportExportJobConfiguration.IdempotencyIndex))
        {
            throw new DomainRuleException("IDEMPOTENCY_KEY_CONFLICT", "A request with this Idempotency-Key is being processed; retry it.");
        }
    }
}
