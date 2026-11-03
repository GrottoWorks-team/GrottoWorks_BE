using Microsoft.EntityFrameworkCore;
using TaskExecution.Domain.Scopes;
using TaskExecution.Domain.Tasks;
using TaskExecution.Domain.Assignments;
using TaskExecution.Infrastructure.Events;

namespace TaskExecution.Infrastructure.Data;

/// <summary>task_db - data owned exclusively by TaskExecution.</summary>
public sealed class TaskExecutionDbContext(DbContextOptions<TaskExecutionDbContext> options) : DbContext(options)
{
    public DbSet<SeasonScope> SeasonScopes => Set<SeasonScope>();
    public DbSet<TaskExecution.Infrastructure.Scopes.ScopeInboxMessage> ScopeEventInbox => Set<TaskExecution.Infrastructure.Scopes.ScopeInboxMessage>();
    public DbSet<UserStatusProjection> UserStatusProjections => Set<UserStatusProjection>();
    public DbSet<WorkAreaScope> WorkAreaScopes => Set<WorkAreaScope>();
    public DbSet<WorkTask> Tasks => Set<WorkTask>();
    public DbSet<TaskSkill> TaskSkills => Set<TaskSkill>();
    public DbSet<TaskMaterial> TaskMaterials => Set<TaskMaterial>();
    public DbSet<TaskExecution.Domain.Support.SupportRequest> SupportRequests => Set<TaskExecution.Domain.Support.SupportRequest>();
    public DbSet<TaskExecution.Application.Attendance.AttendanceSessionAlert> AttendanceAlerts => Set<TaskExecution.Application.Attendance.AttendanceSessionAlert>();
    public DbSet<TaskExecution.Domain.Attendance.AttendanceCorrection> AttendanceCorrections => Set<TaskExecution.Domain.Attendance.AttendanceCorrection>();
    public DbSet<TaskExecution.Domain.Attendance.AttendanceRequest> AttendanceRequests => Set<TaskExecution.Domain.Attendance.AttendanceRequest>();
    public DbSet<TaskExecution.Domain.Attendance.AttendanceSession> AttendanceSessions => Set<TaskExecution.Domain.Attendance.AttendanceSession>();
    public DbSet<TaskExecution.Application.Attendance.AttendanceReplay> AttendanceReplays => Set<TaskExecution.Application.Attendance.AttendanceReplay>();
    public DbSet<TaskExecution.Domain.Assignments.TaskAssignment> Assignments => Set<TaskExecution.Domain.Assignments.TaskAssignment>();
    public DbSet<TaskExecution.Infrastructure.Events.TaskEventOutboxMessage> EventOutbox => Set<TaskExecution.Infrastructure.Events.TaskEventOutboxMessage>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        SaveChangesAsync(acceptAllChangesOnSuccess).GetAwaiter().GetResult();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        SaveChangesAsync(true, cancellationToken);

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        var changedTasks = ChangeTracker.Entries<WorkTask>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .Select(entry => entry.Entity).ToArray();
        if (changedTasks.Length > 0)
            await StageTaskSnapshotsAsync(changedTasks, cancellationToken);

        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private async Task StageTaskSnapshotsAsync(WorkTask[] tasks, CancellationToken cancellationToken)
    {
        var taskIds = tasks.Select(task => task.Id).ToArray();
        var areaIds = tasks.Select(task => task.WorkAreaId).Distinct().ToArray();
        var areas = await WorkAreaScopes.Where(area => areaIds.Contains(area.WorkAreaId))
            .ToDictionaryAsync(area => area.WorkAreaId, cancellationToken);
        foreach (var area in WorkAreaScopes.Local) areas[area.WorkAreaId] = area;
        var seasonIds = areas.Values.Select(area => area.SeasonId).Distinct().ToArray();
        var seasons = await SeasonScopes.Where(season => seasonIds.Contains(season.SeasonId))
            .ToDictionaryAsync(season => season.SeasonId, cancellationToken);
        foreach (var season in SeasonScopes.Local) seasons[season.SeasonId] = season;

        var assignments = await Assignments.AsNoTracking().Where(row => taskIds.Contains(row.TaskId))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        // Include changes in this unit of work, not only the previous committed membership.
        foreach (var entry in ChangeTracker.Entries<TaskAssignment>())
        {
            if (!taskIds.Contains(entry.Entity.TaskId)) continue;
            if (entry.State == EntityState.Deleted) assignments.Remove(entry.Entity.Id);
            else assignments[entry.Entity.Id] = entry.Entity;
        }

        var materials = await TaskMaterials.AsNoTracking().Where(row => taskIds.Contains(row.TaskId))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        foreach (var entry in ChangeTracker.Entries<TaskMaterial>())
        {
            if (!taskIds.Contains(entry.Entity.TaskId)) continue;
            if (entry.State == EntityState.Deleted) materials.Remove(entry.Entity.Id);
            else materials[entry.Entity.Id] = entry.Entity;
        }

        var capturedAt = DateTimeOffset.UtcNow;
        var snapshots = new List<TaskExecution.Contracts.Events.TaskScopeSnapshot>(tasks.Length);
        foreach (var task in tasks)
        {
            if (!areas.TryGetValue(task.WorkAreaId, out var area) || !seasons.TryGetValue(area.SeasonId, out var season))
                throw new InvalidOperationException("Cannot persist a task without its local parent scopes.");
            snapshots.Add(TaskSnapshotMapper.Map(task, area, season, assignments.Values, capturedAt, materials.Values));
        }

        var snapshotIds = snapshots.Select(snapshot => snapshot.EventId).ToArray();
        var existingIds = (await EventOutbox.Where(row => snapshotIds.Contains(row.Id))
            .Select(row => row.Id).ToListAsync(cancellationToken)).ToHashSet();
        existingIds.UnionWith(EventOutbox.Local.Select(row => row.Id));
        foreach (var snapshot in snapshots)
        {
            if (!existingIds.Add(snapshot.EventId)) continue;
            EventOutbox.Add(TaskEventOutboxMessage.Create(snapshot));
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TaskExecutionDbContext).Assembly);
    }
}
