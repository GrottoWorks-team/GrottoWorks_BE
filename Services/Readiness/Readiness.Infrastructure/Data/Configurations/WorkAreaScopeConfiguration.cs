using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Readiness.Domain.Scope;

namespace Readiness.Infrastructure.Data.Configurations;

/// <summary>
/// <c>work_area_scope</c> — projection of ParishCoordination work areas (spec §2.2).
/// Ids are references to another service: no FKs.
/// </summary>
public sealed class WorkAreaScopeConfiguration : IEntityTypeConfiguration<WorkAreaScope>
{
    public void Configure(EntityTypeBuilder<WorkAreaScope> builder)
    {
        builder.ToTable("work_area_scope");
        builder.HasKey(scope => scope.WorkAreaId);
        builder.Property(scope => scope.WorkAreaId).HasColumnName("work_area_id").ValueGeneratedNever();
        builder.Property(scope => scope.SeasonId).HasColumnName("season_id").IsRequired();
        builder.Property(scope => scope.ParishId).HasColumnName("parish_id").IsRequired();
        builder.Property(scope => scope.Name).HasColumnName("work_area_name").HasMaxLength(150).IsRequired();
        builder.Property(scope => scope.Status).HasColumnName("work_area_status").HasConversion(EnumText.Converter<WorkAreaStatus>()).HasMaxLength(30).IsRequired();
        builder.Property(scope => scope.SeasonStatus).HasColumnName("season_status").HasConversion(EnumText.Converter<SeasonStatus>()).HasMaxLength(30).IsRequired();
        builder.Property(scope => scope.CommunityId).HasColumnName("community_id");
        builder.Property(scope => scope.LeaderUserId).HasColumnName("leader_user_id");
        builder.Property(scope => scope.MaterialOfficerUserId).HasColumnName("material_officer_user_id");
        builder.Property(scope => scope.SourceVersion).HasColumnName("source_version").IsRequired();
        builder.Property(scope => scope.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired();

        builder.HasIndex(scope => scope.SeasonId).HasDatabaseName("ix_work_area_scope_season_id");
        builder.HasIndex(scope => scope.ParishId).HasDatabaseName("ix_work_area_scope_parish_id");
    }
}
