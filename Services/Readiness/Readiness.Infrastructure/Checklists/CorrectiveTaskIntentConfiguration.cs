using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Readiness.Domain.Checklists;

namespace Readiness.Infrastructure.Checklists;

public sealed class CorrectiveTaskIntentConfiguration : IEntityTypeConfiguration<CorrectiveTaskIntent>
{
    public void Configure(EntityTypeBuilder<CorrectiveTaskIntent> builder)
    {
        builder.ToTable("corrective_task_intent");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Payload).HasColumnType("jsonb");
        builder.Property(x => x.Fingerprint).HasMaxLength(64);
        builder.Property(x => x.State).HasMaxLength(20);
        builder.HasIndex(x => new { x.State, x.NextAttemptAt });
    }
}
