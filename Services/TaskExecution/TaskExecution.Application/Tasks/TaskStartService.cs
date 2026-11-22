using System.Text.Json;
using TaskExecution.Application.Assignments;
using TaskExecution.Application.Events;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Users;
using TaskExecution.Contracts.Events;
using TaskExecution.Domain;
using TaskExecution.Domain.Assignments;
using TaskExecution.Domain.Scopes;
using TaskExecution.Domain.Tasks;
using TaskState = TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.Application.Tasks;

public sealed record TaskStartReason(string Code, string Detail);
public sealed record TaskMaterialShortage(Guid MaterialId, decimal RequiredQuantity, decimal AllocatedQuantity);
public sealed record TaskStartEligibility(Guid TaskId, TaskType TaskType, TaskState Status, DateTimeOffset? StartAt,
    int AcceptedMembers, int RequiredMembers, bool MaterialsKnown, IReadOnlyList<TaskMaterialShortage> MaterialShortages,
    bool CanStart, IReadOnlyList<TaskStartReason> Blockers, IReadOnlyList<TaskStartReason> Warnings);
public sealed record TaskStartResult(TaskDto Task, IReadOnlyList<TaskStartReason> Warnings);

public sealed class TaskStartService(ITaskRepository tasks, IAssignmentStore assignments, TaskScopeAccess access,
    IScopeProjectionStore scopes, ActiveUserAccess users, ITaskResourceGateway resource, TaskEventRecorder events,
    BuildingBlocks.Security.ICurrentUser user, TimeProvider clock)
{
    private const string MaterialsUnavailable = "TASK_START_MATERIALS_UNAVAILABLE";

    public async Task<TaskStartEligibility> GetEligibilityAsync(Guid taskId, CancellationToken ct)
    {
        var task = await tasks.GetByIdAsync(taskId, ct) ?? throw MissingTask();
        var (area, season) = await LoadScopeAsync(task, ct);
        await users.EnsureCallerAsync(area.ParishId, ct);
        var accepted = await CountAcceptedAsync(taskId, ct);
        var shortages = await GetShortagesAsync(task, ct);
        return Evaluate(task, season, accepted, shortages, clock.GetUtcNow());
    }

    public async Task<TaskStartResult> StartAsync(Guid taskId, int? version, CancellationToken ct)
    {
        var initial = await tasks.GetByIdAsync(taskId, ct) ?? throw MissingTask();
        var (initialArea, initialSeason) = await LoadScopeAsync(initial, ct);
        await users.EnsureCallerAsync(initialArea.ParishId, ct);
        initial.EnsureVersion(version);
        var checkedVersion = initial.Version;
        var initialCount = await CountAcceptedAsync(taskId, ct);
        EnsureEligible(Evaluate(initial, initialSeason, initialCount, [], clock.GetUtcNow()));
        var shortages = await GetShortagesAsync(initial, ct);

        return await assignments.InTaskTransactionAsync(taskId, async () =>
        {
            var task = await tasks.GetByIdForUpdateAsync(taskId, ct) ?? throw MissingTask();
            var (area, season) = await LoadScopeAsync(task, ct);
            task.EnsureVersion(version);
            var accepted = await CountAcceptedAsync(taskId, ct);
            var now = clock.GetUtcNow();
            EnsureEligible(Evaluate(task, season, accepted, [], now));
            task.EnsureVersion(checkedVersion);
            var eligibility = Evaluate(task, season, accepted, shortages, now);
            EnsureEligible(eligibility);
            task.Start(user.UserId, now);
            events.Record(TaskEventTypes.StatusChanged, task, area, new
            {
                taskId, workAreaId = area.WorkAreaId, previousStatus = "NOT_STARTED", status = "IN_PROGRESS",
                startedByUserId = user.UserId, startedAt = now
            });
            await assignments.SaveAsync(ct);
            return new TaskStartResult(TaskDto.From(task), eligibility.Warnings);
        }, ct);
    }

    private async Task<(WorkAreaScope Area, SeasonScope Season)> LoadScopeAsync(WorkTask task, CancellationToken ct)
    {
        var area = await access.GetWorkAreaForLeaderReadAsync(task.WorkAreaId, ct);
        var season = await scopes.GetSeasonAsync(area.SeasonId, ct)
            ?? throw DomainRuleException.NotFound("SEASON_NOT_FOUND", "The owning season is missing.");
        return (area, season);
    }

    private async Task<int> CountAcceptedAsync(Guid taskId, CancellationToken ct) =>
        (await assignments.ListAsync(taskId, ct))
            .Where(row => row.Status is AssignmentStatus.Accepted or AssignmentStatus.InProgress)
            .Select(row => row.VolunteerId).Distinct().Count();

    private async Task<IReadOnlyList<TaskMaterialShortage>?> GetShortagesAsync(WorkTask task, CancellationToken ct)
    {
        if (task.Type == TaskType.Purchase || task.Materials.Count == 0) return [];

        JsonElement summary;
        try
        {
            summary = await resource.MaterialSummaryAsync(task.Id, ct);
        }
        catch (DomainRuleException exception) when (exception.Kind == DomainErrorKind.ServiceUnavailable)
        {
            return null;
        }

        if (summary.ValueKind != JsonValueKind.Object ||
            !TryReadId(summary, "taskId", out var taskId) || taskId != task.Id ||
            !TryReadId(summary, "workAreaId", out var areaId) || areaId != task.WorkAreaId ||
            !summary.TryGetProperty("requirementVersion", out var version) ||
            version.ValueKind != JsonValueKind.Number || !version.TryGetInt64(out var sourceVersion) || sourceVersion < 1 ||
            !summary.TryGetProperty("materials", out var rows) || rows.ValueKind != JsonValueKind.Array)
            return null;

        var materials = new Dictionary<Guid, TaskMaterialShortage>();
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || !TryReadId(row, "materialId", out var id) ||
                !TryReadQuantity(row, "requiredQuantity", out var required) ||
                !TryReadQuantity(row, "allocatedQuantity", out var allocated) ||
                !materials.TryAdd(id, new(id, required, allocated)))
                return null;
        }

        var shortages = new List<TaskMaterialShortage>();
        foreach (var requirement in task.Materials.OrderBy(row => row.MaterialId))
        {
            if (!materials.TryGetValue(requirement.MaterialId, out var line) ||
                line.RequiredQuantity != requirement.RequiredQuantity)
                return null;
            if (line.AllocatedQuantity < requirement.RequiredQuantity) shortages.Add(line);
        }

        if (materials.Values.Count(line => line.RequiredQuantity > 0) != task.Materials.Count) return null;
        return shortages;
    }

    private static bool TryReadId(JsonElement row, string name, out Guid id)
    {
        id = default;
        return row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
            value.TryGetGuid(out id) && id != Guid.Empty;
    }

    private static bool TryReadQuantity(JsonElement row, string name, out decimal quantity)
    {
        quantity = default;
        return row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetDecimal(out quantity) && quantity >= 0;
    }

    private static TaskStartEligibility Evaluate(WorkTask task, SeasonScope season, int accepted,
        IReadOnlyList<TaskMaterialShortage>? shortages, DateTimeOffset now)
    {
        var required = Math.Max(1, task.RequiredVolunteerCount);
        var blockers = new List<TaskStartReason>();
        if (task.Status != TaskState.NotStarted)
            blockers.Add(new(task.Status == TaskState.Open ? "TASK_NOT_PUBLISHED" : "TASK_INVALID_STATE",
                "Only a NOT_STARTED task can be started. Mark an OPEN task ready first."));
        if (season.Status != SeasonScopeStatus.Active)
            blockers.Add(new("TASK_START_SEASON_NOT_ACTIVE", "The owning season must be ACTIVE."));
        if (accepted < required)
            blockers.Add(new("TASK_NOT_ENOUGH_MEMBERS", $"The task needs {required} accepted volunteers and has {accepted}."));
        if (shortages is null)
            blockers.Add(new(MaterialsUnavailable, "Task material allocations could not be verified."));
        else if (shortages.Count != 0)
            blockers.Add(new("TASK_NOT_ENOUGH_MATERIALS", "Some required materials have not been fully allocated to the task."));

        TaskStartReason[] warnings = task.StartAt > now
            ? [new("TASK_START_NOT_DUE", "The scheduled start time has not arrived.")]
            : [];
        return new(task.Id, task.Type, task.Status, task.StartAt, accepted, required,
            shortages is not null, shortages ?? [], blockers.Count == 0, blockers, warnings);
    }

    private static void EnsureEligible(TaskStartEligibility eligibility)
    {
        if (eligibility.Blockers.FirstOrDefault() is { } blocker)
            throw new DomainRuleException(blocker.Code, blocker.Detail,
                blocker.Code == MaterialsUnavailable ? DomainErrorKind.ServiceUnavailable : DomainErrorKind.Conflict);
    }

    private static DomainRuleException MissingTask() =>
        DomainRuleException.NotFound("TASK_NOT_FOUND", "The task does not exist.");
}
