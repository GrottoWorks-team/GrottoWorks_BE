using Reporting.Domain.Scope;

namespace Reporting.Application.Scope;

/// <summary>Projection store for <c>work_area_scope</c>.</summary>
public interface IWorkAreaScopeStore
{
    Task<WorkAreaScope?> GetAsync(Guid workAreaId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkAreaScope>> ListBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkAreaScope>> ListByIdsAsync(IReadOnlyCollection<Guid> workAreaIds, CancellationToken cancellationToken = default);

    /// <summary>Work areas of the parish, optionally narrowed to one season.</summary>
    Task<IReadOnlyList<WorkAreaScope>> ListByParishAsync(Guid parishId, Guid? seasonId, CancellationToken cancellationToken = default);

    void Add(WorkAreaScope scope);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
