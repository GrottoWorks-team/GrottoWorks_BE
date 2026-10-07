using Identity.Application.Abstractions;
using Identity.Infrastructure.Data;

namespace Identity.Infrastructure.Data;

public sealed class DatabaseReadinessProbe(IdentityDbContext dbContext) : IReadinessProbe
{
    public Task<bool> IsReadyAsync(CancellationToken cancellationToken = default) =>
        dbContext.Database.CanConnectAsync(cancellationToken);
}
