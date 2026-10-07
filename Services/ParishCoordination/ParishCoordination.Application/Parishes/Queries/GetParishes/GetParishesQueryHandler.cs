namespace ParishCoordination.Application.Parishes.Queries.GetParishes;

public sealed class GetParishesQueryHandler(IParishRepository parishRepository)
{
    public async Task<IReadOnlyList<ParishDto>> HandleAsync(
        GetParishesQuery query,
        CancellationToken cancellationToken = default)
    {
        var parishes = await parishRepository.GetAllAsync(cancellationToken);

        return parishes
            .Select(ParishDto.From)
            .ToList();
    }
}
