using ParishCoordination.Application.Parishes;

namespace ParishCoordination.Api.Responses;

public sealed record ParishListResponse(IReadOnlyList<ParishResponse> Data);
