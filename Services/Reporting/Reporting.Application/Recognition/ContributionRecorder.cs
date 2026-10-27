using Reporting.Application.Abstractions;
using Reporting.Contracts.Events;
using Reporting.Domain;
using Reporting.Domain.Recognition;

namespace Reporting.Application.Recognition;

/// <summary>What one event contributes, before it is matched with an existing contribution.</summary>
public sealed record ContributionDraft(
    string TypeCode,
    Guid? UserId,
    Guid? DonationId,
    Guid? DonorUserId,
    Guid SeasonId,
    Guid ParishId,
    Guid? WorkAreaId,
    string? ReferenceType,
    Guid? ReferenceId,
    string? Description,
    DateTimeOffset Date)
{
    public static ContributionDraft ForUser(string typeCode, Guid userId, Guid seasonId, Guid parishId, Guid? workAreaId, string referenceType,
        Guid referenceId, string? description, DateTimeOffset date) =>
        new(typeCode, userId, null, null, seasonId, parishId, workAreaId, referenceType, referenceId, description, date);

    public static ContributionDraft ForDonation(string typeCode, Guid donationId, Guid? donorUserId, Guid seasonId, Guid parishId, Guid? workAreaId,
        string? description, DateTimeOffset date) =>
        new(typeCode, null, donationId, donorUserId, seasonId, parishId, workAreaId, null, null, description, date);
}

/// <summary>A point awarded in this unit of work, with what its event needs.</summary>
public sealed record AwardedPoint(RecognitionPoint Point, Contribution Contribution, ContributionType Type, RecognitionRule Rule);

public interface IContributionStore
{
    Task<Contribution?> GetByDonationAsync(Guid donationId, CancellationToken cancellationToken = default);

    Task<Contribution?> GetByReferenceAsync(string referenceType, Guid referenceId, Guid userId, CancellationToken cancellationToken = default);

    Task<bool> PointExistsAsync(Guid contributionId, Guid ruleId, Guid userId, CancellationToken cancellationToken = default);

    void Add(Contribution contribution);

    void Add(RecognitionPoint point);

    /// <summary>Saves contributions, points and the inbox row together; a concurrent insert of the same row → 409 (retry).</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// F-RPT-02: upserts the contribution of each draft (one per donation, one per reference and user) and awards the points
/// of every ACTIVE rule of an ACTIVE type whose period covers the contribution date (parish calendar) to the credited
/// account. Awarding is idempotent per (contribution, rule, user), so a later receipt or a donor linked later only adds
/// what is missing; external donors get no points.
/// </summary>
public sealed class ContributionRecorder(IContributionStore store, IRecognitionCatalogStore catalog, IIntegrationEventPublisher publisher)
{
    public async Task<(int Contributions, IReadOnlyList<AwardedPoint> Points)> RecordAsync(
        IReadOnlyList<ContributionDraft> drafts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var points = new List<AwardedPoint>();
        var created = 0;
        foreach (var draft in drafts)
        {
            var type = await catalog.GetTypeByCodeAsync(draft.TypeCode, cancellationToken)
                ?? throw new InvalidOperationException($"System contribution type {draft.TypeCode} is missing (seed).");
            var (contribution, isNew) = await UpsertAsync(draft, type, cancellationToken);
            created += isNew ? 1 : 0;
            if (contribution.CreditedUserId is not { } userId || type.Status != ContributionTypeStatus.Active)
            {
                continue;
            }

            var date = ParishClock.Today(contribution.ContributionDate);
            foreach (var rule in (await catalog.ListRulesAsync(RecognitionRuleStatus.Active, type.ContributionTypeId, cancellationToken))
                     .Where(rule => rule.AppliesOn(date)))
            {
                if (!isNew && await store.PointExistsAsync(contribution.ContributionId, rule.RecognitionRuleId, userId, cancellationToken))
                {
                    continue;
                }

                var point = RecognitionPoint.Award(contribution, userId, rule, now);
                store.Add(point);
                points.Add(new AwardedPoint(point, contribution, type, rule));
            }
        }

        return (created, points);
    }

    public Task SaveAsync(CancellationToken cancellationToken) => store.SaveChangesAsync(cancellationToken);

    public async Task PublishAsync(IReadOnlyList<AwardedPoint> points, CancellationToken cancellationToken)
    {
        foreach (var (point, contribution, type, rule) in points)
        {
            await publisher.PublishAsync(
                RecognitionEntryCreated.EventType,
                RecognitionEntryCreated.Version,
                new RecognitionEntryCreated(point.RecognitionPointId, point.AwardedToUserId, point.SeasonId, contribution.ParishId,
                    contribution.ContributionId, type.Code, rule.RecognitionRuleId, rule.Name, rule.RuleVersion, point.AwardedPoints,
                    point.CreatedAt),
                "recognition_point",
                point.RecognitionPointId,
                cancellationToken);
        }
    }

    private async Task<(Contribution Contribution, bool IsNew)> UpsertAsync(ContributionDraft draft, ContributionType type, CancellationToken cancellationToken)
    {
        if (draft.DonationId is { } donationId)
        {
            var existing = await store.GetByDonationAsync(donationId, cancellationToken);
            if (existing is not null)
            {
                EnsureSameSeason(existing, draft);
                existing.UpdateDonation(draft.DonorUserId, draft.Description);
                return (existing, false);
            }

            var donation = Contribution.ForDonation(donationId, draft.DonorUserId, draft.SeasonId, draft.ParishId, draft.WorkAreaId,
                type.ContributionTypeId, draft.Description, draft.Date);
            store.Add(donation);
            return (donation, true);
        }

        var userId = draft.UserId!.Value;
        var current = await store.GetByReferenceAsync(draft.ReferenceType!, draft.ReferenceId!.Value, userId, cancellationToken);
        if (current is not null)
        {
            EnsureSameSeason(current, draft);
            current.Describe(draft.Description);
            return (current, false);
        }

        var contribution = Contribution.ForUser(userId, draft.SeasonId, draft.ParishId, draft.WorkAreaId, type.ContributionTypeId,
            draft.ReferenceType!, draft.ReferenceId!.Value, draft.Description, draft.Date);
        store.Add(contribution);
        return (contribution, true);
    }

    /// <summary>ERD note: the contribution's season must match its source.</summary>
    private static void EnsureSameSeason(Contribution existing, ContributionDraft draft)
    {
        if (existing.SeasonId != draft.SeasonId)
        {
            throw DomainRuleException.Validation("EVENT_SEASON_MISMATCH", "The event belongs to another season than its recorded contribution.");
        }
    }
}
