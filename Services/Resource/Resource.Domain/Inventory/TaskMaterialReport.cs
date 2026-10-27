namespace Resource.Domain.Inventory;

/// <summary><c>task_material_report.report_status</c> (ERD v3.6.2, BA 9.9).</summary>
public enum MaterialReportStatus
{
    Submitted,
    Verified,
    RevisionRequired
}

/// <summary>What the volunteer reports for one allocation item.</summary>
public sealed record ReportedLineInput(Guid MaterialAllocationItemId, decimal Remaining, decimal Damaged, decimal Lost, string? Note);

/// <summary>What the officer verifies for one report item.</summary>
public sealed record VerifiedLineInput(Guid ReportItemId, decimal Remaining, decimal Damaged, decimal Lost);

/// <summary>
/// <c>task_material_report</c> (ERD v3.6.2, F-RES-17): after the leader completed the task (BR-44) the responsible
/// volunteer reports, per allocation item of the task (BR-45), what remains, what was damaged and what was lost. Only
/// the officer's verified values change stock (BR-46..48); <c>allocated_quantity</c> is never rewritten (BR-51).
/// One report per task; a report sent back for revision is resubmitted in place.
/// </summary>
public sealed class TaskMaterialReport
{
    public const int MaxNoteLength = 2000;
    public const int MaxReasonLength = 500;

    private readonly List<TaskMaterialReportItem> _items = [];

    private TaskMaterialReport()
    {
    }

    public Guid TaskMaterialReportId { get; private set; }

    public Guid TaskId { get; private set; }

    public Guid ReportedBy { get; private set; }

    public Guid? VerifiedBy { get; private set; }

    public MaterialReportStatus Status { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Why the officer sent it back (not in ERD v3.6.2 — extension column, see Resource README).</summary>
    public string? RevisionReason { get; private set; }

    public DateTimeOffset ReportedAt { get; private set; }

    public DateTimeOffset? VerifiedAt { get; private set; }

    /// <summary>Optimistic concurrency token mapped to PostgreSQL <c>xmin</c>.</summary>
    public uint Version { get; private set; }

    public IReadOnlyList<TaskMaterialReportItem> Items => _items;

    /// <param name="allocated">Allocation items of the task (BR-45) with their allocated quantity.</param>
    public static TaskMaterialReport Submit(
        Guid taskId,
        Guid volunteerUserId,
        IReadOnlyDictionary<Guid, decimal> allocated,
        IReadOnlyCollection<ReportedLineInput> lines,
        string? note,
        DateTimeOffset now)
    {
        var report = new TaskMaterialReport
        {
            TaskMaterialReportId = Guid.NewGuid(),
            TaskId = taskId,
            Status = MaterialReportStatus.Submitted
        };
        report.Fill(volunteerUserId, allocated, lines, note, now);
        return report;
    }

    public void EnsureVersion(long? expected)
    {
        if (expected is { } value && value != Version)
        {
            throw new DomainRuleException(
                "MATERIAL_REPORT_VERSION_MISMATCH",
                "The material report was changed by someone else. Reload it and try again.",
                DomainErrorKind.PreconditionFailed);
        }
    }

    /// <summary>Resubmission after REVISION_REQUIRED: the lines are replaced, the report waits for verification again.</summary>
    public void Resubmit(
        Guid volunteerUserId,
        IReadOnlyDictionary<Guid, decimal> allocated,
        IReadOnlyCollection<ReportedLineInput> lines,
        string? note,
        DateTimeOffset now)
    {
        if (Status != MaterialReportStatus.RevisionRequired)
        {
            throw new DomainRuleException("MATERIAL_REPORT_ALREADY_SUBMITTED", "This task already has a material report waiting or verified.");
        }

        Status = MaterialReportStatus.Submitted;
        Fill(volunteerUserId, allocated, lines, note, now);
    }

    /// <summary>The officer enters verified values for every item (BR-47, BR-48).</summary>
    public void Verify(Guid officerUserId, IReadOnlyCollection<VerifiedLineInput> lines, DateTimeOffset now)
    {
        EnsureSubmitted();
        if (lines.Count != _items.Count || lines.Select(line => line.ReportItemId).Distinct().Count() != _items.Count)
        {
            throw DomainRuleException.Validation("MATERIAL_REPORT_ITEMS_MISMATCH", "Verify every item of the report exactly once.");
        }

        foreach (var line in lines)
        {
            var item = _items.FirstOrDefault(candidate => candidate.TaskMaterialReportItemId == line.ReportItemId)
                ?? throw DomainRuleException.Validation("MATERIAL_REPORT_ITEMS_MISMATCH", $"Item {line.ReportItemId} is not part of this report.");
            item.SetVerified(line.Remaining, line.Damaged, line.Lost);
        }

        Status = MaterialReportStatus.Verified;
        VerifiedBy = officerUserId;
        VerifiedAt = now.ToUniversalTime();
        RevisionReason = null;
    }

    public void RequestRevision(string? reason)
    {
        EnsureSubmitted();
        var cleaned = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (cleaned is null || cleaned.Length > MaxReasonLength)
        {
            throw DomainRuleException.Validation("MATERIAL_REPORT_REASON_REQUIRED", $"A reason of at most {MaxReasonLength} characters is required.");
        }

        Status = MaterialReportStatus.RevisionRequired;
        RevisionReason = cleaned;
    }

    private void EnsureSubmitted()
    {
        if (Status != MaterialReportStatus.Submitted)
        {
            throw new DomainRuleException("MATERIAL_REPORT_INVALID_STATE", "Only a submitted material report can be verified or sent back.");
        }
    }

    private void Fill(Guid volunteerUserId, IReadOnlyDictionary<Guid, decimal> allocated, IReadOnlyCollection<ReportedLineInput> lines, string? note, DateTimeOffset now)
    {
        if (allocated.Count == 0)
        {
            throw new DomainRuleException("TASK_HAS_NO_ALLOCATION", "Nothing was allocated to this task, so there is nothing to report.");
        }

        if (lines.Count != allocated.Count ||
            lines.Select(line => line.MaterialAllocationItemId).Distinct().Count() != allocated.Count ||
            lines.Any(line => !allocated.ContainsKey(line.MaterialAllocationItemId)))
        {
            throw DomainRuleException.Validation(
                "MATERIAL_REPORT_ITEMS_MISMATCH",
                "Report every material allocated to the task exactly once, and nothing else (BR-45).");
        }

        var cleaned = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (cleaned is { Length: > MaxNoteLength })
        {
            throw DomainRuleException.Validation("MATERIAL_REPORT_NOTE_TOO_LONG", $"Note must not exceed {MaxNoteLength} characters.");
        }

        var items = lines.Select(line => TaskMaterialReportItem.Create(TaskMaterialReportId, line, allocated[line.MaterialAllocationItemId])).ToList();
        _items.Clear();
        _items.AddRange(items);
        ReportedBy = volunteerUserId;
        ReportedAt = now.ToUniversalTime();
        Note = cleaned;
    }
}

/// <summary><c>task_material_report_item</c> (ERD v3.6.2): reported and verified figures of one allocation item.</summary>
public sealed class TaskMaterialReportItem
{
    private TaskMaterialReportItem()
    {
    }

    public Guid TaskMaterialReportItemId { get; private set; }

    public Guid TaskMaterialReportId { get; private set; }

    public Guid MaterialAllocationItemId { get; private set; }

    /// <summary>Allocated quantity of the allocation item, kept to validate verified values (not a column).</summary>
    public decimal AllocatedQuantity { get; private set; }

    public decimal ReportedRemaining { get; private set; }

    public decimal ReportedDamaged { get; private set; }

    public decimal ReportedLost { get; private set; }

    public decimal? VerifiedRemaining { get; private set; }

    public decimal? VerifiedDamaged { get; private set; }

    public decimal? VerifiedLost { get; private set; }

    public string? Note { get; private set; }

    internal static TaskMaterialReportItem Create(Guid reportId, ReportedLineInput line, decimal allocated)
    {
        Check(line.Remaining, line.Damaged, line.Lost, allocated, "MATERIAL_REPORT_QUANTITY_INVALID");
        return new TaskMaterialReportItem
        {
            TaskMaterialReportItemId = Guid.NewGuid(),
            TaskMaterialReportId = reportId,
            MaterialAllocationItemId = line.MaterialAllocationItemId,
            AllocatedQuantity = allocated,
            ReportedRemaining = line.Remaining,
            ReportedDamaged = line.Damaged,
            ReportedLost = line.Lost,
            Note = string.IsNullOrWhiteSpace(line.Note) ? null : line.Note.Trim()
        };
    }

    /// <summary>The allocation item quantity is needed again after loading from the database.</summary>
    public void AttachAllocated(decimal allocated) => AllocatedQuantity = allocated;

    internal void SetVerified(decimal remaining, decimal damaged, decimal lost)
    {
        Check(remaining, damaged, lost, AllocatedQuantity, "MATERIAL_REPORT_VERIFIED_QUANTITY_INVALID");
        VerifiedRemaining = remaining;
        VerifiedDamaged = damaged;
        VerifiedLost = lost;
    }

    /// <summary>Remaining + damaged + lost never exceed what was allocated; each 0 or more with 2 decimals.</summary>
    private static void Check(decimal remaining, decimal damaged, decimal lost, decimal allocated, string code)
    {
        if (new[] { remaining, damaged, lost }.Any(value => value < 0 || decimal.Round(value, 2) != value) ||
            remaining + damaged + lost > allocated)
        {
            throw DomainRuleException.Validation(code, $"Remaining, damaged and lost must be 0 or more and together at most the {allocated} allocated.");
        }
    }
}
