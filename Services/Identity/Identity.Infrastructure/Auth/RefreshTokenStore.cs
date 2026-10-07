using Identity.Application.Auth;
using Identity.Domain.Entities;
using Identity.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Auth;

public sealed class RefreshTokenStore(IdentityDbContext dbContext) : IRefreshTokenStore
{
    public Task<RefreshToken?> GetByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        return dbContext.RefreshTokens
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);
    }

    public void Add(RefreshToken refreshToken)
    {
        dbContext.RefreshTokens.Add(refreshToken);
    }

    public async Task<bool> TryRotateAsync(
        RefreshToken current,
        RefreshToken next,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Conditional UPDATE: PostgreSQL re-checks the WHERE clause after acquiring the row lock,
        // so of two concurrent rotations exactly one sees 1 affected row.
        // replaced_by_token_id is set afterwards (FK to the new row, which does not exist yet).
        var claimed = await dbContext.RefreshTokens
            .Where(token => token.RefreshTokenId == current.RefreshTokenId &&
                            token.RevokedAt == null &&
                            token.ReplacedByTokenId == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(token => token.RevokedAt, (DateTimeOffset?)now)
                    .SetProperty(token => token.RevokedReason, (string?)RefreshToken.ReasonReplaced),
                cancellationToken);

        if (claimed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        current.RotateWith(next.RefreshTokenId, now);
        dbContext.RefreshTokens.Add(next);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    public async Task RevokeFamilyAsync(
        Guid familyId,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var family = await dbContext.RefreshTokens
            .Where(token => token.FamilyId == familyId)
            .ToListAsync(cancellationToken);

        foreach (var token in family)
        {
            token.Revoke(reason, now);
        }
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
