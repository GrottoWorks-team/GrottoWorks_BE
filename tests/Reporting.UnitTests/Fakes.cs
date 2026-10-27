using BuildingBlocks.Security;
using Reporting.Application.Abstractions;
using Reporting.Application.Recognition;
using Reporting.Domain.Recognition;

namespace Reporting.UnitTests;

public sealed class FakeIntegrationEventPublisher : IIntegrationEventPublisher
{
    public sealed record Published(string EventType, int Version, object Payload, string AggregateType, Guid AggregateId);

    public List<Published> Events { get; } = [];

    public Task PublishAsync<TPayload>(string eventType, int version, TPayload payload, string aggregateType, Guid aggregateId, CancellationToken cancellationToken = default)
        where TPayload : class
    {
        Events.Add(new Published(eventType, version, payload, aggregateType, aggregateId));
        return Task.CompletedTask;
    }
}

public sealed class FakeCurrentUser(Guid userId, string role, Guid? parishId = null) : ICurrentUser
{
    public bool IsAuthenticated => true;

    public Guid UserId { get; } = userId;

    public string Role { get; } = role;

    public Guid? ParishId { get; } = parishId;

    public Guid? CommunityId => null;
}

/// <summary>Starts with the seeded system contribution types, like the migration.</summary>
public sealed class FakeRecognitionCatalogStore : IRecognitionCatalogStore
{
    public List<ContributionType> Types { get; } =
        ContributionType.SystemTypes.Select(type => ContributionType.System(type.Id, type.Code, type.Name, type.Description)).ToList();

    public List<RecognitionRule> Rules { get; } = [];

    public int SaveChangesCount { get; private set; }

    public ContributionType Type(string code) => Types.Single(type => type.Code == code);

    public Task<IReadOnlyList<ContributionType>> ListTypesAsync(ContributionTypeStatus? status, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ContributionType>>(Types.Where(type => status is null || type.Status == status).OrderBy(type => type.Code).ToList());

    public Task<ContributionType?> GetTypeAsync(Guid typeId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Types.FirstOrDefault(type => type.ContributionTypeId == typeId));

    public Task<ContributionType?> GetTypeByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        Task.FromResult(Types.FirstOrDefault(type => type.Code == code));

    public Task<bool> TypeCodeExistsAsync(string code, CancellationToken cancellationToken = default) =>
        Task.FromResult(Types.Any(type => type.Code == code));

    public void AddType(ContributionType type) => Types.Add(type);

    public Task<IReadOnlyList<RecognitionRule>> ListRulesAsync(RecognitionRuleStatus? status, Guid? contributionTypeId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RecognitionRule>>(Rules
            .Where(rule => status is null || rule.Status == status)
            .Where(rule => contributionTypeId is null || rule.ContributionTypeId == contributionTypeId)
            .ToList());

    public Task<RecognitionRule?> GetRuleAsync(Guid ruleId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rules.FirstOrDefault(rule => rule.RecognitionRuleId == ruleId));

    public Task<bool> RuleNameExistsAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rules.Any(rule => rule.Name == name));

    public Task<int> LatestRuleVersionAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rules.Where(rule => rule.Name == name).Select(rule => rule.RuleVersion).DefaultIfEmpty(0).Max());

    public void AddRule(RecognitionRule rule) => Rules.Add(rule);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCount++;
        return Task.CompletedTask;
    }
}

public sealed class FakeScopeStore : Reporting.Application.Scope.IWorkAreaScopeStore
{
    public List<Reporting.Domain.Scope.WorkAreaScope> Scopes { get; } = [];

    public Task<Reporting.Domain.Scope.WorkAreaScope?> GetAsync(Guid workAreaId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Scopes.FirstOrDefault(s => s.WorkAreaId == workAreaId));

    public Task<IReadOnlyList<Reporting.Domain.Scope.WorkAreaScope>> ListBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Reporting.Domain.Scope.WorkAreaScope>>(Scopes.Where(s => s.SeasonId == seasonId).ToList());

    public Task<IReadOnlyList<Reporting.Domain.Scope.WorkAreaScope>> ListByIdsAsync(IReadOnlyCollection<Guid> workAreaIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Reporting.Domain.Scope.WorkAreaScope>>(Scopes.Where(s => workAreaIds.Contains(s.WorkAreaId)).ToList());

    public Task<IReadOnlyList<Reporting.Domain.Scope.WorkAreaScope>> ListByParishAsync(Guid parishId, Guid? seasonId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Reporting.Domain.Scope.WorkAreaScope>>(
            Scopes.Where(s => s.ParishId == parishId && (seasonId is null || s.SeasonId == seasonId)).OrderBy(s => s.Name).ToList());

    public void Add(Reporting.Domain.Scope.WorkAreaScope scope) => Scopes.Add(scope);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Reporting.Domain.Scope.WorkAreaScope Seed(Guid parishId, Guid seasonId, Guid? leaderId = null, Guid? officerId = null, string name = "Hang đá")
    {
        var scope = Reporting.Domain.Scope.WorkAreaScope.FromSnapshot(
            new Reporting.Domain.Scope.WorkAreaSnapshot(Guid.NewGuid(), seasonId, parishId, name, Reporting.Domain.Scope.WorkAreaStatus.InProgress,
                Reporting.Domain.Scope.SeasonStatus.Active, null, leaderId, officerId, 1),
            DateTimeOffset.UtcNow);
        Scopes.Add(scope);
        return scope;
    }
}

/// <summary>Contributions, points and inbox in one fake unit of work; staged rows commit on SaveChanges.</summary>
public sealed class FakeContributionStore :
    Reporting.Application.Recognition.IContributionStore,
    Reporting.Application.Ingestion.IInboxStore,
    Reporting.Application.Recognition.IRecognitionQueryStore
{
    private readonly List<object> _staged = [];

    public List<Contribution> Contributions { get; } = [];

    public List<RecognitionPoint> Points { get; } = [];

    public List<Reporting.Domain.Inbox.ProcessedEvent> Processed { get; } = [];

    public Task<Contribution?> GetByDonationAsync(Guid donationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Contributions.FirstOrDefault(c => c.DonationId == donationId));

    public Task<Contribution?> GetByReferenceAsync(string referenceType, Guid referenceId, Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Contributions.FirstOrDefault(c => c.DonationId == null && c.ReferenceType == referenceType && c.ReferenceId == referenceId &&
                                                          c.ContributedByUserId == userId));

    public Task<bool> PointExistsAsync(Guid contributionId, Guid ruleId, Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Points.Any(p => p.ContributionId == contributionId && p.RecognitionRuleId == ruleId && p.AwardedToUserId == userId));

    public void Add(Contribution contribution) => _staged.Add(contribution);

    public void Add(RecognitionPoint point) => _staged.Add(point);

    public Task<bool> ContainsAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Processed.Any(p => p.EventId == eventId));

    public void Add(Reporting.Domain.Inbox.ProcessedEvent processed) => _staged.Add(processed);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var row in _staged)
        {
            switch (row)
            {
                case Contribution contribution: Contributions.Add(contribution); break;
                case RecognitionPoint point: Points.Add(point); break;
                case Reporting.Domain.Inbox.ProcessedEvent processed: Processed.Add(processed); break;
            }
        }

        _staged.Clear();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Contribution>> ListContributionsAsync(Guid parishId, Guid? seasonId, Guid? creditedUserId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Contribution>>(Contributions
            .Where(c => c.ParishId == parishId && (seasonId is null || c.SeasonId == seasonId))
            .Where(c => creditedUserId is null || c.ContributedByUserId == creditedUserId || c.DonorUserId == creditedUserId)
            .ToList());

    public Task<IReadOnlyList<RecognitionPoint>> ListPointsAsync(Guid parishId, Guid? seasonId, Guid? userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RecognitionPoint>>(Points
            .Where(p => Contributions.Any(c => c.ContributionId == p.ContributionId && c.ParishId == parishId))
            .Where(p => seasonId is null || p.SeasonId == seasonId)
            .Where(p => userId is null || p.AwardedToUserId == userId)
            .ToList());
}
