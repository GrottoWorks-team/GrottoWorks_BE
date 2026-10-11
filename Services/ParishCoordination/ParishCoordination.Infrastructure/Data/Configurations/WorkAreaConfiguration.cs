using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Infrastructure.Data.Configurations;

public sealed class WorkAreaConfiguration : IEntityTypeConfiguration<WorkArea>
{
    public void Configure(EntityTypeBuilder<WorkArea> builder)
    {
        builder.ToTable("work_area");

        builder.HasKey(workArea => workArea.Id);

        builder.Property(workArea => workArea.Id)
            .HasColumnName("work_area_id");

        builder.Property(workArea => workArea.SeasonId)
            .HasColumnName("season_id")
            .IsRequired();

        builder.Property(workArea => workArea.CommunityId)
            .HasColumnName("community_id");

        builder.Property(workArea => workArea.WorkAreaCategoryId)
            .HasColumnName("work_area_category_id");

        builder.Property(workArea => workArea.LeaderUserId)
            .HasColumnName("leader_user_id");

        builder.Property(workArea => workArea.MaterialOfficerUserId)
            .HasColumnName("material_officer_user_id");

        builder.Property(workArea => workArea.Name)
            .HasColumnName("work_area_name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(workArea => workArea.Description)
            .HasColumnName("work_area_description");

        builder.Property(workArea => workArea.StartDate)
            .HasColumnName("start_date")
            .HasColumnType("date");

        builder.Property(workArea => workArea.Deadline)
            .HasColumnName("deadline")
            .HasColumnType("date");

        builder.Property(workArea => workArea.Status)
            .HasColumnName("work_area_status")
            .HasConversion(
                status => status.ToString().ToUpperInvariant(),
                value => Enum.Parse<WorkAreaStatus>(value, true))
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(workArea => workArea.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(workArea => workArea.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(workArea => workArea.SeasonId);

        builder.HasOne<Season>()
            .WithMany()
            .HasForeignKey(workArea => workArea.SeasonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Community>()
            .WithMany()
            .HasForeignKey(workArea => workArea.CommunityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}