using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskExecution.Domain.Tasks;

namespace TaskExecution.Infrastructure.Data.Configurations;

public sealed class TaskMaterialConfiguration : IEntityTypeConfiguration<TaskMaterial>
{
    public void Configure(EntityTypeBuilder<TaskMaterial> builder)
    {
        builder.ToTable("task_material", table => table.HasCheckConstraint("task_material_positive_quantity", "required_quantity > 0"));
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasColumnName("task_material_id").ValueGeneratedNever();
        builder.Property(row => row.TaskId).HasColumnName("task_id");
        builder.Property(row => row.MaterialId).HasColumnName("material_id");
        builder.Property(row => row.RequiredQuantity).HasColumnName("required_quantity").HasPrecision(12, 2);
        builder.Property(row => row.Note).HasColumnName("material_note").HasMaxLength(2000);
        builder.HasIndex(row => new { row.TaskId, row.MaterialId }).IsUnique().HasDatabaseName("ux_task_material_task_id_material_id");
        builder.HasOne<WorkTask>().WithMany(task => task.Materials).HasForeignKey(row => row.TaskId).OnDelete(DeleteBehavior.Cascade);
    }
}
