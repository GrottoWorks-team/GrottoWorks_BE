namespace TaskExecution.Application.Support;

public interface ISupportAssignmentLifecycle
{
    // Stage changes in the caller's unit of work; do not save or start another transaction.
    Task CancelAssignmentsAsync(IReadOnlyCollection<Guid> assignmentIds,DateTimeOffset now,CancellationToken cancellationToken);
}
