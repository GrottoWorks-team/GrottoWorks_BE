using TaskExecution.Domain.Scopes;

namespace TaskExecution.Application.Scopes;

public interface IScopeProjectionStore
{
    Task<SeasonScope?> GetSeasonAsync(Guid seasonId, CancellationToken cancellationToken = default);
    Task<WorkAreaScope?> GetWorkAreaAsync(Guid workAreaId, CancellationToken cancellationToken = default);
    void Add(SeasonScope scope);
    void Add(WorkAreaScope scope);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
