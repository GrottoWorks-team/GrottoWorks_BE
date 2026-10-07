using BuildingBlocks.Web;
using Microsoft.EntityFrameworkCore;
using ParishCoordination.Infrastructure.Data;

namespace ParishCoordination.Api.Common;

/// <summary>Reports the database as ready when a connection can be opened (F-PLT-03).</summary>
public sealed class DatabaseReadinessProbe(ParishCoordinationDbContext dbContext) : IReadinessProbe
{
    public Task<bool> IsReadyAsync(CancellationToken cancellationToken = default) =>
        dbContext.Database.CanConnectAsync(cancellationToken);
}
