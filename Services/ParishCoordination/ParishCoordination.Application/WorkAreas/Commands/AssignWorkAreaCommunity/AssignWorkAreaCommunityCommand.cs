namespace ParishCoordination.Application.WorkAreas.Commands.AssignWorkAreaCommunity;

public sealed record AssignWorkAreaCommunityCommand(
    Guid WorkAreaId,
    Guid? CommunityId,
    int? ExpectedVersion);
