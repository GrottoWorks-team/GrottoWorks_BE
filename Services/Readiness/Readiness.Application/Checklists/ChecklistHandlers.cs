using BuildingBlocks.Security;
using Readiness.Application.Abstractions;
using Readiness.Application.Scope;
using Readiness.Contracts.Events;
using Readiness.Domain;
using Readiness.Domain.Checklists;
using Readiness.Domain.Scope;

namespace Readiness.Application.Checklists;

public sealed record ReadinessItemDto(
    Guid Id,
    string Name,
    string? Description,
    bool Mandatory,
    ItemStatus Status,
    Guid? CorrectiveTaskId,
    Guid? CheckedBy,
    DateTimeOffset? CheckedAt,
    string? Note,
    IReadOnlyList<Guid> EvidenceFileIds,
    long CheckVersion)
{
    /// <param name="checkVersion">Version of the checklist after the change, for the next <c>If-Match</c>.</param>
    public static ReadinessItemDto From(ReadinessItem item, long checkVersion) =>
        new(item.ReadinessItemId, item.Name, item.Description, item.Mandatory, item.Status, item.CorrectiveTaskId, item.CheckedByUserId,
            item.CheckedAt, item.Note, item.EvidenceFileIds, checkVersion);
}

/// <summary><c>ReadinessCheck</c> (API Contract v1.3.2).</summary>
public sealed record ReadinessCheckDto(
    Guid Id,
    Guid WorkAreaId,
    string WorkAreaName,
    Guid SeasonId,
    string Name,
    ChecklistStatus Status,
    string? ReopenReason,
    Guid? CompletedBy,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<ReadinessItemDto> Items,
    long Version)
{
    public static ReadinessCheckDto From(ReadinessChecklist checklist, WorkAreaScope scope) =>
        new(checklist.ReadinessChecklistId, checklist.WorkAreaId, scope.Name, scope.SeasonId, checklist.Name, checklist.Status,
            checklist.ReopenReason, checklist.CompletedByUserId, checklist.CompletedAt,
            checklist.Items.OrderBy(item => item.Position).Select(item => ReadinessItemDto.From(item, checklist.Version)).ToList(), checklist.Version);
}

/// <summary>One work area in <c>SeasonReadiness</c>.</summary>
public sealed record WorkAreaReadinessDto(
    Guid WorkAreaId,
    string WorkAreaName,
    WorkAreaStatus WorkAreaStatus,
    Guid? ReadinessCheckId,
    ChecklistStatus? ChecklistStatus,
    int TotalItems,
    int MandatoryItems,
    int PassedItems,
    int FailedItems,
    int PendingItems,
    bool Ready);

/// <summary><c>SeasonReadiness</c> (F-RDY-06).</summary>
public sealed record SeasonReadinessDto(Guid SeasonId, int WorkAreas, int ReadyWorkAreas, IReadOnlyList<WorkAreaReadinessDto> Areas);

public sealed record CreateReadinessCheckCommand(Guid WorkAreaId, string? Name, IReadOnlyList<ItemDefinition> Items);

public sealed record ReplaceItemsCommand(Guid CheckId, long? Version, IReadOnlyList<ItemDefinition> Items);

public sealed record ItemResultCommand(Guid CheckId, Guid ItemId, long? Version, InspectionResult Result, string? Note, IReadOnlyList<Guid> EvidenceFileIds);

public sealed record LinkCorrectiveTaskCommand(Guid CheckId, Guid ItemId, long? Version, Guid? TaskId);

public sealed record AttachEvidenceCommand(Guid CheckId, Guid ItemId, IReadOnlyList<Guid> FileIds);

public sealed record CheckCommand(Guid CheckId, long? Version, string? Reason = null);

public interface IReadinessCheckStore
{
    /// <summary>Tracked, with items.</summary>
    Task<ReadinessChecklist?> GetAsync(Guid checkId, CancellationToken cancellationToken = default);

    Task<bool> ExistsForWorkAreaAsync(Guid workAreaId, CancellationToken cancellationToken = default);

    /// <summary>With items, read-only.</summary>
    Task<IReadOnlyList<ReadinessChecklist>> ListByWorkAreasAsync(IReadOnlyCollection<Guid> workAreaIds, CancellationToken cancellationToken = default);

    /// <summary>Tracked: checklists with an item linked to the corrective task.</summary>
    Task<IReadOnlyList<ReadinessChecklist>> ListByCorrectiveTaskAsync(Guid taskId, CancellationToken cancellationToken = default);

    void Add(ReadinessChecklist checklist);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Loads a checklist with its work area, applying parish tenancy (404 outside the parish).</summary>
public sealed class ChecklistLoader(IReadinessCheckStore store, WorkAreaAccess access)
{
    public async Task<(ReadinessChecklist Checklist, WorkAreaScope Scope)> ForReadAsync(Guid checkId, CancellationToken cancellationToken)
    {
        var checklist = await store.GetAsync(checkId, cancellationToken) ?? throw NotFound();
        try
        {
            return (checklist, await access.ForReadAsync(checklist.WorkAreaId, cancellationToken));
        }
        catch (DomainRuleException exception) when (exception.Kind == DomainErrorKind.NotFound)
        {
            throw NotFound();
        }
    }

    /// <summary>The leader of the checklist's work area (BR-29 pattern), season writable.</summary>
    public async Task<(ReadinessChecklist Checklist, WorkAreaScope Scope)> ForLeaderAsync(Guid checkId, long? version, CancellationToken cancellationToken)
    {
        var (checklist, _) = await ForReadAsync(checkId, cancellationToken);
        var scope = await access.ForLeaderWriteAsync(checklist.WorkAreaId, cancellationToken);
        checklist.EnsureVersion(version);
        return (checklist, scope);
    }

    public static DomainRuleException NotFound() => DomainRuleException.NotFound("READINESS_CHECK_NOT_FOUND", "The readiness check does not exist.");
}

/// <summary>F-RDY-02 <c>createReadinessCheck</c> (LEADER of the work area): one checklist per work area, leader-defined items (D-04).</summary>
public sealed class CreateReadinessCheckCommandHandler(WorkAreaAccess access, IReadinessCheckStore store)
{
    public async Task<ReadinessCheckDto> HandleAsync(CreateReadinessCheckCommand command, CancellationToken cancellationToken = default)
    {
        var scope = await access.ForLeaderWriteAsync(command.WorkAreaId, cancellationToken);
        var checklist = ReadinessChecklist.Create(scope.WorkAreaId, command.Name, command.Items);
        if (await store.ExistsForWorkAreaAsync(scope.WorkAreaId, cancellationToken))
        {
            throw new DomainRuleException("READINESS_CHECK_EXISTS", "This work area already has a readiness checklist.");
        }

        store.Add(checklist);
        await store.SaveChangesAsync(cancellationToken);
        return ReadinessCheckDto.From(checklist, scope);
    }
}

/// <summary>F-RDY-02 <c>replaceReadinessItems</c> (LEADER) and <c>getReadinessCheck</c> (PARISH, LEADER, MO of the parish).</summary>
public sealed class ChecklistDefinitionHandler(ChecklistLoader loader, IReadinessCheckStore store)
{
    public async Task<ReadinessCheckDto> ReplaceItemsAsync(ReplaceItemsCommand command, CancellationToken cancellationToken = default)
    {
        var (checklist, scope) = await loader.ForLeaderAsync(command.CheckId, command.Version, cancellationToken);
        checklist.ReplaceItems(command.Items);
        await store.SaveChangesAsync(cancellationToken);
        return ReadinessCheckDto.From(checklist, scope);
    }

    public async Task<ReadinessCheckDto> GetAsync(Guid checkId, CancellationToken cancellationToken = default)
    {
        var (checklist, scope) = await loader.ForReadAsync(checkId, cancellationToken);
        return ReadinessCheckDto.From(checklist, scope);
    }
}

/// <summary>
/// F-RDY-03/04/05 inspection commands of the leader of the work area: first result, corrective task link (BR-66), recheck.
/// A failed result publishes <c>readiness.item.failed</c>.
/// </summary>
public sealed class InspectionCommandHandler(
    ChecklistLoader loader,
    IReadinessCheckStore store,
    ITaskProjectionStore tasks,
    IIntegrationEventPublisher publisher,
    ICurrentUser currentUser)
{
    public Task<ReadinessItemDto> RecordResultAsync(ItemResultCommand command, CancellationToken cancellationToken = default) =>
        InspectAsync(command, recheck: false, cancellationToken);

    public Task<ReadinessItemDto> RecheckAsync(ItemResultCommand command, CancellationToken cancellationToken = default) =>
        InspectAsync(command, recheck: true, cancellationToken);

    /// <summary>
    /// Links an existing CORRECTIVE task of the same work area. Creating one needs TaskExecution <c>POST /internal/tasks</c>
    /// (not available yet — plan §5 fallback "link task có sẵn").
    /// </summary>
    public async Task<ReadinessItemDto> LinkCorrectiveTaskAsync(LinkCorrectiveTaskCommand command, CancellationToken cancellationToken = default)
    {
        var (checklist, scope) = await loader.ForLeaderAsync(command.CheckId, command.Version, cancellationToken);
        var taskId = command.TaskId
            ?? throw new DomainRuleException(
                "CORRECTIVE_TASK_CREATE_UNAVAILABLE",
                "Creating the corrective task from here needs TaskExecution POST /internal/tasks; create it in Tasks and link its id.");
        var task = await tasks.GetAsync(taskId, cancellationToken);
        if (task is null || task.WorkAreaId != scope.WorkAreaId || task.Type != ProjectedTaskType.Corrective || task.Status == ProjectedTaskStatus.Cancelled)
        {
            throw DomainRuleException.Validation("CORRECTIVE_TASK_INVALID", "Link an open CORRECTIVE task of this work area.");
        }

        var item = checklist.LinkCorrectiveTask(command.ItemId, taskId, task.Status == ProjectedTaskStatus.Completed);
        await store.SaveChangesAsync(cancellationToken);
        return ReadinessItemDto.From(item, checklist.Version);
    }

    private async Task<ReadinessItemDto> InspectAsync(ItemResultCommand command, bool recheck, CancellationToken cancellationToken)
    {
        var (checklist, scope) = await loader.ForLeaderAsync(command.CheckId, command.Version, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var item = recheck
            ? checklist.Recheck(command.ItemId, command.Result, command.Note, currentUser.UserId, now)
            : checklist.RecordResult(command.ItemId, command.Result, command.Note, currentUser.UserId, now);
        if (command.EvidenceFileIds.Count > 0)
        {
            item.AttachEvidence(command.EvidenceFileIds);
        }

        await store.SaveChangesAsync(cancellationToken);
        if (command.Result == InspectionResult.Failed)
        {
            await publisher.PublishAsync(
                ReadinessItemFailed.EventType,
                ReadinessItemFailed.Version,
                new ReadinessItemFailed(checklist.ReadinessChecklistId, item.ReadinessItemId, scope.WorkAreaId, scope.SeasonId, scope.ParishId,
                    scope.LeaderUserId, item.Name, item.Mandatory, item.Note, currentUser.UserId, now),
                "readiness_checklist",
                checklist.ReadinessChecklistId,
                cancellationToken);
        }

        return ReadinessItemDto.From(item, checklist.Version);
    }
}

/// <summary>
/// F-RDY-03 <c>attachReadinessEvidence</c>: the leader of the work area, or a volunteer assigned to the item's corrective
/// task ("Fix and Upload Evidence", BA 8.12). File ids are kept until NotificationAudit (F-NTF-01) validates them.
/// </summary>
public sealed class AttachEvidenceCommandHandler(
    ChecklistLoader loader,
    IReadinessCheckStore store,
    ITaskProjectionStore tasks,
    ICurrentUser currentUser)
{
    public async Task<ReadinessItemDto> HandleAsync(AttachEvidenceCommand command, CancellationToken cancellationToken = default)
    {
        var (checklist, scope) = await loader.ForReadAsync(command.CheckId, cancellationToken);
        var item = checklist.Item(command.ItemId);
        var allowed = currentUser.Role == GrottoWorksRoles.Leader
            ? scope.LeaderUserId == currentUser.UserId
            : item.CorrectiveTaskId is { } taskId && (await tasks.GetAsync(taskId, cancellationToken))?.AssigneeUserIds.Contains(currentUser.UserId) == true;
        if (!allowed)
        {
            throw DomainRuleException.Forbidden("READINESS_EVIDENCE_DENIED", "Only the leader or a volunteer of the corrective task can attach evidence.");
        }

        WorkAreaAccess.EnsureWritable(scope);
        item.AttachEvidence(command.FileIds);
        await store.SaveChangesAsync(cancellationToken);
        return ReadinessItemDto.From(item, checklist.Version);
    }
}

/// <summary>
/// F-RDY-05 <c>confirmAreaReadiness</c> (LEADER, work area in READINESS_REVIEW, BR-52 → 409 while a mandatory item is not
/// PASSED) and
/// <c>reopenReadinessCheck</c> (PARISH of the parish or LEADER of the work area, reason required — BR-53).
/// </summary>
public sealed class ReadinessDecisionHandler(
    ChecklistLoader loader,
    IReadinessCheckStore store,
    IIntegrationEventPublisher publisher,
    ICurrentUser currentUser)
{
    public async Task<ReadinessCheckDto> ConfirmAsync(CheckCommand command, CancellationToken cancellationToken = default)
    {
        var (checklist, scope) = await loader.ForLeaderAsync(command.CheckId, command.Version, cancellationToken);
        // BA 9.2 / BR-73: a work area reaches READY from READINESS_REVIEW, i.e. once every non-cancelled task is COMPLETED.
        if (scope.Status != WorkAreaStatus.ReadinessReview)
        {
            throw new DomainRuleException(
                "WORK_AREA_NOT_IN_READINESS_REVIEW",
                "Readiness can be confirmed only while the work area is in READINESS_REVIEW (every non-cancelled task COMPLETED, BR-73).");
        }

        var now = DateTimeOffset.UtcNow;
        checklist.Confirm(currentUser.UserId, now);
        await store.SaveChangesAsync(cancellationToken);
        await publisher.PublishAsync(
            ReadinessAreaReady.EventType,
            ReadinessAreaReady.Version,
            new ReadinessAreaReady(checklist.ReadinessChecklistId, scope.WorkAreaId, scope.SeasonId, scope.ParishId, currentUser.UserId, now),
            "readiness_checklist",
            checklist.ReadinessChecklistId,
            cancellationToken);
        return ReadinessCheckDto.From(checklist, scope);
    }

    public async Task<ReadinessCheckDto> ReopenAsync(CheckCommand command, CancellationToken cancellationToken = default)
    {
        var (checklist, scope) = currentUser.Role == GrottoWorksRoles.Parish
            ? await loader.ForReadAsync(command.CheckId, cancellationToken)
            : await loader.ForLeaderAsync(command.CheckId, null, cancellationToken);
        WorkAreaAccess.EnsureWritable(scope);
        checklist.EnsureVersion(command.Version);
        checklist.Reopen(command.Reason);
        await store.SaveChangesAsync(cancellationToken);
        await publisher.PublishAsync(
            ReadinessAreaReopened.EventType,
            ReadinessAreaReopened.Version,
            new ReadinessAreaReopened(checklist.ReadinessChecklistId, scope.WorkAreaId, scope.SeasonId, scope.ParishId, checklist.ReopenReason!,
                currentUser.UserId, DateTimeOffset.UtcNow),
            "readiness_checklist",
            checklist.ReadinessChecklistId,
            cancellationToken);
        return ReadinessCheckDto.From(checklist, scope);
    }
}

/// <summary>F-RDY-06 <c>getSeasonReadiness</c> (PARISH, LEADER, MO of the parish): every work area of the season and its checklist state.</summary>
public sealed class GetSeasonReadinessQueryHandler(WorkAreaAccess access, IWorkAreaScopeStore scopes, IReadinessCheckStore store)
{
    public async Task<SeasonReadinessDto> HandleAsync(Guid seasonId, CancellationToken cancellationToken = default)
    {
        var areas = await scopes.ListByParishAsync(access.RequireParish(), seasonId, cancellationToken);
        if (areas.Count == 0)
        {
            throw DomainRuleException.NotFound("SEASON_NOT_FOUND", "The season does not exist or is outside your parish.");
        }

        var checklists = (await store.ListByWorkAreasAsync(areas.Select(area => area.WorkAreaId).ToList(), cancellationToken))
            .ToDictionary(checklist => checklist.WorkAreaId);
        var rows = areas
            .OrderBy(area => area.Name, StringComparer.CurrentCulture)
            .Select(area =>
            {
                var checklist = checklists.GetValueOrDefault(area.WorkAreaId);
                var items = checklist?.Items ?? [];
                return new WorkAreaReadinessDto(area.WorkAreaId, area.Name, area.Status, checklist?.ReadinessChecklistId, checklist?.Status,
                    items.Count, items.Count(item => item.Mandatory), items.Count(item => item.Status == ItemStatus.Passed),
                    items.Count(item => item.Status is ItemStatus.Failed or ItemStatus.CorrectiveAction or ItemStatus.Recheck),
                    items.Count(item => item.Status == ItemStatus.Pending), checklist?.Status == ChecklistStatus.Ready);
            })
            .ToList();
        return new SeasonReadinessDto(seasonId, rows.Count, rows.Count(row => row.Ready), rows);
    }
}

/// <summary>A CORRECTIVE task completed in TaskExecution → its failed items wait for the recheck (RECHECK). Same unit of work as the projection.</summary>
public sealed class CorrectiveTaskSync(IReadinessCheckStore store)
{
    public async Task ApplyAsync(TaskProjection task, CancellationToken cancellationToken)
    {
        if (task.Type != ProjectedTaskType.Corrective || task.Status != ProjectedTaskStatus.Completed)
        {
            return;
        }

        foreach (var checklist in await store.ListByCorrectiveTaskAsync(task.TaskId, cancellationToken))
        {
            checklist.CorrectiveTaskCompleted(task.TaskId);
        }
    }
}
