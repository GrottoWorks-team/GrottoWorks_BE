namespace ParishCoordination.Application.WorkAreas.Commands.UpdateWorkArea;

public enum UpdateWorkAreaOutcome
{
    Success,
    NotFound,
    SeasonNotFound,
    SeasonInvalidState,
    CategoryNotFound,
    CategoryInactive,
    VersionMismatch
}

public sealed record UpdateWorkAreaResult(
    UpdateWorkAreaOutcome Outcome,
    WorkAreaDto? WorkArea)
{
    public static UpdateWorkAreaResult Success(WorkAreaDto workArea) =>
        new(UpdateWorkAreaOutcome.Success, workArea);

    public static UpdateWorkAreaResult NotFound() =>
        new(UpdateWorkAreaOutcome.NotFound, null);

    public static UpdateWorkAreaResult SeasonNotFound() =>
        new(UpdateWorkAreaOutcome.SeasonNotFound, null);

    public static UpdateWorkAreaResult SeasonInvalidState() =>
        new(UpdateWorkAreaOutcome.SeasonInvalidState, null);

    public static UpdateWorkAreaResult CategoryNotFound() =>
        new(UpdateWorkAreaOutcome.CategoryNotFound, null);

    public static UpdateWorkAreaResult CategoryInactive() =>
        new(UpdateWorkAreaOutcome.CategoryInactive, null);

    public static UpdateWorkAreaResult VersionMismatch() =>
        new(UpdateWorkAreaOutcome.VersionMismatch, null);
}
