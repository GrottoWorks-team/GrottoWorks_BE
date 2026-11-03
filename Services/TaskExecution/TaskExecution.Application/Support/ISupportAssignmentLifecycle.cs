namespace TaskExecution.Application.Support;

public interface ISupportAssignmentLifecycle
{
    // Stage changes in the caller's unit of work; do not save or start another transaction.
    Task CompleteAssignmentsAsync(IReadOnlyCollection<Guid> assignmentIds,DateTimeOffset now,CancellationToken cancellationToken);
    Task CancelAssignmentsAsync(IReadOnlyCollection<Guid> assignmentIds,DateTimeOffset now,CancellationToken cancellationToken);
    Task RespondAsync(Guid assignmentId,bool accept,string? reason,DateTimeOffset now,CancellationToken cancellationToken);
}
