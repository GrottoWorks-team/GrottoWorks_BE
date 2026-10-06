using ParishCoordination.Application.Parishes;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Infrastructure.Data;

namespace ParishCoordination.Infrastructure.Parishes;

public sealed class ParishWriteService(ParishCoordinationDbContext dbContext)
    : IParishWriteService
{
    public async Task<ParishResponse> CreateAsync(
        CreateParishCommand command,
        CancellationToken cancellationToken = default)
    {
        var parish = Parish.Create(
            command.Name,
            command.Address,
            command.Description,
            DateTimeOffset.UtcNow);

        dbContext.Parishes.Add(parish);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ParishResponse(
            parish.Id,
            parish.Name,
            parish.Address,
            parish.Description,
            parish.Status,
            parish.CreatedAt,
            parish.UpdatedAt);
    }
}
