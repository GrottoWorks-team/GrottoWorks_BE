namespace ParishCoordination.Application.WorkAreas.Commands.AssignWorkAreaCommunity;

public enum AssignWorkAreaCommunityOutcome
{
    Success,
    WorkAreaNotFound,
    SeasonNotFound,
    SeasonInvalidState,
    CommunityNotFound,
    CommunityInactive,
    VersionMismatch
}

public sealed record AssignWorkAreaCommunityResult(
    AssignWorkAreaCommunityOutcome Outcome,
    WorkAreaDto? WorkArea)
{
    public static AssignWorkAreaCommunityResult Success(WorkAreaDto workArea) =>
        new(AssignWorkAreaCommunityOutcome.Success, workArea);

    public static AssignWorkAreaCommunityResult WorkAreaNotFound() =>
        new(AssignWorkAreaCommunityOutcome.WorkAreaNotFound, null);

    public static AssignWorkAreaCommunityResult SeasonNotFound() =>
        new(AssignWorkAreaCommunityOutcome.SeasonNotFound, null);

    public static AssignWorkAreaCommunityResult SeasonInvalidState() =>
        new(AssignWorkAreaCommunityOutcome.SeasonInvalidState, null);

    public static AssignWorkAreaCommunityResult CommunityNotFound() =>
        new(AssignWorkAreaCommunityOutcome.CommunityNotFound, null);

    public static AssignWorkAreaCommunityResult CommunityInactive() =>
        new(AssignWorkAreaCommunityOutcome.CommunityInactive, null);

    public static AssignWorkAreaCommunityResult VersionMismatch() =>
        new(AssignWorkAreaCommunityOutcome.VersionMismatch, null);
}
