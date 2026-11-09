namespace TaskExecution.Domain.Tasks;

public sealed partial class WorkTask
{
    public Guid? TeamLeaderAssignmentId { get; private set; }

    public void EnsureTeamLeaderCanBeEdited(int expectedVersion)
    {
        if (Status is TaskStatus.Completed or TaskStatus.Cancelled)
            throw new DomainRuleException("TASK_INVALID_STATE", "A completed or cancelled task cannot change team leader.");

        if (Version != expectedVersion)
            throw new DomainRuleException("TASK_VERSION_MISMATCH", "The task version does not match the current version.",
                DomainErrorKind.PreconditionFailed);
    }

    public bool SetTeamLeader(Guid? assignmentId, int expectedVersion, DateTimeOffset now)
    {
        EnsureTeamLeaderCanBeEdited(expectedVersion);
        if (TeamLeaderAssignmentId == assignmentId)
            return false;

        TeamLeaderAssignmentId = assignmentId;
        RegisterAssignmentChange(now);
        return true;
    }

    // Use after a mutation that has already updated the task's version and timestamp.
    public void ClearInvalidTeamLeaderForCurrentChange(Guid assignmentId)
    {
        if (Status != TaskStatus.Completed && TeamLeaderAssignmentId == assignmentId)
            TeamLeaderAssignmentId = null;
    }
}
