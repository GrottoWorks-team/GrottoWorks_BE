using Microsoft.EntityFrameworkCore;
using TaskExecution.Domain.Scopes;
using TaskExecution.Domain.Tasks;

namespace TaskExecution.Infrastructure.Data;

/// <summary>task_db - data owned exclusively by TaskExecution.</summary>
public sealed class TaskExecutionDbContext(DbContextOptions<TaskExecutionDbContext> options) : DbContext(options)
{
    public DbSet<SeasonScope> SeasonScopes => Set<SeasonScope>();
    public DbSet<WorkAreaScope> WorkAreaScopes => Set<WorkAreaScope>();
    public DbSet<WorkTask> Tasks => Set<WorkTask>();
    public DbSet<TaskSkill> TaskSkills => Set<TaskSkill>();
    public DbSet<TaskExecution.Domain.Assignments.TaskAssignment> Assignments => Set<TaskExecution.Domain.Assignments.TaskAssignment>();
    public DbSet<TaskExecution.Infrastructure.Events.TaskEventOutboxMessage> EventOutbox => Set<TaskExecution.Infrastructure.Events.TaskEventOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TaskExecutionDbContext).Assembly);
    }
}
