namespace ParishCoordination.Application.Communities.Commands.CreateCommunity;

public sealed record CreateCommunityCommand(
    Guid ParishId,
    string Name,
    string? Description);
