using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Reporting.Domain.Exports;

namespace Reporting.Infrastructure.Data.Configurations;

/// <summary><c>report_export_job</c> (spec §3 technical table, F-RPT-06).</summary>
public sealed class ReportExportJobConfiguration : IEntityTypeConfiguration<ReportExportJob>
{
    public const string IdempotencyIndex = "ux_report_export_job_user_idempotency_key";

    public void Configure(EntityTypeBuilder<ReportExportJob> builder)
    {
        builder.ToTable("report_export_job");
        builder.HasKey(job => job.ReportExportJobId);
        builder.Property(job => job.ReportExportJobId).HasColumnName("report_export_job_id").ValueGeneratedNever();
        builder.Property(job => job.ParishId).HasColumnName("parish_id").IsRequired();
        builder.Property(job => job.RequestedByUserId).HasColumnName("requested_by_user_id").IsRequired();
        builder.Property(job => job.RequestedByRole).HasColumnName("requested_by_role").HasMaxLength(30).IsRequired();
        builder.Property(job => job.ReportType).HasColumnName("report_type").HasMaxLength(30).IsRequired();
        builder.Property(job => job.Format).HasColumnName("export_format").HasConversion(EnumText.Converter<ExportFormat>()).HasMaxLength(10).IsRequired();
        builder.Property(job => job.SeasonId).HasColumnName("season_id").IsRequired();
        builder.Property(job => job.WorkAreaId).HasColumnName("work_area_id");
        builder.Property(job => job.CommunityId).HasColumnName("community_id");
        builder.Property(job => job.FromDate).HasColumnName("from_date");
        builder.Property(job => job.ToDate).HasColumnName("to_date");
        builder.Property(job => job.Status).HasColumnName("job_status").HasConversion(EnumText.Converter<ExportStatus>()).HasMaxLength(20).IsRequired();
        builder.Property(job => job.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(ReportExportJob.MaxIdempotencyKeyLength);
        builder.Property(job => job.Attempts).HasColumnName("attempts");
        builder.Property(job => job.FileName).HasColumnName("file_name").HasMaxLength(200);
        builder.Property(job => job.ContentType).HasColumnName("content_type").HasMaxLength(100);
        builder.Property(job => job.Content).HasColumnName("content");
        builder.Property(job => job.FileSize).HasColumnName("file_size");
        builder.Property(job => job.ErrorCode).HasColumnName("error_code").HasMaxLength(100);
        builder.Property(job => job.ErrorMessage).HasColumnName("error_message").HasMaxLength(500);
        builder.Property(job => job.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(job => job.StartedAt).HasColumnName("started_at").HasColumnType("timestamp with time zone");
        builder.Property(job => job.CompletedAt).HasColumnName("completed_at").HasColumnType("timestamp with time zone");
        builder.Property(job => job.Version).IsRowVersion();

        builder.HasIndex(job => new { job.RequestedByUserId, job.IdempotencyKey }).IsUnique().HasDatabaseName(IdempotencyIndex)
            .HasFilter("\"idempotency_key\" IS NOT NULL");
        builder.HasIndex(job => new { job.Status, job.CreatedAt }, "ix_report_export_job_status_created");
    }
}
