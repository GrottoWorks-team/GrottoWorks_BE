namespace ParishCoordination.Application.WorkAreaCategories.Commands.UpdateWorkAreaCategory;

public enum UpdateWorkAreaCategoryOutcome
{
    Success,
    NotFound
}

public sealed record UpdateWorkAreaCategoryResult(
    UpdateWorkAreaCategoryOutcome Outcome,
    WorkAreaCategoryDto? Category)
{
    public static UpdateWorkAreaCategoryResult Success(WorkAreaCategoryDto category) =>
        new(UpdateWorkAreaCategoryOutcome.Success, category);

    public static UpdateWorkAreaCategoryResult NotFound() =>
        new(UpdateWorkAreaCategoryOutcome.NotFound, null);
}
