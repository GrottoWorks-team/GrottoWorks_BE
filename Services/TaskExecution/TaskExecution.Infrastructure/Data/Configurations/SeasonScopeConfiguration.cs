using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskExecution.Domain.Scopes;

namespace TaskExecution.Infrastructure.Data.Configurations;

public sealed class SeasonScopeConfiguration : IEntityTypeConfiguration<SeasonScope>
{
    public void Configure(EntityTypeBuilder<SeasonScope> builder)
    {
        builder.ToTable("season_scope");
        builder.HasKey(scope => scope.SeasonId);
        builder.Property(scope => scope.SeasonId).HasColumnName("season_id").ValueGeneratedNever();
        builder.Property(scope => scope.ParishId).HasColumnName("parish_id").IsRequired();
        builder.Property(scope => scope.StartDate).HasColumnName("start_date").HasColumnType("date").IsRequired();
        builder.Property(scope => scope.EndDate).HasColumnName("end_date").HasColumnType("date").IsRequired();
        builder.Property(scope => scope.Status).HasColumnName("season_status")
            .HasConversion(EnumText.Converter<SeasonScopeStatus>()).HasMaxLength(30).IsRequired();
        builder.Property(scope => scope.SourceVersion).HasColumnName("source_version").IsRequired();
        builder.Property(scope => scope.UpdatedAt).HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone").IsRequired();
        builder.Ignore(scope => scope.IsReadOnly);

        builder.HasIndex(scope => scope.ParishId).HasDatabaseName("ix_season_scope_parish_id");
    }
}
