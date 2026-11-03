namespace ParishCoordination.Application.WorkAreas.Commands.UpdateWorkArea;

public sealed record UpdateWorkAreaCommand(
    Guid WorkAreaId,
    string? Name,
    bool ShouldUpdateDescription,
    string? Description,
    Guid? CategoryId,
    DateOnly? StartDate,
    DateOnly? Deadline,
    int? ExpectedVersion,
    bool ShouldUpdateLatitude = false,
    decimal? Latitude = null,
    bool ShouldUpdateLongitude = false,
    decimal? Longitude = null);
