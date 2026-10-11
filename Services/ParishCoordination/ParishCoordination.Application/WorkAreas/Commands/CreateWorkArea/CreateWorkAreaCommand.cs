namespace ParishCoordination.Application.WorkAreas.Commands.CreateWorkArea;

public sealed record CreateWorkAreaCommand(
    Guid SeasonId,
    string Name,
    string? Description,
    Guid? CategoryId,
    Guid? CommunityId,
    DateOnly? StartDate,
    DateOnly? Deadline);
