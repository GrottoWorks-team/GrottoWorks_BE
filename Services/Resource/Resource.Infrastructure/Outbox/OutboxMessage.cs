using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Resource.Contracts.Events;

namespace Resource.Infrastructure.Outbox;

/// <summary><c>integration_event_outbox</c> (technical, spec §3 "outbox/inbox"): one integration event waiting for the broker.</summary>
public sealed class OutboxMessage
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private OutboxMessage()
    {
    }

    public Guid EventId { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public string AggregateType { get; private set; } = string.Empty;

    public Guid AggregateId { get; private set; }

    public string Envelope { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public static OutboxMessage Create(ResourceIntegrationEvent message) =>
        new()
        {
            EventId = message.EventId,
            EventType = message.EventType,
            AggregateType = message.AggregateType,
            AggregateId = message.AggregateId,
            Envelope = JsonSerializer.Serialize(message, JsonOptions),
            CreatedAt = message.OccurredAt,
            NextAttemptAt = message.OccurredAt
        };

    public ResourceIntegrationEvent ToMessage() =>
        JsonSerializer.Deserialize<ResourceIntegrationEvent>(Envelope, JsonOptions)
        ?? throw new InvalidOperationException($"Outbox row {EventId} has no envelope.");

    public void MarkPublished(DateTimeOffset now)
    {
        PublishedAt = now;
        LastError = null;
    }

    /// <summary>Backoff 2 s, 5 s, 15 s, 1 min, then 5 min between attempts.</summary>
    public void MarkFailed(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length > 1000 ? error[..1000] : error;
        NextAttemptAt = now + TimeSpan.FromSeconds(Attempts switch { 1 => 2, 2 => 5, 3 => 15, 4 => 60, _ => 300 });
    }
}

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public const string TableName = "integration_event_outbox";

    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable(TableName);
        builder.HasKey(row => row.EventId);
        builder.Property(row => row.EventId).HasColumnName("event_id").ValueGeneratedNever();
        builder.Property(row => row.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
        builder.Property(row => row.AggregateType).HasColumnName("aggregate_type").HasMaxLength(50).IsRequired();
        builder.Property(row => row.AggregateId).HasColumnName("aggregate_id").IsRequired();
        builder.Property(row => row.Envelope).HasColumnName("envelope").HasColumnType("jsonb").IsRequired();
        builder.Property(row => row.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(row => row.NextAttemptAt).HasColumnName("next_attempt_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(row => row.PublishedAt).HasColumnName("published_at").HasColumnType("timestamp with time zone");
        builder.Property(row => row.Attempts).HasColumnName("attempts").IsRequired();
        builder.Property(row => row.LastError).HasColumnName("last_error").HasMaxLength(1000);
        builder.HasIndex(row => new { row.NextAttemptAt, row.CreatedAt }, "ix_integration_event_outbox_pending").HasFilter("published_at IS NULL");
    }
}
