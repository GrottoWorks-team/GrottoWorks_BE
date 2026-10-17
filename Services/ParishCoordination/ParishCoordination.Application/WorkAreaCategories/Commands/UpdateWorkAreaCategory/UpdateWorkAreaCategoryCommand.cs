using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Application.WorkAreaCategories.Commands.UpdateWorkAreaCategory;

public sealed record UpdateWorkAreaCategoryCommand(
    Guid CategoryId,
    string? Name,
    bool ShouldUpdateDescription,
    string? Description,
    WorkAreaCategoryStatus? Status);
