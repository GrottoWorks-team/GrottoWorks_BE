namespace ParishCoordination.Application.Communities.Commands.UpdateCommunity;

public enum UpdateCommunityOutcome
{
    Success,
    NotFound,
    NameConflict,
    VersionMismatch
}

public sealed record UpdateCommunityResult(
    UpdateCommunityOutcome Outcome,
    CommunityDto? Community)
{
    public static UpdateCommunityResult Success(CommunityDto community) =>
        new(UpdateCommunityOutcome.Success, community);

    public static UpdateCommunityResult NotFound() =>
        new(UpdateCommunityOutcome.NotFound, null);

    public static UpdateCommunityResult NameConflict() =>
        new(UpdateCommunityOutcome.NameConflict, null);

    public static UpdateCommunityResult VersionMismatch() =>
        new(UpdateCommunityOutcome.VersionMismatch, null);
}
