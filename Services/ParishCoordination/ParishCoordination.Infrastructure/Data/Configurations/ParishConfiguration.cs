using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParishCoordination.Domain.Entities;

namespace ParishCoordination.Infrastructure.Data.Configurations;

public sealed class ParishConfiguration : IEntityTypeConfiguration<Parish>
{
    public void Configure(EntityTypeBuilder<Parish> builder)
    {
        builder.ToTable("parish");

        builder.HasKey(parish => parish.Id);

        builder.Property(parish => parish.Id)
            .HasColumnName("parish_id");

        builder.Property(parish => parish.Name)
            .HasColumnName("parish_name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(parish => parish.Address)
            .HasColumnName("parish_address")
            .HasMaxLength(255);

        builder.Property(parish => parish.Description)
            .HasColumnName("parish_description");

        builder.Property(parish => parish.Status)
            .HasColumnName("parish_status")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(parish => parish.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(parish => parish.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");
    }
}
