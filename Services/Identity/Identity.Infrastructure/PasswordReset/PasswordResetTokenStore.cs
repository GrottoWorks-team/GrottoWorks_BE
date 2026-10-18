using Identity.Application.Auth.PasswordReset;
using Identity.Domain.Entities;
using Identity.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.PasswordReset;

public sealed class PasswordResetTokenStore(IdentityDbContext dbContext) : IPasswordResetTokenStore
{
    public Task<PasswordResetToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        return dbContext.PasswordResetTokens
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);
    }

    public async Task InvalidateActiveForUserAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        // Tracked update (not ExecuteUpdate) so it commits together with the caller's other changes.
        var active = await dbContext.PasswordResetTokens
            .Where(token => token.UserId == userId && token.UsedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in active)
        {
            token.MarkUsed(now);
        }
    }

    public async Task<bool> TryConsumeAsync(
        PasswordResetToken token,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        // Conditional UPDATE: of two concurrent consumers exactly one sees 1 affected row
        // (PostgreSQL re-evaluates the WHERE clause after taking the row lock).
        var claimed = await dbContext.PasswordResetTokens
            .Where(candidate => candidate.PasswordResetTokenId == token.PasswordResetTokenId &&
                                candidate.UsedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(candidate => candidate.UsedAt, (DateTimeOffset?)now),
                cancellationToken);

        if (claimed == 0)
        {
            return false;
        }

        // Keep the tracked instance consistent with the row so a later SaveChanges writes nothing stale.
        token.MarkUsed(now);
        dbContext.Entry(token).Property(entry => entry.UsedAt).IsModified = false;
        return true;
    }

    public void Add(PasswordResetToken token)
    {
        dbContext.PasswordResetTokens.Add(token);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
