using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationAudit.Domain.Files;

namespace NotificationAudit.Infrastructure.Files;

internal sealed class FileObjectConfiguration : IEntityTypeConfiguration<FileObject>
{
    public void Configure(EntityTypeBuilder<FileObject> builder)
    {
        builder.ToTable("file_object");
        builder.HasKey(file => file.Id);
        builder.Property(file => file.Id).HasColumnName("file_id");
        builder.Property(file => file.UploadedByUserId).HasColumnName("uploaded_by_user_id");
        builder.Property(file => file.ObjectKey).HasColumnName("object_key").HasMaxLength(500).IsRequired();
        builder.HasIndex(file => file.ObjectKey).IsUnique().HasDatabaseName("ux_file_object_object_key");
        builder.Property(file => file.OriginalFileName).HasColumnName("original_file_name").HasMaxLength(255).IsRequired();
        builder.Property(file => file.ContentType).HasColumnName("content_type").HasMaxLength(100).IsRequired();
        builder.Property(file => file.SizeBytes).HasColumnName("size_bytes");
        builder.Property(file => file.Checksum).HasColumnName("checksum").HasMaxLength(128);
        builder.Property(file => file.Purpose).HasColumnName("purpose").HasMaxLength(50).HasConversion<string>();
        builder.Property(file => file.Status).HasColumnName("status").HasMaxLength(30).HasConversion<string>();
        builder.Property(file => file.ReferenceType).HasColumnName("reference_type").HasMaxLength(50);
        builder.Property(file => file.ReferenceId).HasColumnName("reference_id");
        builder.Property(file => file.CreatedAt).HasColumnName("created_at");
        builder.Property(file => file.CompletedAt).HasColumnName("completed_at");
        builder.HasIndex(file => new { file.UploadedByUserId, file.Status })
            .HasDatabaseName("ix_file_object_uploader_status");
    }
}
