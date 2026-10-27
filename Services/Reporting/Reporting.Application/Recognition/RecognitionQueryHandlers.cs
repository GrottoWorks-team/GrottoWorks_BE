using BuildingBlocks.Security;
using Reporting.Application.Scope;
using Reporting.Application.Users;
using Reporting.Domain;
using Reporting.Domain.Recognition;

namespace Reporting.Application.Recognition;

public sealed record RecognitionEntryDto(
    Guid Id,
    int Points,
    Guid ContributionId,
    string ContributionTypeCode,
    string ContributionTypeName,
    string? ContributionDescription,
    DateTimeOffset ContributionDate,
    Guid RuleId,
    string RuleName,
    int RuleVersion,
    string? Reason,
    DateTimeOffset CreatedAt);

public sealed record RecognitionTypeSummaryDto(string ContributionTypeCode, string ContributionTypeName, int Contributions, int Points);

/// <summary><c>RecognitionSummary</c> (F-RPT-03): the caller's contributions and points; points have no cash value (BR-56).</summary>
public sealed record RecognitionSummaryDto(
    Guid UserId,
    Guid? SeasonId,
    int TotalPoints,
    int Contributions,
    IReadOnlyList<RecognitionTypeSummaryDto> ByType,
    IReadOnlyList<RecognitionEntryDto> Entries);

/// <param name="DisplayName">From Identity; null when unknown or Identity is unavailable.</param>
public sealed record RecognitionRankingRowDto(
    int Rank,
    Guid UserId,
    string? DisplayName,
    int TotalPoints,
    int Contributions,
    IReadOnlyList<RecognitionTypeSummaryDto> ByType);

/// <summary><c>RecognitionRanking</c> (F-RPT-03): accounts by points; external donors count as contributions only.</summary>
public sealed record RecognitionRankingDto(
    Guid SeasonId,
    Guid ParishId,
    int TotalPoints,
    int Contributions,
    int ExternalDonorContributions,
    int Contributors,
    IReadOnlyList<RecognitionRankingRowDto> Ranking,
    DateTimeOffset GeneratedAt);

public interface IRecognitionQueryStore
{
    /// <summary>Contributions of the parish (optionally one season), optionally only those credited to one account.</summary>
    Task<IReadOnlyList<Contribution>> ListContributionsAsync(Guid parishId, Guid? seasonId, Guid? creditedUserId, CancellationToken cancellationToken = default);

    /// <summary>Points on contributions of the parish (optionally one season / one account).</summary>
    Task<IReadOnlyList<RecognitionPoint>> ListPointsAsync(Guid parishId, Guid? seasonId, Guid? userId, CancellationToken cancellationToken = default);
}

/// <summary>F-RPT-03 <c>getMyRecognition</c> (VOLUNTEER) and <c>getSeasonRecognition</c> (PARISH of the season).</summary>
public sealed class RecognitionQueryHandler(
    IRecognitionQueryStore store,
    IRecognitionCatalogStore catalog,
    IWorkAreaScopeStore scopes,
    WorkAreaAccess access,
    ICurrentUser currentUser,
    IUserDirectory users)
{
    public async Task<RecognitionSummaryDto> GetMineAsync(Guid? seasonId, CancellationToken cancellationToken = default)
    {
        var parishId = access.RequireParish();
        var userId = currentUser.UserId;
        var contributions = await store.ListContributionsAsync(parishId, seasonId, userId, cancellationToken);
        var points = await store.ListPointsAsync(parishId, seasonId, userId, cancellationToken);
        var (types, rules) = await CatalogAsync(cancellationToken);
        var byId = contributions.ToDictionary(contribution => contribution.ContributionId);

        var entries = points
            .Where(point => byId.ContainsKey(point.ContributionId))
            .OrderByDescending(point => point.CreatedAt)
            .Select(point =>
            {
                var contribution = byId[point.ContributionId];
                var type = types[contribution.ContributionTypeId];
                var rule = rules[point.RecognitionRuleId];
                return new RecognitionEntryDto(point.RecognitionPointId, point.AwardedPoints, contribution.ContributionId, type.Code, type.Name,
                    contribution.Description, contribution.ContributionDate, rule.RecognitionRuleId, rule.Name, rule.RuleVersion, point.Reason,
                    point.CreatedAt);
            })
            .ToList();
        return new RecognitionSummaryDto(userId, seasonId, entries.Sum(entry => entry.Points), contributions.Count,
            ByType(contributions, points, types), entries);
    }

    public async Task<RecognitionRankingDto> GetSeasonAsync(Guid seasonId, CancellationToken cancellationToken = default)
    {
        var parishId = access.RequireParish();
        var contributions = await store.ListContributionsAsync(parishId, seasonId, null, cancellationToken);
        if (contributions.Count == 0 && (await scopes.ListByParishAsync(parishId, seasonId, cancellationToken)).Count == 0)
        {
            throw DomainRuleException.NotFound("SEASON_NOT_FOUND", "The season does not exist or is outside your parish.");
        }

        var points = await store.ListPointsAsync(parishId, seasonId, null, cancellationToken);
        var (types, _) = await CatalogAsync(cancellationToken);
        var credited = contributions.Where(contribution => contribution.CreditedUserId is not null).ToList();
        var rows = credited
            .GroupBy(contribution => contribution.CreditedUserId!.Value)
            .Select(group =>
            {
                var userPoints = points.Where(point => point.AwardedToUserId == group.Key).ToList();
                return (UserId: group.Key, Points: userPoints.Sum(point => point.AwardedPoints), Contributions: group.Count(),
                    ByType: ByType(group.ToList(), userPoints, types));
            })
            .OrderByDescending(row => row.Points)
            .ThenByDescending(row => row.Contributions)
            .ThenBy(row => row.UserId)
            .ToList();

        var names = await users.GetDisplayNamesAsync(rows.Select(row => row.UserId).ToList(), cancellationToken);

        // Competition ranking: equal points share a rank (1, 1, 3).
        var ranking = new List<RecognitionRankingRowDto>(rows.Count);
        for (var index = 0; index < rows.Count; index++)
        {
            var rank = index > 0 && rows[index].Points == rows[index - 1].Points ? ranking[index - 1].Rank : index + 1;
            ranking.Add(new RecognitionRankingRowDto(rank, rows[index].UserId, names.GetValueOrDefault(rows[index].UserId), rows[index].Points,
                rows[index].Contributions, rows[index].ByType));
        }

        return new RecognitionRankingDto(seasonId, parishId, points.Sum(point => point.AwardedPoints), contributions.Count,
            contributions.Count - credited.Count, ranking.Count, ranking, DateTimeOffset.UtcNow);
    }

    private async Task<(Dictionary<Guid, ContributionType> Types, Dictionary<Guid, RecognitionRule> Rules)> CatalogAsync(CancellationToken cancellationToken) =>
        ((await catalog.ListTypesAsync(null, cancellationToken)).ToDictionary(type => type.ContributionTypeId),
            (await catalog.ListRulesAsync(null, null, cancellationToken)).ToDictionary(rule => rule.RecognitionRuleId));

    private static List<RecognitionTypeSummaryDto> ByType(
        IReadOnlyCollection<Contribution> contributions,
        IReadOnlyCollection<RecognitionPoint> points,
        IReadOnlyDictionary<Guid, ContributionType> types)
    {
        var typeOf = contributions.ToDictionary(contribution => contribution.ContributionId, contribution => contribution.ContributionTypeId);
        return contributions
            .GroupBy(contribution => contribution.ContributionTypeId)
            .Select(group => new RecognitionTypeSummaryDto(
                types[group.Key].Code,
                types[group.Key].Name,
                group.Count(),
                points.Where(point => typeOf.TryGetValue(point.ContributionId, out var typeId) && typeId == group.Key).Sum(point => point.AwardedPoints)))
            .OrderBy(summary => summary.ContributionTypeCode, StringComparer.Ordinal)
            .ToList();
    }
}
