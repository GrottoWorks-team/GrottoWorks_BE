using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Reporting.Domain.Inbox;

namespace Reporting.Infrastructure.Data.Configurations;

/// <summary><c>processed_event</c> — inbox (technical, spec §3 "outbox/inbox").</summary>
public sealed class ProcessedEventConfiguration : IEntityTypeConfiguration<ProcessedEvent>
{
    public const string PrimaryKey = "pk_processed_event";

    public void Configure(EntityTypeBuilder<ProcessedEvent> builder)
    {
        builder.ToTable("processed_event");
        builder.HasKey(processed => processed.EventId).HasName(PrimaryKey);
        builder.Property(processed => processed.EventId).HasColumnName("event_id").ValueGeneratedNever();
        builder.Property(processed => processed.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
        builder.Property(processed => processed.Outcome).HasColumnName("outcome").HasMaxLength(30).IsRequired();
        builder.Property(processed => processed.ProcessedAt).HasColumnName("processed_at").HasColumnType("timestamp with time zone").IsRequired();
    }
}
