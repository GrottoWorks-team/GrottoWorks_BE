namespace ParishCoordination.Api.Requests;

public sealed record SaveParishMapRequest(Guid FileId, string FileName);
