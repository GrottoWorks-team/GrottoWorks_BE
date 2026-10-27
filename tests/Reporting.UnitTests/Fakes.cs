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
