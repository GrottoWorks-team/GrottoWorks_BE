namespace ParishCoordination.Application.WorkAreas.Commands.AssignMaterialOfficer;

public sealed record AssignMaterialOfficerCommand(
    Guid WorkAreaId,
    Guid MaterialOfficerUserId,
    int? ExpectedVersion);
