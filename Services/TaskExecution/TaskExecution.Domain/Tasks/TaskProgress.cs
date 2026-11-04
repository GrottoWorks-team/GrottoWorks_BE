namespace TaskExecution.Domain.Tasks;

public sealed class TaskProgress
{
    private TaskProgress() { }
    public Guid Id { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid SubmittedByUserId { get; private set; }
    public int PercentComplete { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public bool IsSubmission { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public static TaskProgress Create(Guid id, Guid taskId, Guid actor, int percent, string? note, bool submission, DateTimeOffset now)
    {
        if (id == Guid.Empty || taskId == Guid.Empty || actor == Guid.Empty)
            throw DomainRuleException.Validation("TASK_PROGRESS_ID_INVALID", "Progress, task and author are required.");
        if (percent is < 0 or > 100 || (submission && percent != 100))
            throw DomainRuleException.Validation("TASK_PROGRESS_PERCENT_INVALID", "Progress must be an integer from 0 to 100; submission requires 100.");
        if (note?.Length > 2000) throw DomainRuleException.Validation("TASK_PROGRESS_NOTE_INVALID", "Note must not exceed 2000 characters.");
        return new() { Id=id, TaskId=taskId, SubmittedByUserId=actor, PercentComplete=percent,
            Note=string.IsNullOrWhiteSpace(note) ? null : note.Trim(), IsSubmission=submission, SubmittedAt=now.ToUniversalTime() };
    }
    public void Approve(Guid leader, DateTimeOffset now)
    {
        if (!IsSubmission || PercentComplete != 100 || ApprovedAt.HasValue || leader == Guid.Empty || now < SubmittedAt)
            throw new DomainRuleException("TASK_PROGRESS_APPROVAL_INVALID", "Only an unapproved completion submission can be approved.");
        ApprovedByUserId=leader; ApprovedAt=now.ToUniversalTime();
    }
}

public sealed class ProgressEvidence
{
    private ProgressEvidence() { }
    public Guid Id { get; private set; }
    public Guid ProgressId { get; private set; }
    public Guid FileId { get; private set; }
    public string? Caption { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }
    public static ProgressEvidence Create(Guid progressId, Guid fileId, string? caption, DateTimeOffset now)
    {
        if (progressId == Guid.Empty || fileId == Guid.Empty || caption?.Length > 255)
            throw DomainRuleException.Validation("TASK_EVIDENCE_INVALID", "Evidence needs valid identities and a caption up to 255 characters.");
        return new() { Id=Guid.NewGuid(), ProgressId=progressId, FileId=fileId, Caption=caption?.Trim(), UploadedAt=now.ToUniversalTime() };
    }
}

public sealed class TaskCompletionReview
{
    private TaskCompletionReview() { }
    public Guid Id { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid ProgressId { get; private set; }
    public Guid ReviewedByUserId { get; private set; }
    public bool Approved { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset ReviewedAt { get; private set; }
    public static TaskCompletionReview Create(Guid taskId, Guid progressId, Guid leader, bool approved, string? reason, DateTimeOffset now)
    {
        if (taskId == Guid.Empty || progressId == Guid.Empty || leader == Guid.Empty ||
            (!approved && string.IsNullOrWhiteSpace(reason)) || reason?.Trim().Length > 2000)
            throw DomainRuleException.Validation("TASK_REVIEW_REASON_REQUIRED", "Revision requires a reason; review reason must not exceed 2000 characters.");
        return new() { Id=Guid.NewGuid(), TaskId=taskId, ProgressId=progressId, ReviewedByUserId=leader,
            Approved=approved, Reason=reason?.Trim(), ReviewedAt=now.ToUniversalTime() };
    }
}
