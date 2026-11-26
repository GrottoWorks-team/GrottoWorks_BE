using BuildingBlocks.Security;

namespace ParishCoordination.Application.Parishes;

public sealed record GetParishMapQuery(Guid ParishId);

public sealed class GetParishMapQueryHandler(IParishRepository parishRepository, ICurrentUser currentUser)
{
    public async Task<ParishMapDto?> HandleAsync(GetParishMapQuery query, CancellationToken cancellationToken)
    {
        ParishAccess.EnsureSameParish(currentUser, query.ParishId);
        var parish = await parishRepository.GetByIdAsync(query.ParishId, cancellationToken);
        if (parish is null) return null;
        return new ParishMapDto(parish.MapImageFileId, parish.MapImageFileName, parish.UpdatedAt);
    }
}
