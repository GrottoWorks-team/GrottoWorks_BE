using BuildingBlocks.Security;

namespace ParishCoordination.Application.Parishes.Queries.GetParish;

public sealed class GetParishQueryHandler(
    IParishRepository parishRepository,
    ICurrentUser currentUser)
{
    public async Task<ParishDto?> HandleAsync(
        GetParishQuery query,
        CancellationToken cancellationToken = default)
    {
        var parish = await parishRepository.GetByIdAsync(
            query.ParishId,
            cancellationToken);

        if (parish is null)
        {
            return null;
        }

        ParishAccess.EnsureSameParish(currentUser, parish.Id);
        return ParishDto.From(parish);
    }
}
