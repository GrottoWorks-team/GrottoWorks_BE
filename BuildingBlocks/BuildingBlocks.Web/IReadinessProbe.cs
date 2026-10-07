namespace BuildingBlocks.Web;

/// <summary>
/// Readiness check resolved by <see cref="HealthEndpoints"/> for <c>/health/ready</c> (F-PLT-03).
/// Implementations typically probe the service database.
/// </summary>
public interface IReadinessProbe
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken = default);
}
