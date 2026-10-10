namespace ParishCoordination.Application.Communities.Commands.CreateCommunity;

public enum CreateCommunityOutcome
{
    Success,
    ParishNotFound,
    NameConflict
}

public sealed record CreateCommunityResult(
    CreateCommunityOutcome Outcome,
    CommunityDto? Community)
{
    public static CreateCommunityResult Success(CommunityDto community) =>
        new(CreateCommunityOutcome.Success, community);

    public static CreateCommunityResult ParishNotFound() =>
        new(CreateCommunityOutcome.ParishNotFound, null);

    public static CreateCommunityResult NameConflict() =>
        new(CreateCommunityOutcome.NameConflict, null);
}
