using Readiness.Domain;

namespace Readiness.Application.Checklists;

public sealed record CorrectiveTaskDefinition(Guid RequestId, string Name, string? Description, string? CompletionCriteria,
    string Priority, int EstimatedMinutes, int RequiredVolunteerCount, DateTimeOffset? StartAt, DateTimeOffset Deadline)
{
    public void Validate()
    {
        if (RequestId == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 160 ||
            Description?.Length > 2000 || CompletionCriteria?.Length > 4000 ||
            Priority is not ("LOW" or "MEDIUM" or "HIGH" or "CRITICAL") || EstimatedMinutes is <= 0 or > 599999 ||
            RequiredVolunteerCount < 1 || Deadline == default || StartAt.HasValue && Deadline <= StartAt.Value)
            throw DomainRuleException.Validation("CORRECTIVE_TASK_INVALID", "Provide a request ID and valid task name, priority, estimate, capacity and schedule.");
    }
}

public sealed record CorrectiveTaskCreationRequest(Guid ReadinessCheckId, Guid ReadinessItemId, Guid WorkAreaId,
    Guid RequestedByUserId, DateTimeOffset? InspectionAt, Guid? PreviousTaskId, CorrectiveTaskDefinition Task);
public sealed record CreatedCorrectiveTask(Guid Id, Guid WorkAreaId, string Type, string Status);

public interface ICorrectiveTaskCreator
{
    // Persists the immutable request before sending it. A timeout leaves it eligible for background reconciliation.
    Task<CreatedCorrectiveTask> CreateAsync(CorrectiveTaskCreationRequest request, CancellationToken ct);
}
