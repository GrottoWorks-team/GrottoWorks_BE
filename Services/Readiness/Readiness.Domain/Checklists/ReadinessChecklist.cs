namespace Readiness.Domain.Checklists;

/// <summary><c>readiness_checklist.checklist_status</c> (ERD v3.6.2; R-12: PLANNING replaces DRAFT).</summary>
public enum ChecklistStatus
{
    Planning,
    InReview,
    NeedsCorrection,
    Ready,
    Reopened
}

/// <summary><c>readiness_item.item_status</c> (ERD v3.6.2, BA 9.8).</summary>
public enum ItemStatus
{
    Pending,
    Passed,
    Failed,

    /// <summary>A CORRECTIVE task is linked to the failed item.</summary>
    CorrectiveAction,

    /// <summary>The corrective task is completed; the item waits for its recheck.</summary>
    Recheck
}

/// <summary>Inspection outcome entered by the leader.</summary>
public enum InspectionResult
{
    Passed,
    Failed
}

/// <summary>One leader-defined item (D-04: no template).</summary>
public sealed record ItemDefinition(string? Name, string? Description, bool Mandatory);

/// <summary>
/// <c>readiness_checklist</c> (ERD v3.6.2, F-RDY-02..05): the leader's own checklist of a work area (D-04). Items are
/// edited only while PLANNING. Results move it to IN_REVIEW / NEEDS_CORRECTION; READY needs every mandatory item PASSED
/// (BR-52) and a failed mandatory item needs a CORRECTIVE task (BR-66); reopening needs a reason (BR-53).
/// </summary>
public sealed class ReadinessChecklist
{
    public const int MaxNameLength = 150;
    public const int MaxItems = 100;
    public const int MaxReasonLength = 500;

    private readonly List<ReadinessItem> _items = [];

    private ReadinessChecklist()
    {
    }

    public Guid ReadinessChecklistId { get; private set; }

    public Guid WorkAreaId { get; private set; }

    public Guid? CompletedByUserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public ChecklistStatus Status { get; private set; }

    public string? ReopenReason { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Optimistic concurrency token mapped to PostgreSQL <c>xmin</c>.</summary>
    public uint Version { get; private set; }

    public IReadOnlyList<ReadinessItem> Items => _items;

    public static ReadinessChecklist Create(Guid workAreaId, string? name, IReadOnlyCollection<ItemDefinition> items)
    {
        var checklist = new ReadinessChecklist
        {
            ReadinessChecklistId = Guid.NewGuid(),
            WorkAreaId = workAreaId,
            Name = Text.Required(name, MaxNameLength, "READINESS_NAME_INVALID", "Checklist name"),
            Status = ChecklistStatus.Planning
        };
        checklist.SetItems(items);
        return checklist;
    }

    public void EnsureVersion(long? expected)
    {
        if (expected is { } value && value != Version)
        {
            throw new DomainRuleException(
                "READINESS_VERSION_MISMATCH",
                "The checklist was changed by someone else. Reload it and try again.",
                DomainErrorKind.PreconditionFailed);
        }
    }

    /// <summary>D-04: the leader replaces all items, only while PLANNING.</summary>
    public void ReplaceItems(IReadOnlyCollection<ItemDefinition> items)
    {
        if (Status != ChecklistStatus.Planning)
        {
            throw new DomainRuleException("READINESS_INVALID_STATE", "Items can only be changed while the checklist is PLANNING.");
        }

        SetItems(items);
    }

    public ReadinessItem Item(Guid itemId) =>
        _items.FirstOrDefault(item => item.ReadinessItemId == itemId)
        ?? throw DomainRuleException.NotFound("READINESS_ITEM_NOT_FOUND", "The item is not part of this checklist.");

    /// <summary>
    /// F-RDY-03: first inspection of a PENDING item — or, after a reopen, a new inspection of a PASSED item.
    /// </summary>
    public ReadinessItem RecordResult(Guid itemId, InspectionResult result, string? note, Guid leaderUserId, DateTimeOffset now)
    {
        EnsureInspectable();
        var item = Item(itemId);
        var reinspection = Status == ChecklistStatus.Reopened && item.Status == ItemStatus.Passed;
        if (item.Status != ItemStatus.Pending && !reinspection)
        {
            throw new DomainRuleException("READINESS_ITEM_INVALID_STATE", "Only a pending item takes a first result; use recheck after a correction.");
        }

        item.Record(result, note, leaderUserId, now);
        Recompute();
        return item;
    }

    /// <summary>F-RDY-04 / BR-66: a failed item gets its CORRECTIVE task.</summary>
    public ReadinessItem LinkCorrectiveTask(Guid itemId, Guid taskId, bool taskCompleted)
    {
        EnsureInspectable();
        var item = Item(itemId);
        item.LinkCorrectiveTask(taskId, taskCompleted);
        Recompute();
        return item;
    }

    /// <summary>The linked corrective task was completed in TaskExecution: the item waits for its recheck.</summary>
    public bool CorrectiveTaskCompleted(Guid taskId)
    {
        var changed = false;
        foreach (var item in _items.Where(item => item.CorrectiveTaskId == taskId && item.Status == ItemStatus.CorrectiveAction))
        {
            item.MarkRecheck();
            changed = true;
        }

        if (changed)
        {
            Recompute();
        }

        return changed;
    }

    /// <summary>F-RDY-05: result of the recheck after a correction (or of an optional failed item).</summary>
    public ReadinessItem Recheck(Guid itemId, InspectionResult result, string? note, Guid leaderUserId, DateTimeOffset now)
    {
        EnsureInspectable();
        var item = Item(itemId);
        item.Recheck(result, note, leaderUserId, now);
        Recompute();
        return item;
    }

    /// <summary>BR-52: READY only when every mandatory item PASSED.</summary>
    public void Confirm(Guid leaderUserId, DateTimeOffset now)
    {
        if (Status is ChecklistStatus.Ready or ChecklistStatus.Planning)
        {
            throw new DomainRuleException("READINESS_INVALID_STATE", "Only a checklist under review can be confirmed.");
        }

        var blocking = _items.Where(item => item.Mandatory && item.Status != ItemStatus.Passed).Select(item => item.Name).ToList();
        if (blocking.Count > 0)
        {
            throw new DomainRuleException(
                "READINESS_MANDATORY_NOT_PASSED",
                $"Mandatory items not passed yet: {string.Join(", ", blocking)} (BR-52).");
        }

        Status = ChecklistStatus.Ready;
        CompletedByUserId = leaderUserId;
        CompletedAt = now.ToUniversalTime();
        ReopenReason = null;
    }

    /// <summary>BR-53: only a READY checklist is reopened, always with a reason.</summary>
    public void Reopen(string? reason)
    {
        if (Status != ChecklistStatus.Ready)
        {
            throw new DomainRuleException("READINESS_INVALID_STATE", "Only a READY checklist can be reopened.");
        }

        ReopenReason = Text.Required(reason, MaxReasonLength, "READINESS_REOPEN_REASON_REQUIRED", "Reason");
        Status = ChecklistStatus.Reopened;
        CompletedAt = null;
        CompletedByUserId = null;
    }

    private void EnsureInspectable()
    {
        if (Status == ChecklistStatus.Ready)
        {
            throw new DomainRuleException("READINESS_INVALID_STATE", "The checklist is READY; reopen it before changing results.");
        }
    }

    /// <summary>NEEDS_CORRECTION while a mandatory item is failed or being corrected; REOPENED stays until confirmed.</summary>
    private void Recompute()
    {
        var needsCorrection = _items.Any(item => item.Mandatory && item.Status is ItemStatus.Failed or ItemStatus.CorrectiveAction or ItemStatus.Recheck);
        if (needsCorrection)
        {
            Status = ChecklistStatus.NeedsCorrection;
        }
        else if (Status != ChecklistStatus.Reopened)
        {
            Status = ChecklistStatus.InReview;
        }
    }

    private void SetItems(IReadOnlyCollection<ItemDefinition> items)
    {
        if (items.Count == 0 || items.Count > MaxItems)
        {
            throw DomainRuleException.Validation("READINESS_ITEMS_INVALID", $"A checklist has between 1 and {MaxItems} items.");
        }

        var created = items.Select((definition, index) => ReadinessItem.Create(ReadinessChecklistId, definition, index)).ToList();
        if (created.GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            throw DomainRuleException.Validation("READINESS_DUPLICATE_ITEM", "Item names must be unique within a checklist.");
        }

        _items.Clear();
        _items.AddRange(created);
    }
}

/// <summary>
/// <c>readiness_item</c> (ERD v3.6.2): PENDING → PASSED / FAILED → CORRECTIVE_ACTION → RECHECK → PASSED / FAILED (BA 9.8).
/// </summary>
public sealed class ReadinessItem
{
    public const int MaxNameLength = 200;
    public const int MaxTextLength = 2000;
    public const int MaxEvidenceFiles = 10;

    private ReadinessItem()
    {
    }

    public Guid ReadinessItemId { get; private set; }

    public Guid ReadinessChecklistId { get; private set; }

    public Guid? CheckedByUserId { get; private set; }

    public Guid? CorrectiveTaskId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public bool Mandatory { get; private set; }

    public ItemStatus Status { get; private set; }

    public DateTimeOffset? CheckedAt { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Display order of the leader's list (technical column).</summary>
    public int Position { get; private set; }

    /// <summary>Evidence files (F-NTF-01 ids); technical column until NotificationAudit owns the link.</summary>
    public Guid[] EvidenceFileIds { get; private set; } = [];

    internal static ReadinessItem Create(Guid checklistId, ItemDefinition definition, int position) =>
        new()
        {
            ReadinessItemId = Guid.NewGuid(),
            ReadinessChecklistId = checklistId,
            Name = Text.Required(definition.Name, MaxNameLength, "READINESS_ITEM_NAME_INVALID", "Item name"),
            Description = Text.Optional(definition.Description, MaxTextLength, "Description"),
            Mandatory = definition.Mandatory,
            Status = ItemStatus.Pending,
            Position = position
        };

    public void AttachEvidence(IReadOnlyCollection<Guid> fileIds)
    {
        if (fileIds.Count == 0)
        {
            throw DomainRuleException.Validation("READINESS_EVIDENCE_REQUIRED", "Attach at least one file.");
        }

        var all = EvidenceFileIds.Concat(fileIds).Distinct().ToArray();
        if (all.Length > MaxEvidenceFiles)
        {
            throw DomainRuleException.Validation("READINESS_TOO_MANY_EVIDENCE", $"An item holds at most {MaxEvidenceFiles} evidence files.");
        }

        EvidenceFileIds = all;
    }

    internal void Record(InspectionResult result, string? note, Guid userId, DateTimeOffset now)
    {
        Status = result == InspectionResult.Passed ? ItemStatus.Passed : ItemStatus.Failed;
        Note = Text.Optional(note, MaxTextLength, "Note");
        CheckedByUserId = userId;
        CheckedAt = now.ToUniversalTime();
        if (Status == ItemStatus.Passed)
        {
            CorrectiveTaskId = null;
        }
    }

    internal void LinkCorrectiveTask(Guid taskId, bool taskCompleted)
    {
        if (Status is not (ItemStatus.Failed or ItemStatus.CorrectiveAction))
        {
            throw new DomainRuleException("READINESS_ITEM_INVALID_STATE", "Only a failed item gets a corrective task.");
        }

        CorrectiveTaskId = taskId;
        Status = taskCompleted ? ItemStatus.Recheck : ItemStatus.CorrectiveAction;
    }

    internal void MarkRecheck() => Status = ItemStatus.Recheck;

    /// <summary>A mandatory failed item is rechecked only after its corrective task (BR-66).</summary>
    internal void Recheck(InspectionResult result, string? note, Guid userId, DateTimeOffset now)
    {
        var allowed = Status is ItemStatus.CorrectiveAction or ItemStatus.Recheck || (Status == ItemStatus.Failed && !Mandatory);
        if (!allowed)
        {
            throw new DomainRuleException(
                "READINESS_ITEM_INVALID_STATE",
                Status == ItemStatus.Failed
                    ? "Link a corrective task to this mandatory item before the recheck (BR-66)."
                    : "Only a failed or corrected item can be rechecked.");
        }

        var correctiveTask = CorrectiveTaskId;
        Record(result, note, userId, now);
        if (result == InspectionResult.Failed)
        {
            // Still failing: keep the corrective task visible; the leader links a new one (or the same) to retry.
            CorrectiveTaskId = correctiveTask;
        }
    }
}

internal static class Text
{
    public static string Required(string? value, int maxLength, string code, string field)
    {
        var cleaned = value?.Trim();
        if (string.IsNullOrEmpty(cleaned) || cleaned.Length > maxLength)
        {
            throw DomainRuleException.Validation(code, $"{field} is required and at most {maxLength} characters.");
        }

        return cleaned;
    }

    public static string? Optional(string? value, int maxLength, string field)
    {
        var cleaned = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (cleaned is { Length: var length } && length > maxLength)
        {
            throw DomainRuleException.Validation("READINESS_FIELD_TOO_LONG", $"{field} must not exceed {maxLength} characters.");
        }

        return cleaned;
    }
}
