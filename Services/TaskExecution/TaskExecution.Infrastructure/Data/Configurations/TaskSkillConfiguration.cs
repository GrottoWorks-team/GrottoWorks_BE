using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskExecution.Domain.Tasks;

namespace TaskExecution.Infrastructure.Data.Configurations;

public sealed class TaskSkillConfiguration : IEntityTypeConfiguration<TaskSkill>
{
    public void Configure(EntityTypeBuilder<TaskSkill> builder)
    {
        builder.ToTable("task_skill");
        builder.HasKey(skill => skill.Id);
        builder.Property(skill => skill.Id).HasColumnName("task_skill_id").ValueGeneratedNever();
        builder.Property(skill => skill.TaskId).HasColumnName("task_id").IsRequired();
        builder.Property(skill => skill.SkillId).HasColumnName("skill_id").IsRequired();
        builder.Property(skill => skill.RequiredSkillLevel)
            .HasColumnName("required_skill_level")
            .HasMaxLength(30);
        builder.Property(skill => skill.RequiredVolunteerCount)
            .HasColumnName("required_volunteer_count");

        builder.HasIndex(skill => new { skill.TaskId, skill.SkillId })
            .IsUnique()
            .HasDatabaseName("ux_task_skill_task_id_skill_id");

        builder.HasOne<WorkTask>()
            .WithMany(task => task.Skills)
            .HasForeignKey(skill => skill.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
