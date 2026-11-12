namespace ParishCoordination.Application.Users;

public enum IdentityUserStatus
{
    Active,
    Locked,
    Inactive
}

public sealed record IdentityUser(
    Guid UserId,
    IdentityUserStatus Status,
    string Role,
    Guid? ParishId);

public interface IIdentityUserLookup
{
    Task<IdentityUser?> GetByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Batch variant of <see cref="IIdentityUserLookup"/> backed by <c>GET /internal/users?ids=</c>.
/// Season cloning validates every copied leader/officer in one call instead of N calls.
/// </summary>
public interface IIdentityUserBatchLookup
{
    Task<IReadOnlyDictionary<Guid, IdentityUser>> GetByIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);
}

public sealed class IdentityUserLookupUnavailableException(
    string message,
    Exception? innerException = null)
    : Exception(message, innerException);
