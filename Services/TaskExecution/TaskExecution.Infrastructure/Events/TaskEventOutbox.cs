using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskExecution.Application.Events;
using TaskExecution.Contracts.Events;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.Infrastructure.Events;

public sealed class TaskEventOutboxMessage
{
    private TaskEventOutboxMessage() { }
    public Guid Id { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string Envelope { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    public static TaskEventOutboxMessage Create(TaskIntegrationEvent message) => new()
    {
        Id = message.EventId, EventType = message.EventType, CreatedAt = message.OccurredAt,
        Envelope = JsonSerializer.Serialize(message, new JsonSerializerOptions(JsonSerializerDefaults.Web))
    };
}

public sealed class TaskEventOutbox(TaskExecutionDbContext db) : ITaskEventOutbox
{
    // Saved by the caller's DbContext transaction. No transport is registered yet.
    public void Add(TaskIntegrationEvent message) => db.EventOutbox.Add(TaskEventOutboxMessage.Create(message));
}

public sealed class TaskEventOutboxConfiguration : IEntityTypeConfiguration<TaskEventOutboxMessage>
{
    public void Configure(EntityTypeBuilder<TaskEventOutboxMessage> builder)
    {
        builder.ToTable("task_event_outbox");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasColumnName("event_id").ValueGeneratedNever();
        builder.Property(row => row.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
        builder.Property(row => row.Envelope).HasColumnName("envelope").HasColumnType("jsonb").IsRequired();
        builder.Property(row => row.CreatedAt).HasColumnName("created_at");
        builder.Property(row => row.PublishedAt).HasColumnName("published_at");
        builder.HasIndex(row => row.CreatedAt).HasFilter("published_at IS NULL")
            .HasDatabaseName("ix_task_event_outbox_pending");
    }
}
