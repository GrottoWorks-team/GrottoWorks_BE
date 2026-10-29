using Microsoft.EntityFrameworkCore;
using Reporting.Application.Ingestion;
using Reporting.Application.Recognition;
using Reporting.Domain;
using Reporting.Domain.Inbox;
using Reporting.Domain.Recognition;
using Reporting.Infrastructure.Data;
using Reporting.Infrastructure.Data.Configurations;

namespace Reporting.Infrastructure.Recognition;

public sealed class ContributionStore(ReportingDbContext dbContext) : IContributionStore, IInboxStore, IRecognitionQueryStore
{
    public Task<Contribution?> GetByDonationAsync(Guid donationId, CancellationToken cancellationToken = default) =>
        dbContext.Contributions.FirstOrDefaultAsync(contribution => contribution.DonationId == donationId, cancellationToken);

    public Task<Contribution?> GetByReferenceAsync(string referenceType, Guid referenceId, Guid userId, CancellationToken cancellationToken = default) =>
        dbContext.Contributions.FirstOrDefaultAsync(
            contribution => contribution.DonationId == null && contribution.ReferenceType == referenceType &&
                            contribution.ReferenceId == referenceId && contribution.ContributedByUserId == userId,
            cancellationToken);

    public Task<bool> PointExistsAsync(Guid contributionId, Guid ruleId, Guid userId, CancellationToken cancellationToken = default) =>
        dbContext.RecognitionPoints.AnyAsync(
            point => point.ContributionId == contributionId && point.RecognitionRuleId == ruleId && point.AwardedToUserId == userId,
            cancellationToken);

    public void Add(Contribution contribution) => dbContext.Contributions.Add(contribution);

    public void Add(RecognitionPoint point) => dbContext.RecognitionPoints.Add(point);

    public Task<bool> ContainsAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        dbContext.ProcessedEvents.AnyAsync(processed => processed.EventId == eventId, cancellationToken);

    public void Add(ProcessedEvent processed) => dbContext.ProcessedEvents.Add(processed);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation(ProcessedEventConfiguration.PrimaryKey))
        {
            throw new DomainRuleException(IntegrationEventHandler.AlreadyProcessed, "The event was processed concurrently.");
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException || exception.IsAnyUniqueViolation())
        {
            // Another event inserted or changed the same contribution, point or read-model row first.
            throw new DomainRuleException(IntegrationEventHandler.ConcurrentUpdate, "Another event updated the same contribution; deliver this event again.");
        }
    }

    public async Task<IReadOnlyList<Contribution>> ListContributionsAsync(
        Guid parishId,
        Guid? seasonId,
        Guid? creditedUserId,
        CancellationToken cancellationToken = default) =>
        await dbContext.Contributions.AsNoTracking()
            .Where(contribution => contribution.ParishId == parishId)
            .Where(contribution => seasonId == null || contribution.SeasonId == seasonId)
            .Where(contribution => creditedUserId == null ||
                                   contribution.ContributedByUserId == creditedUserId || contribution.DonorUserId == creditedUserId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RecognitionPoint>> ListPointsAsync(
        Guid parishId,
        Guid? seasonId,
        Guid? userId,
        CancellationToken cancellationToken = default) =>
        await dbContext.RecognitionPoints.AsNoTracking()
            .Where(point => dbContext.Contributions.Any(contribution =>
                contribution.ContributionId == point.ContributionId && contribution.ParishId == parishId))
            .Where(point => seasonId == null || point.SeasonId == seasonId)
            .Where(point => userId == null || point.AwardedToUserId == userId)
            .ToListAsync(cancellationToken);
}
