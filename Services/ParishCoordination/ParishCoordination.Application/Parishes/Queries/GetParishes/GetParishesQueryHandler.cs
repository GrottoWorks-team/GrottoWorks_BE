namespace ParishCoordination.Application.Parishes.Queries.GetParishes;

public sealed class GetParishesQueryHandler(IParishRepository parishRepository)
{
    public async Task<GetParishesResult> HandleAsync(
        GetParishesQuery query,
        CancellationToken cancellationToken = default)
    {
        var sortDescending = query.Sort?.EndsWith(",desc", StringComparison.OrdinalIgnoreCase) == true;

        var (parishes, totalItems) = await parishRepository.GetPagedAsync(
            query.Page,
            query.Size,
            sortDescending,
            cancellationToken);

        var items = parishes
            .Select(ParishDto.From)
            .ToList();

        return new GetParishesResult(items, totalItems);
    }
}
