using ParishCoordination.Application.Parishes;

namespace ParishCoordination.Api.Responses;

public sealed record ParishListResponse(IReadOnlyList<ParishDto> Data);
