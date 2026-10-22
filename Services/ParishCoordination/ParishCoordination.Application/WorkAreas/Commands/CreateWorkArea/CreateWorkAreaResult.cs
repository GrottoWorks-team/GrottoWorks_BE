namespace ParishCoordination.Application.WorkAreas.Commands.CreateWorkArea;

public enum CreateWorkAreaOutcome
{
    Success,
    SeasonNotFound,
    SeasonInvalidState,
    CommunityNotFound,
    CategoryNotFound,
    CategoryInactive
}

public sealed record CreateWorkAreaResult(
    CreateWorkAreaOutcome Outcome,
    WorkAreaDto? WorkArea)
{
    public static CreateWorkAreaResult Success(WorkAreaDto workArea) =>
        new(CreateWorkAreaOutcome.Success, workArea);

    public static CreateWorkAreaResult SeasonNotFound() =>
        new(CreateWorkAreaOutcome.SeasonNotFound, null);

    public static CreateWorkAreaResult SeasonInvalidState() =>
        new(CreateWorkAreaOutcome.SeasonInvalidState, null);

    public static CreateWorkAreaResult CommunityNotFound() =>
        new(CreateWorkAreaOutcome.CommunityNotFound, null);

    public static CreateWorkAreaResult CategoryNotFound() =>
        new(CreateWorkAreaOutcome.CategoryNotFound, null);

    public static CreateWorkAreaResult CategoryInactive() =>
        new(CreateWorkAreaOutcome.CategoryInactive, null);
}
