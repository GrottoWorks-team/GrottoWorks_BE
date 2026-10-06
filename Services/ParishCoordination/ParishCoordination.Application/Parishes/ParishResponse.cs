namespace ParishCoordination.Application.Parishes;

public sealed record ParishResponse(
    Guid Id,
    string Name,
    string? Address,
    string? Description,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
