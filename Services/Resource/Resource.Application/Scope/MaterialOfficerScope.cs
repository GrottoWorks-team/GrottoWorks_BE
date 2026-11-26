using BuildingBlocks.Security;
using Resource.Domain;
using Resource.Domain.Contributions;
using Resource.Domain.Scope;

namespace Resource.Application.Scope;

/// <summary>
/// Which material officer may act on a record that belongs to a work area or only to a season (donations, borrowed
/// items): with a work area → the MO of that work area; season only → the MO of any work area of the season. Archived
/// seasons are read-only (BR-07).
/// </summary>
public sealed class MaterialOfficerScope(
    WorkAreaAccess access,
    ParishWarehouseAccess parishAccess,
    IWorkAreaScopeStore scopes,
    ICurrentUser currentUser)
{
    /// <summary>
    /// Donations: MATERIAL goes to the receiving work area (BR-37) or, when no work area is given, to the parish
    /// warehouse (PARISH only); MONEY may be for the whole season.
    /// </summary>
    public async Task<(WorkAreaScope? Scope, Guid SeasonId, Guid ParishId)> ResolveAsync(
        DonationType type,
        Guid? workAreaId,
        Guid? seasonId,
        CancellationToken cancellationToken)
    {
        if (workAreaId is null && type == DonationType.Material)
        {
            var season = seasonId
                ?? throw DomainRuleException.Validation("DONATION_SEASON_REQUIRED", "Choose the season for a parish warehouse donation.");
            var (_, parishId) = await parishAccess.RequireWritableSeasonAsync(season, cancellationToken);
            return (null, season, parishId);
        }

        return await ForWorkAreaOrSeasonAsync(
            workAreaId, seasonId, "DONATION_SEASON_MISMATCH", "DONATION_SEASON_REQUIRED", cancellationToken);
    }

    public async Task<(WorkAreaScope? Scope, Guid SeasonId, Guid ParishId)> ForWorkAreaOrSeasonAsync(
        Guid? workAreaId,
        Guid? seasonId,
        string seasonMismatchCode,
        string seasonRequiredCode,
        CancellationToken cancellationToken)
    {
        if (workAreaId is { } areaId)
        {
            var scope = await access.ForMaterialOfficerWriteAsync(areaId, cancellationToken);
            if (seasonId is { } season && season != scope.SeasonId)
            {
                throw DomainRuleException.Validation(seasonMismatchCode, "The work area belongs to another season.");
            }

            return (scope, scope.SeasonId, scope.ParishId);
        }

        var requiredSeason = seasonId
            ?? throw DomainRuleException.Validation(seasonRequiredCode, "Choose the season (or a work area).");
        var parishId = access.RequireParish();
        var areas = await scopes.ListByParishAsync(parishId, requiredSeason, cancellationToken);
        if (areas.Count == 0)
        {
            throw DomainRuleException.NotFound("SEASON_NOT_FOUND", "The season does not exist or is outside your parish.");
        }

        if (!areas.Any(area => area.MaterialOfficerUserId == currentUser.UserId))
        {
            throw DomainRuleException.Forbidden("WORK_AREA_SCOPE_DENIED", "Only a material officer of this season can do this.");
        }

        WorkAreaAccess.EnsureWritable(areas[0]);
        return (null, requiredSeason, parishId);
    }
}
