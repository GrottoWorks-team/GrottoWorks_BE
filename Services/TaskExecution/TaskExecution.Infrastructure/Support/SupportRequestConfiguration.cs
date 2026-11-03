using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskExecution.Domain.Support;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.Infrastructure.Support;

public sealed class SupportRequestConfiguration : IEntityTypeConfiguration<SupportRequest>
{
    public void Configure(EntityTypeBuilder<SupportRequest> b)
    {
        b.ToTable("support_request");b.HasKey(r=>r.Id);b.Property(r=>r.Id).HasColumnName("support_request_id").ValueGeneratedNever();
        b.Property(r=>r.SeasonId).HasColumnName("season_id");b.Property(r=>r.WorkAreaId).HasColumnName("work_area_id");
        b.Property(r=>r.RequestedBy).HasColumnName("requested_by_user_id");
        b.Property(r=>r.SupportType).HasColumnName("support_type").HasMaxLength(30).HasConversion(EnumText.Converter<SupportType>());
        b.Property(r=>r.Description).HasColumnName("request_description").HasMaxLength(2000);b.Property(r=>r.Deadline).HasColumnName("deadline");
        b.Property(r=>r.Status).HasColumnName("request_status").HasMaxLength(30).HasConversion(EnumText.Converter<SupportRequestStatus>());
        b.Property(r=>r.CreatedAt).HasColumnName("created_at");b.Property(r=>r.UpdatedAt).HasColumnName("updated_at");
        b.Property(r=>r.Version).HasColumnName("version").HasDefaultValue(1).IsConcurrencyToken();
        b.HasIndex(r=>new {r.SeasonId,r.WorkAreaId});
        b.HasMany(r=>r.Positions).WithOne().HasForeignKey(p=>p.RequestId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(r=>r.Materials).WithOne().HasForeignKey(m=>m.RequestId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(r=>r.Positions).HasField("positions").UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(r=>r.Materials).HasField("materials").UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(r=>r.Responses).WithOne().HasForeignKey(r=>r.RequestId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_support_response_request");
        b.Navigation(r=>r.Responses).HasField("responses").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class SupportPositionConfiguration : IEntityTypeConfiguration<SupportPosition>
{
    public void Configure(EntityTypeBuilder<SupportPosition> b)
    {
        b.ToTable("support_request_position",t=>t.HasCheckConstraint("support_position_positive_count","required_volunteer_count>0"));b.HasKey(r=>r.Id);
        b.Property(r=>r.Id).HasColumnName("support_request_position_id").ValueGeneratedNever();b.Property(r=>r.RequestId).HasColumnName("support_request_id");
        b.HasAlternateKey(r=>new {r.RequestId,r.Id}).HasName("ak_support_position_request_id");
        b.Property(r=>r.Name).HasColumnName("position_name").HasMaxLength(150);b.Property(r=>r.RequiredVolunteerCount).HasColumnName("required_volunteer_count");
        b.Property(r=>r.Note).HasColumnName("position_note").HasMaxLength(2000);
        b.Property(r=>r.TaskId).HasColumnName("task_id");
        b.HasOne<TaskExecution.Domain.Tasks.WorkTask>().WithMany().HasForeignKey(r=>r.TaskId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_support_position_task");
        b.HasIndex(r=>r.TaskId).HasDatabaseName("ix_support_request_position_task_id");
        b.HasMany(r=>r.Skills).WithOne().HasForeignKey(s=>s.PositionId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(r=>r.Skills).HasField("skills").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class SupportSkillConfiguration : IEntityTypeConfiguration<SupportPositionSkill>
{
    public void Configure(EntityTypeBuilder<SupportPositionSkill> b)
    {
        b.ToTable("support_position_skill", table => table.HasCheckConstraint("support_skill_level_valid",
            "required_skill_level IS NULL OR required_skill_level IN ('BASIC','INTERMEDIATE','ADVANCED','EXPERT')"));
        b.HasKey(r=>new {r.PositionId,r.SkillId});
        b.Property(r=>r.PositionId).HasColumnName("support_request_position_id");b.Property(r=>r.SkillId).HasColumnName("skill_id");
        b.Property(r=>r.RequiredSkillLevel).HasColumnName("required_skill_level").HasMaxLength(30);
    }
}

public sealed class SupportMaterialConfiguration : IEntityTypeConfiguration<SupportMaterial>
{
    public void Configure(EntityTypeBuilder<SupportMaterial> b)
    {
        b.ToTable("support_request_material",t=>t.HasCheckConstraint("support_request_material_positive_quantity","required_quantity>0"));b.HasKey(r=>r.Id);
        b.Property(r=>r.Id).HasColumnName("support_request_material_id").ValueGeneratedNever();b.Property(r=>r.RequestId).HasColumnName("support_request_id");
        b.Property(r=>r.MaterialId).HasColumnName("material_id");b.Property(r=>r.RequiredQuantity).HasColumnName("required_quantity").HasPrecision(12,2);
        b.Property(r=>r.Note).HasColumnName("material_note").HasMaxLength(2000);
        b.HasIndex(r=>new {r.RequestId,r.MaterialId}).IsUnique();
    }
}
