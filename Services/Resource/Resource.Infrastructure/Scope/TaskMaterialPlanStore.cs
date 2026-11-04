using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Resource.Application.Scope;
using Resource.Domain.Scope;
using Resource.Infrastructure.Data;

namespace Resource.Infrastructure.Scope;

public sealed class TaskMaterialPlanStore(ResourceDbContext db) : ITaskMaterialPlanStore
{
    public Task<TaskMaterialPlan?> GetAsync(Guid taskId, CancellationToken cancellationToken) =>
        db.Set<TaskMaterialPlan>().Include(plan => plan.Items).SingleOrDefaultAsync(plan => plan.TaskId == taskId, cancellationToken);
    public void Add(TaskMaterialPlan plan) => db.Set<TaskMaterialPlan>().Add(plan);
    public Task SaveAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

public sealed class TaskMaterialPlanConfiguration : IEntityTypeConfiguration<TaskMaterialPlan>
{
    public void Configure(EntityTypeBuilder<TaskMaterialPlan> builder)
    {
        builder.ToTable("task_material_plan");
        builder.HasKey(plan => plan.TaskId);
        builder.Property(plan => plan.TaskId).HasColumnName("task_id").ValueGeneratedNever();
        builder.Property(plan => plan.WorkAreaId).HasColumnName("work_area_id");
        builder.Property(plan => plan.ParishId).HasColumnName("parish_id");
        builder.Property(plan => plan.SeasonId).HasColumnName("season_id");
        builder.Property(plan => plan.SourceVersion).HasColumnName("source_version");
        builder.Property(plan => plan.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(plan => plan.WorkAreaId).HasDatabaseName("ix_task_material_plan_work_area_id");
        builder.HasMany(plan => plan.Items).WithOne().HasForeignKey(item => item.TaskId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(plan => plan.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
public sealed class TaskMaterialPlanItemConfiguration : IEntityTypeConfiguration<TaskMaterialPlanItem>
{
    public void Configure(EntityTypeBuilder<TaskMaterialPlanItem> builder)
    {
        builder.ToTable("task_material_plan_item", table => table.HasCheckConstraint("task_material_plan_quantity_positive", "required_quantity > 0"));
        builder.HasKey(item => new { item.TaskId, item.MaterialId });
        builder.Property(item => item.TaskId).HasColumnName("task_id");
        builder.Property(item => item.MaterialId).HasColumnName("material_id");
        builder.Property(item => item.RequiredQuantity).HasColumnName("required_quantity").HasPrecision(12, 2);
        builder.Property(item => item.Note).HasColumnName("material_note").HasMaxLength(2000);
    }
}
