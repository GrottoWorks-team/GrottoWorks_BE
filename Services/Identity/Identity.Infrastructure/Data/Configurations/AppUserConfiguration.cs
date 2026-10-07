using Identity.Domain.Entities;
using Identity.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Data.Configurations;

public sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("app_user");

        builder.HasKey(user => user.UserId);

        builder.Property(user => user.UserId)
            .HasColumnName("user_id");

        builder.Property(user => user.RoleId)
            .HasColumnName("role_id")
            .IsRequired();

        // Cross-service reference to ParishCoordination — deliberately no FK (spec 2.1, no cross-service FKs).
        builder.Property(user => user.ParishId)
            .HasColumnName("parish_id");

        builder.Property(user => user.FullName)
            .HasColumnName("full_name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(user => user.Email)
            .HasColumnName("email")
            .HasMaxLength(255)
            .IsRequired();

        builder.HasIndex(user => user.Email)
            .IsUnique()
            .HasDatabaseName("ux_app_user_email");

        builder.Property(user => user.Phone)
            .HasColumnName("phone")
            .HasMaxLength(30);

        builder.Property(user => user.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(500);

        builder.Property(user => user.Status)
            .HasColumnName("user_status")
            .HasConversion(
                status => status.ToString().ToUpperInvariant(),
                value => Enum.Parse<UserStatus>(value, true))
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(user => user.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(user => user.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");

        builder.HasOne(user => user.Role)
            .WithMany()
            .HasForeignKey(user => user.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(user => user.VolunteerProfile)
            .WithOne()
            .HasForeignKey<VolunteerProfile>(profile => profile.VolunteerUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(user => user.VolunteerSkills)
            .WithOne()
            .HasForeignKey(link => link.VolunteerUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(user => user.VolunteerAvailabilities)
            .WithOne()
            .HasForeignKey(slot => slot.VolunteerUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
