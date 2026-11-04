using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskExecution.Application.Scopes;
using TaskExecution.Domain.Scopes;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.Infrastructure.Scopes;

public sealed class ScopeInboxMessage
{
    public string Consumer { get; set; } = string.Empty;
    public Guid EventId { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}

public sealed class ScopeInboxConfiguration : IEntityTypeConfiguration<ScopeInboxMessage>
{
    public void Configure(EntityTypeBuilder<ScopeInboxMessage> builder)
    {
        builder.ToTable("scope_event_inbox");
        builder.HasKey(row => new { row.Consumer, row.EventId });
        builder.Property(row => row.Consumer).HasColumnName("consumer").HasMaxLength(100);
        builder.Property(row => row.EventId).HasColumnName("event_id");
        builder.Property(row => row.ProcessedAt).HasColumnName("processed_at");
    }
}

public sealed class UserStatusProjectionConfiguration : IEntityTypeConfiguration<UserStatusProjection>
{
    public void Configure(EntityTypeBuilder<UserStatusProjection> builder)
    {
        builder.ToTable("user_status_projection");
        builder.HasKey(row => row.UserId);
        builder.Property(row => row.UserId).HasColumnName("user_id");
        builder.Property(row => row.ParishId).HasColumnName("parish_id");
        builder.Property(row => row.Status).HasColumnName("status").HasMaxLength(30).IsRequired();
        builder.Property(row => row.SourceVersion).HasColumnName("source_version");
    }
}

public sealed class ScopeEventInbox(TaskExecutionDbContext db) : IScopeEventInbox
{
    public async Task ExecuteAsync(string consumer, Guid eventId, Func<Task> apply, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var key = $"scope-inbox:{consumer}:{eventId:D}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        if (await db.ScopeEventInbox.AnyAsync(row => row.Consumer == consumer && row.EventId == eventId, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }
        await apply();
        db.ScopeEventInbox.Add(new ScopeInboxMessage { Consumer = consumer, EventId = eventId, ProcessedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}

public sealed class UserStatusProjectionStore(TaskExecutionDbContext db) : IUserStatusProjectionStore
{
    public Task<UserStatusProjection?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.UserStatusProjections.SingleOrDefaultAsync(row => row.UserId == id, cancellationToken);
    public void Add(UserStatusProjection projection) => db.UserStatusProjections.Add(projection);
    public Task SaveAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
