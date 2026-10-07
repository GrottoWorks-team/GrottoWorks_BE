using RefreshTokenEntity = Identity.Domain.Entities.RefreshToken;

namespace Identity.Application.Auth;

/// <summary>Refresh token store (technical table, F-IDN-03).</summary>
public interface IRefreshTokenStore
{
    Task<RefreshTokenEntity?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    void Add(RefreshTokenEntity refreshToken);

    /// <summary>Revokes every token of a family — used on logout and on reuse detection.</summary>
    Task RevokeFamilyAsync(Guid familyId, string reason, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
