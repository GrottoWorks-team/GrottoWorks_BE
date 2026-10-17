using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Application.WorkAreaCategories;

public sealed record WorkAreaCategoryDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    WorkAreaCategoryStatus Status)
{
    public static WorkAreaCategoryDto From(WorkAreaCategory category)
    {
        return new WorkAreaCategoryDto(
            category.Id,
            category.Code,
            category.Name,
            category.Description,
            category.Status);
    }
}
