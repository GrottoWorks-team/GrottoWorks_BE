using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Application.Seasons;

public sealed record SeasonDto(
    Guid Id,
    Guid ParishId,
    string Name,
    int SeasonYear,
    DateOnly StartDate,
    DateOnly EndDate,
    SeasonStatus Status,
    string? Description,
    decimal? EstimatedBudget,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt)
{
    public static SeasonDto From(Season season)
    {
        return new SeasonDto(
            season.Id,
            season.ParishId,
            season.Name,
            season.SeasonYear,
            season.StartDate,
            season.EndDate,
            season.Status,
            season.Description,
            season.EstimatedBudget,
            season.CreatedAt,
            season.UpdatedAt);
    }
}
