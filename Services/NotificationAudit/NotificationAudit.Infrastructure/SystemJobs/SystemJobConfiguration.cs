using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationAudit.Domain.SystemJobs;

namespace NotificationAudit.Infrastructure.SystemJobs;

public sealed class SystemJobConfiguration : IEntityTypeConfiguration<SystemJob>
{
    public void Configure(EntityTypeBuilder<SystemJob> builder)
    {
        builder.ToTable("system_job");
        builder.HasKey(job => job.Id);
        builder.Property(job => job.Id).HasColumnName("id");
        builder.Property(job => job.OwnerUserId).HasColumnName("owner_user_id");
        builder.Property(job => job.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(20);
        builder.Property(job => job.IdempotencyKey).HasColumnName("idempotency_key");
        builder.Property(job => job.Fingerprint).HasColumnName("fingerprint").HasMaxLength(100);
        builder.Property(job => job.ServicesJson).HasColumnName("services_json").HasColumnType("jsonb");
        builder.Property(job => job.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        builder.Property(job => job.CreatedAt).HasColumnName("created_at");
        builder.Property(job => job.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(job => job.StartedAt).HasColumnName("started_at");
        builder.Property(job => job.FinishedAt).HasColumnName("finished_at");
        builder.Property(job => job.Attempts).HasColumnName("attempts");
        builder.Property(job => job.LeaseToken).HasColumnName("lease_token");
        builder.Property(job => job.LeaseUntil).HasColumnName("lease_until");
        builder.Property(job => job.ArtifactFileId).HasColumnName("artifact_file_id");
        builder.Property(job => job.ArtifactObjectKey).HasColumnName("artifact_object_key").HasMaxLength(500);
        builder.Property(job => job.ArtifactExpiresAt).HasColumnName("artifact_expires_at");
        builder.Property(job => job.ErrorCode).HasColumnName("error_code").HasMaxLength(100);
        builder.Property(job => job.ManifestJson).HasColumnName("manifest_json").HasColumnType("jsonb");
        builder.Property(job => job.CorrelationId).HasColumnName("correlation_id");
        builder.HasIndex(job => new { job.OwnerUserId, job.Kind, job.IdempotencyKey }).IsUnique().HasDatabaseName("ux_system_job_owner_kind_key");
        builder.HasIndex(job => new { job.Kind, job.Status, job.NextAttemptAt }).HasDatabaseName("ix_system_job_claim");
        builder.HasIndex(job => job.ArtifactFileId).IsUnique();
    }
}
