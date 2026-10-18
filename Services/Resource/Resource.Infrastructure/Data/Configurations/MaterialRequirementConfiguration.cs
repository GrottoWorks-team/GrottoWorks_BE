using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Resource.Domain.Catalog;
using Resource.Domain.Inventory;

namespace Resource.Infrastructure.Data.Configurations;

public sealed class MaterialRequirementConfiguration : IEntityTypeConfiguration<MaterialRequirement>
{
    public const string WorkAreaMaterialIndex = "ux_material_requirement_work_area_material";

    public void Configure(EntityTypeBuilder<MaterialRequirement> builder)
    {
        builder.ToTable("material_requirement", table =>
        {
            table.HasCheckConstraint("material_requirement_required_positive", "\"required_quantity\" > 0");
            table.HasCheckConstraint("material_requirement_existing_non_negative", "\"existing_quantity\" >= 0");
        });

        builder.HasKey(requirement => requirement.MaterialRequirementId);
        builder.Property(requirement => requirement.MaterialRequirementId).HasColumnName("material_requirement_id").ValueGeneratedNever();
        // work_area_id references ParishCoordination: no FK across services (spec 2.1).
        builder.Property(requirement => requirement.WorkAreaId).HasColumnName("work_area_id").IsRequired();
        builder.Property(requirement => requirement.MaterialId).HasColumnName("material_id").IsRequired();
        builder.Property(requirement => requirement.RequiredQuantity).HasColumnName("required_quantity").HasPrecision(12, 2).IsRequired();
        builder.Property(requirement => requirement.ExistingQuantity).HasColumnName("existing_quantity").HasPrecision(12, 2).HasDefaultValue(0m).IsRequired();
        builder.Property(requirement => requirement.Status).HasColumnName("requirement_status").HasConversion(EnumText.Converter<RequirementStatus>()).HasMaxLength(30).IsRequired();
        builder.Property(requirement => requirement.Note).HasColumnName("requirement_note");
        builder.Property(requirement => requirement.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(requirement => requirement.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        builder.Property(requirement => requirement.Version).IsRowVersion();

        builder.HasIndex(requirement => new { requirement.WorkAreaId, requirement.MaterialId }).IsUnique().HasDatabaseName(WorkAreaMaterialIndex);

        builder.HasOne<Material>().WithMany().HasForeignKey(requirement => requirement.MaterialId).OnDelete(DeleteBehavior.Restrict);
    }
}
