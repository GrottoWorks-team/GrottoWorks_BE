namespace ParishCoordination.Application.WorkAreaCategories.Commands.CreateWorkAreaCategory;

public sealed record CreateWorkAreaCategoryCommand(
    string Code,
    string Name,
    string? Description);
