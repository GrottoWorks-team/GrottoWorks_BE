namespace ParishCoordination.Application.WorkAreas.Commands.AssignWorkAreaLeader;

public sealed record AssignWorkAreaLeaderCommand(
    Guid WorkAreaId,
    Guid LeaderUserId,
    int? ExpectedVersion);
