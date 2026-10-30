using Microsoft.EntityFrameworkCore;
using TaskExecution.Application.Support;
using TaskExecution.Domain;
using TaskExecution.Domain.Support;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.Infrastructure.Support;

public sealed partial class SupportRequestRepository(TaskExecutionDbContext db) : ISupportRequestRepository
{
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
