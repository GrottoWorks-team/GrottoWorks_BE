namespace Resource.Application.Users;

/// <summary>A user as returned by Identity <c>GET /internal/users?ids=</c> (BuildingBlocks README §5).</summary>
public sealed record IdentityUser(Guid UserId, string DisplayName, string Status, string Role, Guid? ParishId);

/// <summary>Service-to-service lookup of Identity users (F-IDN-09).</summary>
public interface IIdentityUserLookup
{
    /// <summary>Returns <c>null</c> when the user does not exist; throws <see cref="IdentityUserLookupUnavailableException"/> otherwise.</summary>
    Task<IdentityUser?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed class IdentityUserLookupUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
