using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Application.Communities.Commands.UpdateCommunity;

public sealed record UpdateCommunityCommand(
    Guid CommunityId,
    string? Name,
    bool ShouldUpdateDescription,
    string? Description,
    CommunityStatus? Status,
    int? ExpectedVersion);
