using Identity.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Identity.Infrastructure.Parish;

/// <summary>
/// INTERIM — ParishCoordination does not expose communities yet (F-PAR-02).
/// Accepts every community and logs a warning so the gap stays visible.
/// Replace with an implementation backed by a Parish internal endpoint or a community projection
/// (event <c>parish.community.*</c>) before the S1 demo; the handler and tests already go through
/// <see cref="IParishDirectory"/>, so only the DI registration changes.
/// </summary>
public sealed class PendingParishDirectory(ILogger<PendingParishDirectory> logger) : IParishDirectory
{
    public Task<bool> CommunityBelongsToParishAsync(
        Guid communityId,
        Guid parishId,
        CancellationToken cancellationToken = default)
    {
        logger.LogWarning(
            "Community {CommunityId} accepted for parish {ParishId} WITHOUT verification (Parish integration pending, F-PAR-02).",
            communityId,
            parishId);

        return Task.FromResult(true);
    }
}
