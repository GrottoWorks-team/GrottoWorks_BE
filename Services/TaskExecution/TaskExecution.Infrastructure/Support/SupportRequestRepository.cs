using Microsoft.EntityFrameworkCore;
using TaskExecution.Application.Support;
using TaskExecution.Domain;
using TaskExecution.Domain.Support;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.Infrastructure.Support;

public sealed partial class SupportRequestRepository(TaskExecutionDbContext db) : ISupportRequestRepository
{
    public async Task<T> InRequestTransactionAsync<T>(Guid requestId,Func<Task<T>> operation,CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var key = $"support-request:{requestId:D}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key},0))", cancellationToken);
        var result = await operation();
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<IReadOnlyList<SupportPositionTask>> GetPositionTasksAsync(IReadOnlyCollection<Guid> taskIds,CancellationToken cancellationToken)
    {
        if (taskIds.Count == 0) return [];
        var ids = taskIds.Distinct().ToArray();
        // Keep each checked Task state stable until publication commits, including concurrent cancellation.
        return await db.Tasks.FromSqlInterpolated($"SELECT * FROM task WHERE task_id = ANY({ids}) ORDER BY task_id FOR SHARE")
            .AsNoTracking().Select(task => new SupportPositionTask(task.Id,task.WorkAreaId,task.Status)).ToListAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<SupportRequest> Items,long Total)> GetPageAsync(Guid? parishId,Guid? seasonId,Guid? areaId,
        SupportType? type,SupportRequestStatus? status,int page,int size,bool descending,CancellationToken ct)
    {
        var query=db.SupportRequests.AsNoTracking()
            .Include(r=>r.Positions).ThenInclude(p=>p.Skills).Include(r=>r.Materials).AsSplitQuery()
            .Where(request=>db.WorkAreaScopes.Any(area=>area.WorkAreaId==request.WorkAreaId &&
                (!parishId.HasValue || area.ParishId==parishId.Value)));
        if (seasonId.HasValue) query=query.Where(r=>r.SeasonId==seasonId.Value);
        if (areaId.HasValue) query=query.Where(r=>r.WorkAreaId==areaId.Value);
        if (type.HasValue) query=query.Where(r=>r.SupportType==type.Value);
        if (status.HasValue) query=query.Where(r=>r.Status==status.Value);
        var total=await query.LongCountAsync(ct);
        var sorted=descending ? query.OrderByDescending(r=>r.CreatedAt).ThenBy(r=>r.Id) : query.OrderBy(r=>r.CreatedAt).ThenBy(r=>r.Id);
        return (await sorted.Skip((page-1)*size).Take(size).ToListAsync(ct),total);
    }

    public Task<SupportRequest?> GetAsync(Guid id,bool tracked,CancellationToken ct)
    {
        var query=db.SupportRequests.Include(r=>r.Positions).ThenInclude(p=>p.Skills).Include(r=>r.Materials).AsSplitQuery();
        return (tracked ? query : query.AsNoTracking()).SingleOrDefaultAsync(r=>r.Id==id,ct);
    }
    public void Add(SupportRequest request)=>db.SupportRequests.Add(request);
    public async Task SaveAsync(CancellationToken ct)
    {
        try {await db.SaveChangesAsync(ct);}
        catch(DbUpdateConcurrencyException) {throw new DomainRuleException("SUPPORT_REQUEST_VERSION_MISMATCH","The request changed concurrently.",DomainErrorKind.PreconditionFailed);}
    }
}
