using ParishCoordination.Application.Communities;

namespace ParishCoordination.Api.Responses;

public sealed record CommunityListResponse(IReadOnlyList<CommunityDto> Data);
