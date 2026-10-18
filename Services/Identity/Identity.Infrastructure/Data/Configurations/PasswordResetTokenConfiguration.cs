using Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Data.Configurations;

/// <summary>Technical table for F-IDN-04 (not part of ERD v3.6 — spec section 3, "bảng bổ sung").</summary>
public sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable(
            "password_reset_token",
            table => table.HasCheckConstraint(
                "password_reset_token_valid_ttl",
                "\"expires_at\" > \"created_at\""));

        builder.HasKey(token => token.PasswordResetTokenId);

        builder.Property(token => token.PasswordResetTokenId)
            .HasColumnName("password_reset_token_id");

        builder.Property(token => token.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(token => token.TokenHash)
            .HasColumnName("token_hash")
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(token => token.TokenHash)
            .IsUnique()
            .HasDatabaseName("ux_password_reset_token_token_hash");

        builder.HasIndex(token => token.UserId)
            .HasDatabaseName("ix_password_reset_token_user_id");

        builder.Property(token => token.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(token => token.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(token => token.UsedAt)
            .HasColumnName("used_at")
            .HasColumnType("timestamp with time zone");

        // Same bounded context: a real FK keeps orphan codes impossible (deleting users is not allowed anyway).
        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
