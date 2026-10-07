using Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Data.Configurations;

public sealed class VolunteerSkillConfiguration : IEntityTypeConfiguration<VolunteerSkill>
{
    public void Configure(EntityTypeBuilder<VolunteerSkill> builder)
    {
        builder.ToTable("volunteer_skill");

        builder.HasKey(link => link.VolunteerSkillId);

        builder.Property(link => link.VolunteerSkillId)
            .HasColumnName("volunteer_skill_id");

        builder.Property(link => link.VolunteerUserId)
            .HasColumnName("volunteer_user_id")
            .IsRequired();

        builder.Property(link => link.SkillId)
            .HasColumnName("skill_id")
            .IsRequired();

        builder.HasIndex(link => new { link.VolunteerUserId, link.SkillId })
            .IsUnique()
            .HasDatabaseName("ux_volunteer_skill_user_skill");

        builder.Property(link => link.SkillLevel)
            .HasColumnName("skill_level")
            .HasMaxLength(30);

        builder.Property(link => link.SkillNote)
            .HasColumnName("skill_note");

        builder.HasOne(link => link.Skill)
            .WithMany()
            .HasForeignKey(link => link.SkillId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
