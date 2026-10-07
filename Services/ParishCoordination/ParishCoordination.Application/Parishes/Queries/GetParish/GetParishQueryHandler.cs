namespace ParishCoordination.Application.Parishes.Queries.GetParish;

public sealed class GetParishQueryHandler(IParishRepository parishRepository)
{
    public async Task<ParishDto?> HandleAsync(
        GetParishQuery query,
        CancellationToken cancellationToken = default)
    {
        var parish = await parishRepository.GetByIdAsync(
            query.ParishId,
            cancellationToken);

        return parish is null ? null : ParishDto.From(parish);
    }
}
