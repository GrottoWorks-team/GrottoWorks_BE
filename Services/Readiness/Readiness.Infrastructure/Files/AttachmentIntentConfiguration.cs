using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Readiness.Domain.Files;

namespace Readiness.Infrastructure.Files;

public sealed class AttachmentIntentConfiguration : IEntityTypeConfiguration<AttachmentIntent>
{
    public void Configure(EntityTypeBuilder<AttachmentIntent> builder)
    {
        builder.ToTable("file_attachment_intent");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Purpose).HasMaxLength(50);
        builder.Property(x => x.State).HasMaxLength(20);
        builder.HasIndex(x => new { x.State, x.NextAttemptAt });
        builder.HasIndex(x => new { x.ReferenceId, x.FileId }).IsUnique();
    }
}
