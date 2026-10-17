namespace ParishCoordination.Application.WorkAreaCategories.Commands.CreateWorkAreaCategory;

public enum CreateWorkAreaCategoryOutcome
{
    Success,
    CodeConflict
}

public sealed record CreateWorkAreaCategoryResult(
    CreateWorkAreaCategoryOutcome Outcome,
    WorkAreaCategoryDto? Category)
{
    public static CreateWorkAreaCategoryResult Success(WorkAreaCategoryDto category) =>
        new(CreateWorkAreaCategoryOutcome.Success, category);

    public static CreateWorkAreaCategoryResult CodeConflict() =>
        new(CreateWorkAreaCategoryOutcome.CodeConflict, null);
}
