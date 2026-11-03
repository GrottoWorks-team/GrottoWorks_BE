using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskExecution.Application.Tasks;
using TaskExecution.Domain;
using TaskExecution.Domain.Tasks;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.Infrastructure.Tasks;

public sealed class CorrectiveTaskReplay
{
    public string Key { get; set; }="";
    public Guid ReadinessItemId { get; set; }
    public Guid TaskId { get; set; }
    public string Fingerprint { get; set; }="";
    public string Response { get; set; }="";
}
public sealed class SpecialTaskStore(TaskExecutionDbContext db) : ISpecialTaskStore
{
    public async Task<T> InTransactionAsync<T>(string key,Func<Task<T>> action,CancellationToken ct)
    {
        if(db.Database.CurrentTransaction is not null) {await LockAsync(key,ct);return await action();}
        await using var transaction=await db.Database.BeginTransactionAsync(ct);
        await LockAsync(key,ct);var result=await action();await transaction.CommitAsync(ct);return result;
    }
    private Task LockAsync(string key,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key},0))",ct);
    public Task<WorkTask?> FindPurchaseAsync(Guid requestId,CancellationToken ct)=>db.Tasks.SingleOrDefaultAsync(row=>row.PurchaseRequestId==requestId,ct);
    public async Task<TaskDto?> ReplayCorrectiveAsync(string key,Guid itemId,string fingerprint,CancellationToken ct)
    {
        var rows=await db.Set<CorrectiveTaskReplay>().Where(row=>row.Key==key || row.ReadinessItemId==itemId).ToListAsync(ct);
        if(rows.Count==0) return null;
        if(rows.Count!=1 || rows[0].Fingerprint!=fingerprint)
            throw new DomainRuleException("IDEMPOTENCY_KEY_REUSED","The key or readiness item has already been used with different input.");
        return JsonSerializer.Deserialize<TaskDto>(rows[0].Response) ?? throw new InvalidOperationException("Corrective replay response is corrupt.");
    }
    public void SaveCorrective(string key,Guid itemId,string fingerprint,TaskDto task)=>db.Set<CorrectiveTaskReplay>().Add(new() {
        Key=key,ReadinessItemId=itemId,TaskId=task.Id,Fingerprint=fingerprint,Response=JsonSerializer.Serialize(task) });
}
public sealed class CorrectiveTaskReplayConfiguration : IEntityTypeConfiguration<CorrectiveTaskReplay>
{
    public void Configure(EntityTypeBuilder<CorrectiveTaskReplay> b)
    {
        b.ToTable("corrective_task_replay");b.HasKey(x=>x.Key);b.Property(x=>x.Key).HasColumnName("idempotency_key").HasMaxLength(200);
        b.Property(x=>x.ReadinessItemId).HasColumnName("readiness_item_id");b.Property(x=>x.TaskId).HasColumnName("task_id");
        b.Property(x=>x.Fingerprint).HasColumnName("fingerprint").HasMaxLength(64);b.Property(x=>x.Response).HasColumnName("response").HasColumnType("jsonb");
        b.HasIndex(x=>x.ReadinessItemId).IsUnique();
    }
}
