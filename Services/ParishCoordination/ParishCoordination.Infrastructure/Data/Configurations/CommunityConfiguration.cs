using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Infrastructure.Data.Configurations;

public sealed class CommunityConfiguration : IEntityTypeConfiguration<Community>
{
    public void Configure(EntityTypeBuilder<Community> builder)
    {
        builder.ToTable("community");

        builder.HasKey(community => community.Id);

        builder.Property(community => community.Id)
            .HasColumnName("community_id");

        builder.Property(community => community.ParishId)
            .HasColumnName("parish_id")
            .IsRequired();

        builder.Property(community => community.Name)
            .HasColumnName("community_name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(community => community.Description)
            .HasColumnName("community_description");

        builder.Property(community => community.Status)
            .HasColumnName("community_status")
            .HasConversion(
                status => status.ToString().ToUpperInvariant(),
                value => Enum.Parse<CommunityStatus>(value, true))
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(community => community.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(community => community.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(community => community.Version)
            .HasColumnName("version")
            .HasDefaultValue(1)
            .IsConcurrencyToken();

        builder.HasIndex(community => community.ParishId);

        builder.HasIndex(community => new { community.ParishId, community.Name })
            .IsUnique();

        builder.HasOne<Parish>()
            .WithMany()
            .HasForeignKey(community => community.ParishId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
