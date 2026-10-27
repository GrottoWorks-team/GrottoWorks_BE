using Microsoft.EntityFrameworkCore;
using Reporting.Domain.Recognition;
using Reporting.Domain.Scope;

namespace Reporting.Infrastructure.Data;

/// <summary>reporting_db — tables owned by the Reporting service (spec section 3, ERD v3.6.2).</summary>
public sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> options) : DbContext(options)
{
    public DbSet<WorkAreaScope> WorkAreaScopes => Set<WorkAreaScope>();

    public DbSet<TaskProjection> TaskProjections => Set<TaskProjection>();

    public DbSet<ContributionType> ContributionTypes => Set<ContributionType>();

    public DbSet<RecognitionRule> RecognitionRules => Set<RecognitionRule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReportingDbContext).Assembly);
    }
}
