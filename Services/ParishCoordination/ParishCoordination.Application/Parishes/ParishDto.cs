using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Application.Parishes;

public sealed record ParishDto(
    Guid Id,
    string Name,
    string? Address,
    string? Description,
    ParishStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    int Version)
{
    public static ParishDto From(Parish parish)
    {
        return new ParishDto(
            parish.Id,
            parish.Name,
            parish.Address,
            parish.Description,
            parish.Status,
            parish.CreatedAt,
            parish.UpdatedAt,
            parish.Version);
    }
}
