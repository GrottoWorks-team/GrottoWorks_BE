using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Readiness.Application.Scope;
using Readiness.Infrastructure.Data;

namespace Readiness.Infrastructure.Messaging;

public sealed class ScopeReplicationReceipt
{
    public string Consumer { get; set; } = string.Empty;
    public Guid EventId { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}

public sealed class ScopeReplicationReceiptConfiguration : IEntityTypeConfiguration<ScopeReplicationReceipt>
{
    public void Configure(EntityTypeBuilder<ScopeReplicationReceipt> builder)
    {
        builder.ToTable("scope_replication_inbox");
        builder.HasKey(row => new { row.Consumer, row.EventId });
        builder.Property(row => row.Consumer).HasColumnName("consumer").HasMaxLength(100);
        builder.Property(row => row.EventId).HasColumnName("event_id");
        builder.Property(row => row.ProcessedAt).HasColumnName("processed_at");
    }
}

public sealed class ScopeReplicationInbox(ReadinessDbContext db) : IScopeReplicationInbox
{
    public async Task ExecuteAsync(string consumer, Guid eventId, Guid seasonId, Func<Task> apply,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Serialize parent and child changes across both snapshot queues.
        var key = $"readiness-scope-season:{seasonId:D}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        var receipts = db.Set<ScopeReplicationReceipt>();
        if (!await receipts.AnyAsync(row => row.Consumer == consumer && row.EventId == eventId, cancellationToken))
        {
            await apply();
            receipts.Add(new() { Consumer = consumer, EventId = eventId, ProcessedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }
}
