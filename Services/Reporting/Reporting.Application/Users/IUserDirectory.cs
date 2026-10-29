namespace Reporting.Application.Users;

/// <summary>
/// Display names from Identity <c>GET /internal/users?ids=</c> (F-IDN-09) for rankings and reports. Best effort: an
/// unknown user or an unavailable Identity leaves the name out, the report itself never fails for it.
/// </summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);
}

/// <summary>Used when no Identity address is configured (tests, isolated runs).</summary>
public sealed class NoUserDirectory : IUserDirectory
{
    public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}
