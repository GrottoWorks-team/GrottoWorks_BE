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

public sealed class IdentityUserLookupUnavailableException(
    string message,
    Exception? innerException = null)
    : Exception(message, innerException);
