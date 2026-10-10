using BuildingBlocks.Security;

namespace ParishCoordination.Application.Parishes.Queries.GetParishes;

public sealed class GetParishesQueryHandler(
    IParishRepository parishRepository,
    ICurrentUser currentUser)
{
    public async Task<GetParishesResult> HandleAsync(
        GetParishesQuery query,
        CancellationToken cancellationToken = default)
    {
        var sortDescending = query.Sort?.EndsWith(",desc", StringComparison.OrdinalIgnoreCase) == true;
        var parishId = string.Equals(currentUser.Role, GrottoWorksRoles.Admin, StringComparison.Ordinal)
            ? null
            : currentUser.ParishId;

        ParishAccess.EnsureSameParish(currentUser, parishId);

        var (parishes, totalItems) = await parishRepository.GetPagedAsync(
            query.Page,
            query.Size,
            sortDescending,
            parishId,
            cancellationToken);

        var items = parishes
            .Select(ParishDto.From)
            .ToList();

        return new GetParishesResult(items, totalItems);
    }
}
