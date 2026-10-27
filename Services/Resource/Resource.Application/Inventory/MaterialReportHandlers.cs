using BuildingBlocks.Security;
using Resource.Application.Abstractions;
using Resource.Application.Catalog;
using Resource.Application.Scope;
using Resource.Application.Shortages;
using Resource.Contracts.Events;
using Resource.Domain;
using Resource.Domain.Inventory;
using Resource.Domain.Scope;

namespace Resource.Application.Inventory;

public sealed record TaskMaterialReportItemDto(
    Guid Id,
    Guid MaterialAllocationItemId,
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    string Unit,
    decimal AllocatedQuantity,
    decimal ReportedRemainingQuantity,
    decimal ReportedDamagedQuantity,
    decimal ReportedLostQuantity,
    decimal? VerifiedRemainingQuantity,
    decimal? VerifiedDamagedQuantity,
    decimal? VerifiedLostQuantity,
    decimal? UsedQuantity,
    string? Note);

/// <summary><c>TaskMaterialReport</c> (API Contract v1.3.2).</summary>
public sealed record TaskMaterialReportDto(
    Guid Id,
    Guid TaskId,
    string TaskName,
    Guid WorkAreaId,
    Guid ReportedBy,
    Guid? VerifiedBy,
    MaterialReportStatus Status,
    string? Note,
    string? RevisionReason,
    DateTimeOffset ReportedAt,
    DateTimeOffset? VerifiedAt,
    IReadOnlyList<TaskMaterialReportItemDto> Items,
    long Version);

public sealed record SubmitMaterialReportCommand(Guid TaskId, IReadOnlyList<ReportedLineInput> Items, string? Note);

public sealed record VerifyMaterialReportCommand(Guid ReportId, long? Version, IReadOnlyList<VerifiedLineInput> Items);

public sealed record RequestReportRevisionCommand(Guid ReportId, long? Version, string? Reason);

public interface ITaskMaterialReportStore
{
    /// <summary>Tracked, with items.</summary>
    Task<TaskMaterialReport?> GetAsync(Guid reportId, CancellationToken cancellationToken = default);

    /// <summary>Tracked, with items — at most one per task.</summary>
    Task<TaskMaterialReport?> GetByTaskAsync(Guid taskId, CancellationToken cancellationToken = default);

    void Add(TaskMaterialReport report);

    /// <summary>Saves the unit of work, including ledger rows appended to the same context.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Loads the task of a report with its allocation items and the caller's view of the work area.</summary>
public sealed class MaterialReportContext(
    ITaskProjectionStore tasks,
    IAllocationStore allocations,
    IMaterialCatalogStore catalog,
    WorkAreaAccess access)
{
    public async Task<(TaskProjection Task, WorkAreaScope Scope)> TaskAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await tasks.GetAsync(taskId, cancellationToken)
            ?? throw DomainRuleException.NotFound("TASK_NOT_FOUND", "The task does not exist.");
        try
        {
            return (task, await access.ForReadAsync(task.WorkAreaId, cancellationToken));
        }
        catch (DomainRuleException exception) when (exception.Kind == DomainErrorKind.NotFound)
        {
            throw DomainRuleException.NotFound("TASK_NOT_FOUND", "The task does not exist.");
        }
    }

    public async Task<IReadOnlyDictionary<Guid, MaterialAllocationItem>> AllocationItemsAsync(Guid taskId, CancellationToken cancellationToken) =>
        (await allocations.ListItemsOfTaskAsync(taskId, cancellationToken)).ToDictionary(item => item.MaterialAllocationItemId);

    public async Task<TaskMaterialReportDto> ToDtoAsync(
        TaskMaterialReport report,
        TaskProjection task,
        IReadOnlyDictionary<Guid, MaterialAllocationItem> allocationItems,
        CancellationToken cancellationToken)
    {
        var materials = (await catalog.GetMaterialsAsync(allocationItems.Values.Select(item => item.MaterialId).Distinct().ToList(), cancellationToken))
            .ToDictionary(material => material.MaterialId);
        return new TaskMaterialReportDto(
            report.TaskMaterialReportId, report.TaskId, task.Name, task.WorkAreaId, report.ReportedBy, report.VerifiedBy, report.Status,
            report.Note, report.RevisionReason, report.ReportedAt, report.VerifiedAt,
            report.Items
                .Select(item =>
                {
                    var allocation = allocationItems[item.MaterialAllocationItemId];
                    var material = materials[allocation.MaterialId];
                    decimal? used = item.VerifiedRemaining is { } remaining
                        ? allocation.AllocatedQuantity - remaining - item.VerifiedDamaged!.Value - item.VerifiedLost!.Value
                        : null;
                    return new TaskMaterialReportItemDto(item.TaskMaterialReportItemId, item.MaterialAllocationItemId, allocation.MaterialId,
                        material.Code, material.Name, material.Unit, allocation.AllocatedQuantity, item.ReportedRemaining, item.ReportedDamaged,
                        item.ReportedLost, item.VerifiedRemaining, item.VerifiedDamaged, item.VerifiedLost, used, item.Note);
                })
                .OrderBy(item => item.MaterialName, StringComparer.CurrentCulture)
                .ToList(),
            report.Version);
    }

    public static IReadOnlyList<MaterialReportLine> Lines(
        TaskMaterialReport report,
        IReadOnlyDictionary<Guid, MaterialAllocationItem> allocationItems,
        bool verified) =>
        report.Items
            .Select(item => new MaterialReportLine(
                item.TaskMaterialReportItemId, item.MaterialAllocationItemId, allocationItems[item.MaterialAllocationItemId].MaterialId,
                allocationItems[item.MaterialAllocationItemId].AllocatedQuantity,
                verified ? item.VerifiedRemaining!.Value : item.ReportedRemaining,
                verified ? item.VerifiedDamaged!.Value : item.ReportedDamaged,
                verified ? item.VerifiedLost!.Value : item.ReportedLost))
            .ToList();
}

/// <summary>
/// F-RES-17 <c>submitTaskMaterialReport</c> (VOLUNTEER with an ACCEPTED assignment on the task): only after the leader
/// completed the task (BR-44), one line per allocation item of the task (BR-45). A report sent back for revision is
/// resubmitted in place; otherwise one report per task.
/// </summary>
public sealed class SubmitMaterialReportCommandHandler(
    MaterialReportContext context,
    ITaskMaterialReportStore store,
    IIntegrationEventPublisher publisher,
    ICurrentUser currentUser)
{
    public async Task<TaskMaterialReportDto> HandleAsync(SubmitMaterialReportCommand command, CancellationToken cancellationToken = default)
    {
        var (task, scope) = await context.TaskAsync(command.TaskId, cancellationToken);
        if (!task.AssigneeUserIds.Contains(currentUser.UserId))
        {
            throw DomainRuleException.Forbidden("MATERIAL_REPORT_NOT_ASSIGNEE", "Only a volunteer assigned to the task can report its materials.");
        }

        if (task.Status != ProjectedTaskStatus.Completed)
        {
            throw new DomainRuleException("TASK_NOT_COMPLETED", "The leader must complete the task before its material report (BR-44).");
        }

        WorkAreaAccess.EnsureWritable(scope);
        var allocationItems = await context.AllocationItemsAsync(task.TaskId, cancellationToken);
        var allocated = allocationItems.ToDictionary(pair => pair.Key, pair => pair.Value.AllocatedQuantity);

        var now = DateTimeOffset.UtcNow;
        var report = await store.GetByTaskAsync(task.TaskId, cancellationToken);
        if (report is null)
        {
            report = TaskMaterialReport.Submit(task.TaskId, currentUser.UserId, allocated, command.Items, command.Note, now);
            store.Add(report);
        }
        else
        {
            report.Resubmit(currentUser.UserId, allocated, command.Items, command.Note, now);
        }

        await store.SaveChangesAsync(cancellationToken);
        await publisher.PublishAsync(
            MaterialReportSubmitted.EventType,
            MaterialReportSubmitted.Version,
            new MaterialReportSubmitted(report.TaskMaterialReportId, task.TaskId, task.WorkAreaId, scope.ParishId, report.ReportedBy,
                scope.MaterialOfficerUserId, MaterialReportContext.Lines(report, allocationItems, verified: false), report.ReportedAt),
            "task_material_report",
            report.TaskMaterialReportId,
            cancellationToken);
        return await context.ToDtoAsync(report, task, allocationItems, cancellationToken);
    }
}

/// <summary>F-RES-17 <c>listTaskMaterialReports</c> (LEADER, MO of the parish; VOLUNTEER assigned to the task).</summary>
public sealed class ListTaskMaterialReportsQueryHandler(MaterialReportContext context, ITaskMaterialReportStore store, ICurrentUser currentUser)
{
    public async Task<IReadOnlyList<TaskMaterialReportDto>> HandleAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var (task, _) = await context.TaskAsync(taskId, cancellationToken);
        if (currentUser.Role == GrottoWorksRoles.Volunteer && !task.AssigneeUserIds.Contains(currentUser.UserId))
        {
            throw DomainRuleException.NotFound("TASK_NOT_FOUND", "The task does not exist.");
        }

        var report = await store.GetByTaskAsync(taskId, cancellationToken);
        if (report is null)
        {
            return [];
        }

        return [await context.ToDtoAsync(report, task, await context.AllocationItemsAsync(taskId, cancellationToken), cancellationToken)];
    }
}

/// <summary>
/// F-RES-17 <c>verifyTaskMaterialReport</c> (MO of the task's work area, BR-47): verified values for every item; verified
/// remaining goes back to the same work area's stock as <c>TASK_RETURN</c> (reference <c>TASK_MATERIAL_REPORT_ITEM</c>,
/// posted once — BR-49); damaged and lost never do (BR-50); allocations stay untouched (BR-51). One transaction.
/// </summary>
public sealed class VerifyMaterialReportCommandHandler(
    MaterialReportContext context,
    ITaskMaterialReportStore store,
    IInventoryStore inventory,
    ShortageTracker shortages,
    WorkAreaAccess access,
    IIntegrationEventPublisher publisher,
    ICurrentUser currentUser)
{
    public async Task<TaskMaterialReportDto> HandleAsync(VerifyMaterialReportCommand command, CancellationToken cancellationToken = default)
    {
        var report = await store.GetAsync(command.ReportId, cancellationToken) ?? throw NotFound();
        var (task, _) = await context.TaskAsync(report.TaskId, cancellationToken);
        var scope = await access.ForMaterialOfficerWriteAsync(task.WorkAreaId, cancellationToken);
        report.EnsureVersion(command.Version);

        var allocationItems = await context.AllocationItemsAsync(task.TaskId, cancellationToken);
        foreach (var item in report.Items)
        {
            item.AttachAllocated(allocationItems[item.MaterialAllocationItemId].AllocatedQuantity);
        }

        var now = DateTimeOffset.UtcNow;
        report.Verify(currentUser.UserId, command.Items, now);

        var before = new List<ShortageTracker.Snapshot>();
        foreach (var item in report.Items.Where(item => item.VerifiedRemaining > 0))
        {
            var materialId = allocationItems[item.MaterialAllocationItemId].MaterialId;
            before.Add(await shortages.CaptureAsync(scope, materialId, cancellationToken));
            inventory.Append(MaterialTransaction.Post(
                scope.WorkAreaId, materialId, TransactionType.TaskReturn, item.VerifiedRemaining!.Value, now, currentUser.UserId,
                null, TransactionReferenceType.TaskMaterialReportItem, item.TaskMaterialReportItemId));
        }

        await store.SaveChangesAsync(cancellationToken);
        foreach (var snapshot in before)
        {
            await shortages.PublishIfChangedAsync(snapshot, cancellationToken);
        }

        await publisher.PublishAsync(
            MaterialReportVerified.EventType,
            MaterialReportVerified.Version,
            new MaterialReportVerified(report.TaskMaterialReportId, task.TaskId, task.WorkAreaId, scope.ParishId, report.ReportedBy,
                currentUser.UserId, MaterialReportContext.Lines(report, allocationItems, verified: true), now),
            "task_material_report",
            report.TaskMaterialReportId,
            cancellationToken);
        return await context.ToDtoAsync(report, task, allocationItems, cancellationToken);
    }

    internal static DomainRuleException NotFound() => DomainRuleException.NotFound("MATERIAL_REPORT_NOT_FOUND", "The material report does not exist.");
}

/// <summary>F-RES-17 <c>requestMaterialReportRevision</c> (MO of the task's work area): send it back with a reason.</summary>
public sealed class RequestReportRevisionCommandHandler(
    MaterialReportContext context,
    ITaskMaterialReportStore store,
    WorkAreaAccess access)
{
    public async Task<TaskMaterialReportDto> HandleAsync(RequestReportRevisionCommand command, CancellationToken cancellationToken = default)
    {
        var report = await store.GetAsync(command.ReportId, cancellationToken) ?? throw VerifyMaterialReportCommandHandler.NotFound();
        var (task, _) = await context.TaskAsync(report.TaskId, cancellationToken);
        await access.ForMaterialOfficerWriteAsync(task.WorkAreaId, cancellationToken);
        report.EnsureVersion(command.Version);
        report.RequestRevision(command.Reason);
        await store.SaveChangesAsync(cancellationToken);
        return await context.ToDtoAsync(report, task, await context.AllocationItemsAsync(task.TaskId, cancellationToken), cancellationToken);
    }
}
