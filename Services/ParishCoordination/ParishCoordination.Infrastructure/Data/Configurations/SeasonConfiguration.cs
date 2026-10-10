using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Infrastructure.Data.Configurations;

public sealed class SeasonConfiguration : IEntityTypeConfiguration<Season>
{
    public void Configure(EntityTypeBuilder<Season> builder)
    {
        builder.ToTable("christmas_season");

        builder.HasKey(season => season.Id);

        builder.Property(season => season.Id)
            .HasColumnName("season_id");

        builder.Property(season => season.ParishId)
            .HasColumnName("parish_id")
            .IsRequired();

        builder.Property(season => season.CreatedByUserId)
            .HasColumnName("created_by_user_id")
            .IsRequired();

        builder.Property(season => season.Name)
            .HasColumnName("season_name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(season => season.SeasonYear)
            .HasColumnName("season_year")
            .IsRequired();

        builder.Property(season => season.StartDate)
            .HasColumnName("start_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(season => season.EndDate)
            .HasColumnName("end_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(season => season.Status)
            .HasColumnName("season_status")
            .HasConversion(
                status => status.ToString().ToUpperInvariant(),
                value => Enum.Parse<SeasonStatus>(value, true))
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(season => season.Description)
            .HasColumnName("season_description");

        builder.Property(season => season.EstimatedBudget)
            .HasColumnName("estimated_budget")
            .HasPrecision(14, 2);

        builder.Property(season => season.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(season => season.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(season => new { season.ParishId, season.SeasonYear })
            .IsUnique();

        builder.HasIndex(season => new { season.ParishId, season.Name })
            .IsUnique();

        builder.HasOne<Parish>()
            .WithMany()
            .HasForeignKey(season => season.ParishId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
