using Microsoft.EntityFrameworkCore;
using ParishCoordination.Application.Parishes;
using ParishCoordination.Infrastructure.Data;

namespace ParishCoordination.Infrastructure.Parishes;

public sealed class ParishReadService(ParishCoordinationDbContext dbContext)
    : IParishReadService
{
    public async Task<IReadOnlyList<ParishResponse>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Parishes
            .AsNoTracking()
            .OrderBy(parish => parish.Name)
            .ThenBy(parish => parish.Id)
            .Select(parish => new ParishResponse(
                parish.Id,
                parish.Name,
                parish.Address,
                parish.Description,
                parish.Status,
                parish.CreatedAt,
                parish.UpdatedAt))
            .ToListAsync(cancellationToken);
    }
}
