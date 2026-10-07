namespace Identity.Application.Abstractions;

/// <summary>Readiness probe abstraction so the API layer does not touch the database directly.</summary>
public interface IReadinessProbe
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken = default);
}
