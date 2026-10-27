using BuildingBlocks.Web;
using Reporting.Infrastructure.Data;

namespace Reporting.Api.Common;

/// <summary>Reports the database as ready when a connection can be opened (F-PLT-03).</summary>
public sealed class DatabaseReadinessProbe(ReportingDbContext dbContext) : IReadinessProbe
{
    public Task<bool> IsReadyAsync(CancellationToken cancellationToken = default) =>
        dbContext.Database.CanConnectAsync(cancellationToken);
}
