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
    public DbSet<TaskExecution.Application.Attendance.AttendanceSessionAlert> AttendanceAlerts => Set<TaskExecution.Application.Attendance.AttendanceSessionAlert>();
    public DbSet<TaskExecution.Domain.Attendance.AttendanceCorrection> AttendanceCorrections => Set<TaskExecution.Domain.Attendance.AttendanceCorrection>();
    public DbSet<TaskExecution.Domain.Attendance.AttendanceRequest> AttendanceRequests => Set<TaskExecution.Domain.Attendance.AttendanceRequest>();
    public DbSet<TaskExecution.Domain.Attendance.AttendanceSession> AttendanceSessions => Set<TaskExecution.Domain.Attendance.AttendanceSession>();
    public DbSet<TaskExecution.Application.Attendance.AttendanceReplay> AttendanceReplays => Set<TaskExecution.Application.Attendance.AttendanceReplay>();
    public DbSet<TaskExecution.Domain.Assignments.TaskAssignment> Assignments => Set<TaskExecution.Domain.Assignments.TaskAssignment>();
    public DbSet<TaskExecution.Infrastructure.Events.TaskEventOutboxMessage> EventOutbox => Set<TaskExecution.Infrastructure.Events.TaskEventOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TaskExecutionDbContext).Assembly);
    }
}
