namespace TaskExecution.Domain.Tasks;

public sealed partial class WorkTask
{
    public void SetSpecialType(TaskType type, Guid? purchaseRequestId = null)
    {
        if (Version != 1 || Status != TaskStatus.Open || type == TaskType.Work ||
            (type == TaskType.Purchase && (!purchaseRequestId.HasValue || purchaseRequestId == Guid.Empty)) ||
            (type == TaskType.Corrective && purchaseRequestId.HasValue))
            throw DomainRuleException.Validation("TASK_TYPE_INVALID", "Special type and purchase link must be valid at creation.");
        Type=type; PurchaseRequestId=purchaseRequestId;
    }
    public void EnsureVersion(int? version)
    {
        if (version is <= 0) throw DomainRuleException.Validation("VALIDATION_FAILED", "Version must be positive.");
        if (version.HasValue && version != Version)
            throw new DomainRuleException("TASK_VERSION_MISMATCH", "The task changed.", DomainErrorKind.PreconditionFailed);
    }
    public void RecordProgress(bool submission, bool resubmit, int? version, DateTimeOffset now)
    {
        EnsureVersion(version);
        if (Type == TaskType.Purchase) throw new DomainRuleException("TASK_TYPE_INVALID", "Purchase completion is driven by delivery confirmation.");
        if (submission ? (resubmit ? Status != TaskStatus.RevisionRequired : Status != TaskStatus.InProgress)
            : Status is not (TaskStatus.NotStarted or TaskStatus.InProgress or TaskStatus.RevisionRequired))
            throw new DomainRuleException("TASK_INVALID_STATE", "The task cannot receive progress or submission in its current state.");
        Status=submission ? TaskStatus.Submitted : Status == TaskStatus.NotStarted ? TaskStatus.InProgress : Status;
        UpdatedAt=now.ToUniversalTime(); Version++;
    }
    public void ReviewCompletion(bool approve, int? version, DateTimeOffset now)
    {
        EnsureVersion(version);
        if (Type == TaskType.Purchase || Status != TaskStatus.Submitted)
            throw new DomainRuleException("TASK_INVALID_STATE", "Only submitted work or corrective tasks can be reviewed.");
        Status=approve ? TaskStatus.Completed : TaskStatus.RevisionRequired;
        UpdatedAt=now.ToUniversalTime(); Version++;
    }
    public bool CompletePurchase(DateTimeOffset now)
    {
        if (Type != TaskType.Purchase) throw new DomainRuleException("TASK_TYPE_INVALID", "Delivery must target a purchase task.");
        if (Status == TaskStatus.Completed) return false;
        if (Status == TaskStatus.Cancelled) throw new DomainRuleException("TASK_INVALID_STATE", "A cancelled task cannot complete.");
        Status=TaskStatus.Completed; UpdatedAt=now.ToUniversalTime(); Version++; return true;
    }
}
