using Identity.Domain.Entities;
using Identity.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Data.Configurations;

public sealed class SkillConfiguration : IEntityTypeConfiguration<Skill>
{
    public void Configure(EntityTypeBuilder<Skill> builder)
    {
        builder.ToTable("skill");

        builder.HasKey(skill => skill.SkillId);

        builder.Property(skill => skill.SkillId)
            .HasColumnName("skill_id");

        builder.Property(skill => skill.Code)
            .HasColumnName("skill_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(skill => skill.Code)
            .IsUnique()
            .HasDatabaseName("ux_skill_skill_code");

        builder.Property(skill => skill.Name)
            .HasColumnName("skill_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(skill => skill.Description)
            .HasColumnName("skill_description");

        builder.Property(skill => skill.Status)
            .HasColumnName("skill_status")
            .HasConversion(
                status => status.ToString().ToUpperInvariant(),
                value => Enum.Parse<SkillStatus>(value, true))
            .HasMaxLength(30)
            .IsRequired();
    }
}
