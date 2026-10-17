using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Infrastructure.Data.Configurations;

public sealed class WorkAreaCategoryConfiguration
    : IEntityTypeConfiguration<WorkAreaCategory>
{
    public void Configure(EntityTypeBuilder<WorkAreaCategory> builder)
    {
        builder.ToTable("work_area_category");

        builder.HasKey(category => category.Id);

        builder.Property(category => category.Id)
            .HasColumnName("work_area_category_id");

        builder.Property(category => category.Code)
            .HasColumnName("category_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(category => category.Name)
            .HasColumnName("category_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(category => category.Description)
            .HasColumnName("category_description");

        builder.Property(category => category.Status)
            .HasColumnName("category_status")
            .HasConversion(
                status => status.ToString().ToUpperInvariant(),
                value => Enum.Parse<WorkAreaCategoryStatus>(value, true))
            .HasMaxLength(30)
            .IsRequired();

        builder.HasIndex(category => category.Code)
            .IsUnique();
    }
}
