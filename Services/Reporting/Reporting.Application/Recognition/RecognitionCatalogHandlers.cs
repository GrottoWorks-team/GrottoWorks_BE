using Reporting.Domain;
using Reporting.Domain.Recognition;

namespace Reporting.Application.Recognition;

/// <summary><c>ContributionType</c>.</summary>
public sealed record ContributionTypeDto(Guid Id, string Code, string Name, string? Description, ContributionTypeStatus Status, bool System)
{
    public static ContributionTypeDto From(ContributionType type) =>
        new(type.ContributionTypeId, type.Code, type.Name, type.Description, type.Status, type.IsSystem);
}

/// <summary><c>RecognitionRule</c>: one version of a rule.</summary>
public sealed record RecognitionRuleDto(
    Guid Id,
    Guid ContributionTypeId,
    string ContributionTypeCode,
    string Name,
    int RuleVersion,
    int Points,
    string? Description,
    RecognitionRuleStatus Status,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    long Version)
{
    public static RecognitionRuleDto From(RecognitionRule rule, ContributionType type) =>
        new(rule.RecognitionRuleId, rule.ContributionTypeId, type.Code, rule.Name, rule.RuleVersion, rule.Points, rule.Description,
            rule.Status, rule.EffectiveFrom, rule.EffectiveTo, rule.Version);
}

public sealed record CreateContributionTypeCommand(string? Code, string? Name, string? Description);

public sealed record UpdateContributionTypeCommand(Guid TypeId, string? Name, string? Description, ContributionTypeStatus? Status);

/// <param name="PreviousRuleId">Set to create the next version of that rule (BR-68); name and type are inherited.</param>
public sealed record CreateRecognitionRuleCommand(
    Guid? ContributionTypeId,
    string? Name,
    int Points,
    string? Description,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    Guid? PreviousRuleId);

public sealed record DeactivateRecognitionRuleCommand(Guid RuleId, long? Version, string? Reason);

public interface IRecognitionCatalogStore
{
    Task<IReadOnlyList<ContributionType>> ListTypesAsync(ContributionTypeStatus? status, CancellationToken cancellationToken = default);

    Task<ContributionType?> GetTypeAsync(Guid typeId, CancellationToken cancellationToken = default);

    Task<ContributionType?> GetTypeByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<bool> TypeCodeExistsAsync(string code, CancellationToken cancellationToken = default);

    void AddType(ContributionType type);

    Task<IReadOnlyList<RecognitionRule>> ListRulesAsync(RecognitionRuleStatus? status, Guid? contributionTypeId, CancellationToken cancellationToken = default);

    Task<RecognitionRule?> GetRuleAsync(Guid ruleId, CancellationToken cancellationToken = default);

    Task<bool> RuleNameExistsAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Highest version number of the rule called <paramref name="name"/>.</summary>
    Task<int> LatestRuleVersionAsync(string name, CancellationToken cancellationToken = default);

    void AddRule(RecognitionRule rule);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>The parish calendar date (Vietnam, UTC+7): rule periods are local dates.</summary>
public static class ParishClock
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(7);

    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(now.ToOffset(Offset).DateTime);
}

/// <summary>F-RPT-01 <c>listContributionTypes</c> (ADMIN, PARISH), <c>createContributionType</c>, <c>updateContributionType</c> (ADMIN).</summary>
public sealed class ContributionTypeHandler(IRecognitionCatalogStore store)
{
    public async Task<IReadOnlyList<ContributionTypeDto>> ListAsync(ContributionTypeStatus? status, CancellationToken cancellationToken = default) =>
        (await store.ListTypesAsync(status, cancellationToken)).Select(ContributionTypeDto.From).ToList();

    public async Task<ContributionTypeDto> CreateAsync(CreateContributionTypeCommand command, CancellationToken cancellationToken = default)
    {
        var type = ContributionType.Create(command.Code, command.Name, command.Description);
        if (await store.TypeCodeExistsAsync(type.Code, cancellationToken))
        {
            throw new DomainRuleException("CONTRIBUTION_TYPE_CODE_EXISTS", $"Contribution type '{type.Code}' already exists.");
        }

        store.AddType(type);
        await store.SaveChangesAsync(cancellationToken);
        return ContributionTypeDto.From(type);
    }

    public async Task<ContributionTypeDto> UpdateAsync(UpdateContributionTypeCommand command, CancellationToken cancellationToken = default)
    {
        var type = await store.GetTypeAsync(command.TypeId, cancellationToken)
            ?? throw DomainRuleException.NotFound("CONTRIBUTION_TYPE_NOT_FOUND", "The contribution type does not exist.");
        type.Update(command.Name, command.Description, command.Status);
        await store.SaveChangesAsync(cancellationToken);
        return ContributionTypeDto.From(type);
    }
}

/// <summary>
/// F-RPT-01 <c>listRecognitionRules</c> (ADMIN, PARISH), <c>createRecognitionRule</c> (new rule or next version, BR-68) and
/// <c>deactivateRecognitionRule</c> (ADMIN).
/// </summary>
public sealed class RecognitionRuleHandler(IRecognitionCatalogStore store)
{
    public async Task<IReadOnlyList<RecognitionRuleDto>> ListAsync(
        RecognitionRuleStatus? status,
        Guid? contributionTypeId,
        CancellationToken cancellationToken = default)
    {
        var rules = await store.ListRulesAsync(status, contributionTypeId, cancellationToken);
        var types = (await store.ListTypesAsync(null, cancellationToken)).ToDictionary(type => type.ContributionTypeId);
        return rules
            .OrderBy(rule => rule.Name, StringComparer.CurrentCulture)
            .ThenByDescending(rule => rule.RuleVersion)
            .Select(rule => RecognitionRuleDto.From(rule, types[rule.ContributionTypeId]))
            .ToList();
    }

    public async Task<RecognitionRuleDto> CreateAsync(CreateRecognitionRuleCommand command, CancellationToken cancellationToken = default)
    {
        RecognitionRule rule;
        if (command.PreviousRuleId is { } previousId)
        {
            var previous = await store.GetRuleAsync(previousId, cancellationToken)
                ?? throw DomainRuleException.NotFound("RECOGNITION_RULE_NOT_FOUND", "The previous rule version does not exist.");
            if (command.ContributionTypeId is { } typeId && typeId != previous.ContributionTypeId)
            {
                throw DomainRuleException.Validation("RECOGNITION_RULE_TYPE_MISMATCH", "A new version keeps the contribution type of its rule.");
            }

            if (command.Name is { } name && !string.Equals(name.Trim(), previous.Name, StringComparison.Ordinal))
            {
                throw DomainRuleException.Validation("RECOGNITION_RULE_NAME_MISMATCH", "A new version keeps the name of its rule.");
            }

            if (await store.LatestRuleVersionAsync(previous.Name, cancellationToken) != previous.RuleVersion)
            {
                throw new DomainRuleException("RECOGNITION_RULE_NOT_LATEST", "Only the latest version of a rule gets a new version.");
            }

            rule = previous.NextVersion(command.Points, command.Description, command.EffectiveFrom, command.EffectiveTo);
        }
        else
        {
            var typeId = command.ContributionTypeId
                ?? throw DomainRuleException.Validation("CONTRIBUTION_TYPE_REQUIRED", "contributionTypeId is required for a new rule.");
            var type = await store.GetTypeAsync(typeId, cancellationToken)
                ?? throw DomainRuleException.Validation("CONTRIBUTION_TYPE_NOT_FOUND", "The contribution type does not exist.");
            if (type.Status != ContributionTypeStatus.Active)
            {
                throw DomainRuleException.Validation("CONTRIBUTION_TYPE_INACTIVE", "The contribution type is inactive.");
            }

            rule = RecognitionRule.Create(type.ContributionTypeId, command.Name, command.Points, command.Description, command.EffectiveFrom,
                command.EffectiveTo);
            if (await store.RuleNameExistsAsync(rule.Name, cancellationToken))
            {
                throw new DomainRuleException(
                    "RECOGNITION_RULE_NAME_EXISTS",
                    "A rule with this name exists; send previousRuleId to create its next version (BR-68).");
            }
        }

        store.AddRule(rule);
        await store.SaveChangesAsync(cancellationToken);
        var ruleType = await store.GetTypeAsync(rule.ContributionTypeId, cancellationToken);
        return RecognitionRuleDto.From(rule, ruleType!);
    }

    public async Task<RecognitionRuleDto> DeactivateAsync(DeactivateRecognitionRuleCommand command, CancellationToken cancellationToken = default)
    {
        var rule = await store.GetRuleAsync(command.RuleId, cancellationToken)
            ?? throw DomainRuleException.NotFound("RECOGNITION_RULE_NOT_FOUND", "The rule does not exist.");
        rule.EnsureVersion(command.Version);
        rule.Deactivate(command.Reason, ParishClock.Today(DateTimeOffset.UtcNow));
        await store.SaveChangesAsync(cancellationToken);
        var type = await store.GetTypeAsync(rule.ContributionTypeId, cancellationToken);
        return RecognitionRuleDto.From(rule, type!);
    }
}
