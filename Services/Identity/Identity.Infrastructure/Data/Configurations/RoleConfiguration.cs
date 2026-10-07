using Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Data.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("role");

        builder.HasKey(role => role.RoleId);

        builder.Property(role => role.RoleId)
            .HasColumnName("role_id");

        builder.Property(role => role.Code)
            .HasColumnName("role_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(role => role.Code)
            .IsUnique()
            .HasDatabaseName("ux_role_role_code");

        builder.Property(role => role.Name)
            .HasColumnName("role_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(role => role.Description)
            .HasColumnName("role_description");
    }
}
