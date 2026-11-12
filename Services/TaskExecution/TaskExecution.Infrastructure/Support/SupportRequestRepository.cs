using Microsoft.EntityFrameworkCore;
using TaskExecution.Application.Support;
using TaskExecution.Domain;
using TaskExecution.Domain.Support;
using TaskExecution.Infrastructure.Data;
using Npgsql;

namespace TaskExecution.Infrastructure.Support;

public sealed partial class SupportRequestRepository(TaskExecutionDbContext db) : ISupportRequestRepository
{
    public Task<Guid?> FindRequestIdByMemberAsync(Guid memberId,CancellationToken cancellationToken)=>
        db.Set<SupportResponseMember>().Where(member=>member.Id==memberId).Select(member=>(Guid?)member.RequestId).SingleOrDefaultAsync(cancellationToken);
    public async Task<T> InRequestTransactionAsync<T>(Guid requestId,Func<Task<T>> operation,CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            var existingKey = $"support-request:{requestId:D}";
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({existingKey},0))", cancellationToken);
            return await operation();
        }
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

    public Task<bool> IsTaskRequestingSupportAsync(Guid taskId,CancellationToken cancellationToken) =>
        db.Set<SupportPosition>()
            .Where(position => position.TaskId == taskId)
            .AnyAsync(position => db.SupportRequests.Any(request => request.Id == position.RequestId &&
                (request.Status == SupportRequestStatus.Published || request.Status == SupportRequestStatus.PartiallyFulfilled)),
                cancellationToken);

    public async Task<(IReadOnlyList<SupportRequest> Items,long Total)> GetPageAsync(Guid? parishId,Guid? seasonId,Guid? areaId,
        SupportType? type,SupportRequestStatus? status,int page,int size,bool descending,CancellationToken ct)
    {
        var query=db.SupportRequests.AsNoTracking()
            .Include(r=>r.Positions).ThenInclude(p=>p.Skills).Include(r=>r.Materials).Include(r=>r.Responses).ThenInclude(r=>r.Members)
            .Include(r=>r.Responses).ThenInclude(r=>r.Materials).AsSplitQuery()
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
        var query=db.SupportRequests.Include(r=>r.Positions).ThenInclude(p=>p.Skills).Include(r=>r.Materials)
            .Include(r=>r.Responses).ThenInclude(r=>r.Members).Include(r=>r.Responses).ThenInclude(r=>r.Materials).AsSplitQuery();
        return (tracked ? query : query.AsNoTracking()).SingleOrDefaultAsync(r=>r.Id==id,ct);
    }
    public void Add(SupportRequest request)=>db.SupportRequests.Add(request);
    public async Task SaveAsync(CancellationToken ct)
    {
        try {await db.SaveChangesAsync(ct);}
        catch(DbUpdateConcurrencyException exception)
        {
            var code=exception.Entries.Any(entry=>entry.Entity is SupportResponseMaterial) ? "SUPPORT_MATERIAL_VERSION_MISMATCH"
                : exception.Entries.Any(entry=>entry.Entity is SupportResponseMember) ? "SUPPORT_MEMBER_VERSION_MISMATCH"
                : exception.Entries.Any(entry=>entry.Entity is TaskExecution.Domain.Tasks.WorkTask) ? "TASK_VERSION_MISMATCH" : "SUPPORT_REQUEST_VERSION_MISMATCH";
            throw new DomainRuleException(code,"The support request, member or task changed concurrently.",DomainErrorKind.PreconditionFailed);
        }
        catch(DbUpdateException exception) when(exception.InnerException is PostgresException {SqlState:PostgresErrorCodes.UniqueViolation,ConstraintName:SupportResponseMemberConfiguration.ActiveVolunteerIndex})
        {throw new DomainRuleException("SUPPORT_MEMBER_DUPLICATE","The volunteer already has an active position in this request.");}
    }
}
