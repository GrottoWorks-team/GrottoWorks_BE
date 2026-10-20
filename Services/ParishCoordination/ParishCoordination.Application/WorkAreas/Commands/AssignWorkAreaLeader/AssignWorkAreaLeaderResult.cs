namespace ParishCoordination.Application.WorkAreas.Commands.AssignWorkAreaLeader;

public enum AssignWorkAreaLeaderOutcome
{
    Success,
    WorkAreaNotFound,
    SeasonNotFound,
    SeasonInvalidState,
    UserNotFound,
    UserNotActive,
    UserRoleMismatch,
    IdentityServiceUnavailable,
    VersionMismatch
}

public sealed record AssignWorkAreaLeaderResult(
    AssignWorkAreaLeaderOutcome Outcome,
    WorkAreaDto? WorkArea)
{
    public static AssignWorkAreaLeaderResult Success(WorkAreaDto workArea) =>
        new(AssignWorkAreaLeaderOutcome.Success, workArea);

    public static AssignWorkAreaLeaderResult WorkAreaNotFound() =>
        new(AssignWorkAreaLeaderOutcome.WorkAreaNotFound, null);

    public static AssignWorkAreaLeaderResult SeasonNotFound() =>
        new(AssignWorkAreaLeaderOutcome.SeasonNotFound, null);

    public static AssignWorkAreaLeaderResult SeasonInvalidState() =>
        new(AssignWorkAreaLeaderOutcome.SeasonInvalidState, null);

    public static AssignWorkAreaLeaderResult UserNotFound() =>
        new(AssignWorkAreaLeaderOutcome.UserNotFound, null);

    public static AssignWorkAreaLeaderResult UserNotActive() =>
        new(AssignWorkAreaLeaderOutcome.UserNotActive, null);

    public static AssignWorkAreaLeaderResult UserRoleMismatch() =>
        new(AssignWorkAreaLeaderOutcome.UserRoleMismatch, null);

    public static AssignWorkAreaLeaderResult IdentityServiceUnavailable() =>
        new(AssignWorkAreaLeaderOutcome.IdentityServiceUnavailable, null);

    public static AssignWorkAreaLeaderResult VersionMismatch() =>
        new(AssignWorkAreaLeaderOutcome.VersionMismatch, null);
}
