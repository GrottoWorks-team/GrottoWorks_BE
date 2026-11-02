using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Reporting.Domain.Scope;

namespace Reporting.Infrastructure.Data.Configurations;

/// <summary><c>season_scope</c> — projection of ParishCoordination seasons (spec §2.2).</summary>
public sealed class SeasonScopeConfiguration : IEntityTypeConfiguration<SeasonScope>
{
    public void Configure(EntityTypeBuilder<SeasonScope> builder)
    {
        builder.ToTable("season_scope");
        builder.HasKey(season => season.SeasonId);
        builder.Property(season => season.SeasonId).HasColumnName("season_id").ValueGeneratedNever();
        builder.Property(season => season.Status).HasColumnName("season_status").HasConversion(EnumText.Converter<SeasonStatus>()).HasMaxLength(30).IsRequired();
        builder.Property(season => season.SourceVersion).HasColumnName("source_version").IsRequired();
        builder.Property(season => season.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired();
    }
}
