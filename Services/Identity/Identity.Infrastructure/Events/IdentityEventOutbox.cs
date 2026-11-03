using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Identity.Contracts.Events;
using Identity.Infrastructure.Data;

namespace Identity.Infrastructure.Events;

public sealed class IdentityEventOutboxMessage
{
    private IdentityEventOutboxMessage() { }
    public Guid Id { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string Envelope { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public Guid AggregateId { get; private set; }
    public long Sequence { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public string? LastError { get; private set; }

    public IdentityIntegrationEvent ToMessage() => JsonSerializer.Deserialize<IdentityIntegrationEvent>(
        Envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new InvalidOperationException("The identity event envelope is empty.");

    public void MarkPublished(DateTimeOffset now)
    {
        PublishedAt = now;
        LastError = null;
    }

    public void MarkFailed(DateTimeOffset now)
    {
        Attempts++;
        NextAttemptAt = now.AddSeconds(Attempts switch { 1 => 2, 2 => 5, 3 => 15, 4 => 60, _ => 300 });
        LastError = "Event delivery failed; inspect correlated dispatcher logs.";
    }

    public static IdentityEventOutboxMessage Create(IdentityIntegrationEvent message) => new()
    {
        Id = message.EventId, EventType = message.EventType, CreatedAt = message.OccurredAt,
        AggregateId = message.AggregateId, NextAttemptAt = message.OccurredAt,
        Envelope = JsonSerializer.Serialize(message, new JsonSerializerOptions(JsonSerializerDefaults.Web))
    };
}

public sealed class IdentityEventOutboxConfiguration : IEntityTypeConfiguration<IdentityEventOutboxMessage>
{
    public void Configure(EntityTypeBuilder<IdentityEventOutboxMessage> builder)
    {
        builder.ToTable("identity_event_outbox");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasColumnName("event_id").ValueGeneratedNever();
        builder.Property(row => row.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
        builder.Property(row => row.Envelope).HasColumnName("envelope").HasColumnType("jsonb").IsRequired();
        builder.Property(row => row.CreatedAt).HasColumnName("created_at");
        builder.Property(row => row.PublishedAt).HasColumnName("published_at");
        builder.Property(row => row.AggregateId).HasColumnName("aggregate_id");
        builder.Property(row => row.Sequence).HasColumnName("sequence").UseIdentityAlwaysColumn();
        builder.Property(row => row.Attempts).HasColumnName("attempts").HasDefaultValue(0);
        builder.Property(row => row.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(row => row.LastError).HasColumnName("last_error").HasMaxLength(500);
        builder.HasIndex(row => new { row.AggregateId, row.Sequence }).HasDatabaseName("ix_identity_event_outbox_aggregate_sequence");
        builder.HasIndex(row => row.CreatedAt).HasFilter("published_at IS NULL")
            .HasDatabaseName("ix_identity_event_outbox_pending");
    }
}
