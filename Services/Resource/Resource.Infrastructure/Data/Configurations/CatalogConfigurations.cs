using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Resource.Domain.Catalog;

namespace Resource.Infrastructure.Data.Configurations;

internal static class StatusConversion
{
    public static PropertyBuilder<CatalogStatus> AsCatalogStatus(this PropertyBuilder<CatalogStatus> property, string column) =>
        property.HasColumnName(column)
            .HasConversion(status => status.ToString().ToUpperInvariant(), value => Enum.Parse<CatalogStatus>(value, true))
            .HasMaxLength(30)
            .IsRequired();
}

public sealed class MaterialCategoryConfiguration : IEntityTypeConfiguration<MaterialCategory>
{
    public const string CodeIndex = "ux_material_category_category_code";

    public void Configure(EntityTypeBuilder<MaterialCategory> builder)
    {
        builder.ToTable("material_category");
        builder.HasKey(category => category.MaterialCategoryId);
        builder.Property(category => category.MaterialCategoryId).HasColumnName("material_category_id").ValueGeneratedNever();
        builder.Property(category => category.Code).HasColumnName("category_code").HasMaxLength(MaterialCategory.MaxCodeLength).IsRequired();
        builder.HasIndex(category => category.Code).IsUnique().HasDatabaseName(CodeIndex);
        builder.Property(category => category.Name).HasColumnName("category_name").HasMaxLength(MaterialCategory.MaxNameLength).IsRequired();
        builder.Property(category => category.Description).HasColumnName("category_description");
        builder.Property(category => category.Status).AsCatalogStatus("category_status");
    }
}

public sealed class MaterialConfiguration : IEntityTypeConfiguration<Material>
{
    public const string CodeIndex = "ux_material_material_code";

    public void Configure(EntityTypeBuilder<Material> builder)
    {
        builder.ToTable("material");
        builder.HasKey(material => material.MaterialId);
        builder.Property(material => material.MaterialId).HasColumnName("material_id").ValueGeneratedNever();
        builder.Property(material => material.MaterialCategoryId).HasColumnName("material_category_id").IsRequired();
        builder.Property(material => material.Code).HasColumnName("material_code").HasMaxLength(Material.MaxCodeLength).IsRequired();
        builder.HasIndex(material => material.Code).IsUnique().HasDatabaseName(CodeIndex);
        builder.Property(material => material.Name).HasColumnName("material_name").HasMaxLength(Material.MaxNameLength).IsRequired();
        builder.Property(material => material.Unit).HasColumnName("material_unit").HasMaxLength(Material.MaxUnitLength).IsRequired();
        builder.Property(material => material.Description).HasColumnName("material_description");
        builder.Property(material => material.Status).AsCatalogStatus("material_status");
        builder.HasIndex(material => material.MaterialCategoryId).HasDatabaseName("ix_material_material_category_id");

        // Same service, same database: a real FK (cross-service references are the ones without FKs).
        builder.HasOne<MaterialCategory>()
            .WithMany()
            .HasForeignKey(material => material.MaterialCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
