using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ParishCoordination.Infrastructure.Lifecycle;

public sealed class LifecycleTaskProjection
{
    public Guid TaskId { get; set; }
    public Guid WorkAreaId { get; set; }
    public Guid SeasonId { get; set; }
    public Guid ParishId { get; set; }
    public long SourceVersion { get; set; }
    public string Status { get; set; } = "OPEN";
    public string? TaskType { get; set; }

    public bool Apply(long version, string status, string? taskType)
    {
        if (version < SourceVersion) return false;
        if (version == SourceVersion)
        {
            if (Status != status) throw new FormatException("Conflicting Task states at the same source version.");
            // A status event may precede the complete snapshot at the same version.
            var enriched = TaskType is null && taskType is not null;
            TaskType ??= taskType;
            return enriched;
        }
        SourceVersion = version;
        Status = status;
        TaskType = taskType ?? TaskType;
        return true;
    }
}

public sealed class LifecycleReadinessProjection
{
    public Guid WorkAreaId { get; set; }
    public Guid ChecklistId { get; set; }
    public DateTimeOffset DecisionAt { get; set; }
    public bool Reopened { get; set; }
    public bool Invalidated { get; set; }
    public bool Applied { get; set; }

    public bool Apply(Guid checklistId, DateTimeOffset at, bool reopened)
    {
        // Legacy Readiness v1 carries business timestamps, not an aggregate version.
        // A reopen wins a timestamp tie so ambiguity cannot grant readiness.
        if (at < DecisionAt || (at == DecisionAt && (!reopened || Reopened))) return false;
        ChecklistId = checklistId;
        DecisionAt = at;
        Reopened = reopened;
        Invalidated = false;
        Applied = false;
        return true;
    }
}

public sealed class LifecycleInboxMessage
{
    public Guid EventId { get; set; }
    public Guid WorkAreaId { get; set; }
    public Guid SeasonId { get; set; }
    public Guid ParishId { get; set; }
    public string Kind { get; set; } = "";
    public string Envelope { get; set; } = "";
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

public sealed class LifecycleTaskConfiguration : IEntityTypeConfiguration<LifecycleTaskProjection>
{
    public void Configure(EntityTypeBuilder<LifecycleTaskProjection> b)
    {
        b.ToTable("lifecycle_task_projection");
        b.HasKey(x => x.TaskId);
        b.Property(x => x.TaskId).HasColumnName("task_id").ValueGeneratedNever();
        b.Property(x => x.WorkAreaId).HasColumnName("work_area_id");
        b.Property(x => x.SeasonId).HasColumnName("season_id");
        b.Property(x => x.ParishId).HasColumnName("parish_id");
        b.Property(x => x.SourceVersion).HasColumnName("source_version").IsConcurrencyToken();
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(30);
        b.Property(x => x.TaskType).HasColumnName("task_type").HasMaxLength(20);
        b.HasIndex(x => new { x.WorkAreaId, x.Status });
    }
}

public sealed class LifecycleReadinessConfiguration : IEntityTypeConfiguration<LifecycleReadinessProjection>
{
    public void Configure(EntityTypeBuilder<LifecycleReadinessProjection> b)
    {
        b.ToTable("lifecycle_readiness_projection");
        b.HasKey(x => x.WorkAreaId);
        b.Property(x => x.WorkAreaId).HasColumnName("work_area_id").ValueGeneratedNever();
        b.Property(x => x.ChecklistId).HasColumnName("checklist_id");
        b.Property(x => x.DecisionAt).HasColumnName("decision_at");
        b.Property(x => x.Reopened).HasColumnName("reopened");
        b.Property(x => x.Invalidated).HasColumnName("invalidated");
        b.Property(x => x.Applied).HasColumnName("applied");
    }
}

public sealed class LifecycleInboxConfiguration : IEntityTypeConfiguration<LifecycleInboxMessage>
{
    public void Configure(EntityTypeBuilder<LifecycleInboxMessage> b)
    {
        b.ToTable("lifecycle_inbox");
        b.HasKey(x => x.EventId);
        b.Property(x => x.EventId).HasColumnName("event_id").ValueGeneratedNever();
        b.Property(x => x.WorkAreaId).HasColumnName("work_area_id");
        b.Property(x => x.SeasonId).HasColumnName("season_id");
        b.Property(x => x.ParishId).HasColumnName("parish_id");
        b.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(30);
        b.Property(x => x.Envelope).HasColumnName("envelope").HasColumnType("jsonb");
        b.Property(x => x.ReceivedAt).HasColumnName("received_at");
        b.Property(x => x.ProcessedAt).HasColumnName("processed_at");
        b.HasIndex(x => new { x.WorkAreaId, x.ReceivedAt }).HasFilter("processed_at IS NULL");
    }
}
