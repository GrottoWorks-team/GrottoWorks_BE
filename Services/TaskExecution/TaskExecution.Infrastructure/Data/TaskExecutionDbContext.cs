using Microsoft.EntityFrameworkCore;
using TaskExecution.Domain.Scopes;
using TaskExecution.Domain.Tasks;
using TaskExecution.Domain.Assignments;
using TaskExecution.Infrastructure.Events;

namespace TaskExecution.Infrastructure.Data;

/// <summary>task_db - data owned exclusively by TaskExecution.</summary>
public sealed class TaskExecutionDbContext(DbContextOptions<TaskExecutionDbContext> options,
    BuildingBlocks.Security.ICurrentUser? currentUser = null,
    TaskExecution.Application.Events.ITaskCorrelationContext? correlation = null) : DbContext(options)
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
            if(task.Type==TaskExecution.Domain.Tasks.TaskType.Purchase && !EventOutbox.Local.Any(row=>row.AggregateId==task.Id && row.EventType=="task.purchase-task.assigned"))
            {
                var newlyAccepted=ChangeTracker.Entries<TaskAssignment>().Where(row=>row.Entity.TaskId==task.Id &&
                    row.Entity.Status==TaskExecution.Domain.Assignments.AssignmentStatus.Accepted &&
                    (row.State==EntityState.Added || row.Property(x=>x.Status).OriginalValue!=row.Entity.Status)).ToArray();
                foreach(var accepted in newlyAccepted)
                    EventOutbox.Add(TaskEventOutboxMessage.Create(new TaskExecution.Contracts.Events.TaskIntegrationEvent(Guid.NewGuid(),
                        "task.purchase-task.assigned",1,capturedAt,correlation?.CorrelationId ?? $"task:{task.Id:D}:{task.Version}",
                        currentUser?.UserId ?? task.CreatedByUserId,area.ParishId,area.SeasonId,"task",task.Id,task.Version,
                        System.Text.Json.JsonSerializer.SerializeToElement(new {taskId=task.Id,purchaseRequestId=task.PurchaseRequestId,
                            workAreaId=area.WorkAreaId,buyerUserId=accepted.Entity.VolunteerId,assignmentId=accepted.Entity.Id,leaderUserId=area.LeaderUserId}))));
            }
            var entry=Entry(task);
            var created=entry.State==EntityState.Added;
            var statusChanged=!created && entry.Property(row=>row.Status).OriginalValue!=task.Status;
            var eventType=created ? "task.created" : statusChanged ? "task.status.changed" : "task.updated";
            if(!EventOutbox.Local.Any(row=>row.EventType==eventType && row.AggregateId==task.Id && row.ToMessage().AggregateVersion==task.Version))
            {
                var recipients=assignments.Values.Where(row=>row.TaskId==task.Id && row.Status is
                    TaskExecution.Domain.Assignments.AssignmentStatus.Pending or TaskExecution.Domain.Assignments.AssignmentStatus.Accepted or
                    TaskExecution.Domain.Assignments.AssignmentStatus.InProgress or TaskExecution.Domain.Assignments.AssignmentStatus.Completed)
                    .Select(row=>row.VolunteerId).Concat(area.LeaderUserId.HasValue ? new[]{area.LeaderUserId.Value} : []).Distinct().ToArray();
                EventOutbox.Add(TaskEventOutboxMessage.Create(new TaskExecution.Contracts.Events.TaskIntegrationEvent(
                    Guid.NewGuid(),eventType,1,capturedAt,correlation?.CorrelationId ?? $"task:{task.Id:D}:{task.Version}",
                    currentUser?.UserId is { } actor && actor!=Guid.Empty ? actor : task.CreatedByUserId,area.ParishId,area.SeasonId,
                    "task",task.Id,task.Version,System.Text.Json.JsonSerializer.SerializeToElement(new {
                        taskId=task.Id,workAreaId=task.WorkAreaId,taskType=TaskExecution.Infrastructure.Data.EnumText.ToText(task.Type),
                        status=TaskExecution.Infrastructure.Data.EnumText.ToText(task.Status),leaderUserId=area.LeaderUserId,
                        recipientUserIds=recipients,reason=task.CancellationReason,purchaseRequestId=task.PurchaseRequestId,
                        notificationSuperseded=EventOutbox.Local.Any(row=>row.AggregateId==task.Id && row.EventType=="task.completion.reviewed"),
                        previousStatus=created ? null : TaskExecution.Infrastructure.Data.EnumText.ToText(entry.Property(row=>row.Status).OriginalValue)
                    },new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)))));
            }
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
