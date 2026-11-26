using BuildingBlocks.Security;
using Resource.Domain;
using Resource.Domain.Scope;

namespace Resource.Application.Scope;

/// <summary>
/// Authorization for the parish warehouse (F-RES-21). Writes belong to the PARISH role only; ADMIN may read a parish
/// it names explicitly. The season must belong to that parish and must not be archived (BR-07), and the target work
/// area must be in the same parish and season.
/// </summary>
public sealed class ParishWarehouseAccess(IWorkAreaScopeStore scopes, ICurrentUser currentUser)
{
    public Guid RequireWriteParish()
    {
        if (currentUser.Role != GrottoWorksRoles.Parish)
        {
            throw DomainRuleException.Forbidden(
                "PARISH_SCOPE_REQUIRED",
                "Only the parish committee can change the parish warehouse.");
        }

        return currentUser.ParishId
            ?? throw DomainRuleException.Forbidden("PARISH_SCOPE_REQUIRED", "Your account is not attached to a parish.");
    }

    public Guid RequireReadParish(Guid? requestedParishId = null)
    {
        if (currentUser.Role == GrottoWorksRoles.Admin)
        {
            return requestedParishId
                ?? throw DomainRuleException.Validation("PARISH_REQUIRED", "ADMIN must name the parish to read.");
        }

        return RequireWriteParish();
    }

    public async Task<(SeasonScope Season, Guid ParishId)> RequireWritableSeasonAsync(
        Guid seasonId,
        CancellationToken cancellationToken = default)
    {
        var parishId = RequireWriteParish();
        var season = await ReadSeasonAsync(seasonId, parishId, cancellationToken);
        if (season.Status == SeasonStatus.Archived)
        {
            throw new DomainRuleException("SEASON_READ_ONLY", "The season is archived; its data is read-only.");
        }

        return (season, parishId);
    }

    public Task<SeasonScope> RequireReadableSeasonAsync(
        Guid seasonId,
        Guid parishId,
        CancellationToken cancellationToken = default) =>
        ReadSeasonAsync(seasonId, parishId, cancellationToken);

    public async Task<WorkAreaScope> RequireTargetAreaAsync(
        Guid workAreaId,
        Guid parishId,
        Guid seasonId,
        CancellationToken cancellationToken = default)
    {
        var scope = await scopes.GetAsync(workAreaId, cancellationToken);
        if (scope is null || scope.ParishId != parishId || scope.SeasonId != seasonId)
        {
            throw DomainRuleException.NotFound(
                "WORK_AREA_NOT_FOUND",
                "The work area does not exist in this parish and season.");
        }

        WorkAreaAccess.EnsureWritable(scope);
        return scope;
    }

    private async Task<SeasonScope> ReadSeasonAsync(Guid seasonId, Guid parishId, CancellationToken cancellationToken)
    {
        var season = await scopes.GetSeasonAsync(seasonId, cancellationToken)
            ?? throw DomainRuleException.NotFound("SEASON_NOT_FOUND", "The season does not exist.");
        if (season.ParishId is { } owner && owner != parishId)
        {
            throw DomainRuleException.NotFound("SEASON_NOT_FOUND", "The season does not exist in this parish.");
        }

        return season;
    }
}
