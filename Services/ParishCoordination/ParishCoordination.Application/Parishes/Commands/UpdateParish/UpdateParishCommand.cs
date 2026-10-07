using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Application.Parishes.Commands.UpdateParish;

public sealed record UpdateParishCommand(
    Guid ParishId,
    string? Name,
    bool ShouldUpdateAddress,
    string? Address,
    bool ShouldUpdateDescription,
    string? Description,
    ParishStatus? Status,
    int? ExpectedVersion);
