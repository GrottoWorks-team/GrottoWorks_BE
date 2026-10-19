using Microsoft.EntityFrameworkCore;
using TaskExecution.Domain.Scopes;

namespace TaskExecution.Infrastructure.Data;

/// <summary>task_db - data owned exclusively by TaskExecution.</summary>
public sealed class TaskExecutionDbContext(DbContextOptions<TaskExecutionDbContext> options) : DbContext(options)
{
    public DbSet<SeasonScope> SeasonScopes => Set<SeasonScope>();
    public DbSet<WorkAreaScope> WorkAreaScopes => Set<WorkAreaScope>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TaskExecutionDbContext).Assembly);
    }
}
