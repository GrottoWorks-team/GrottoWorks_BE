using Microsoft.EntityFrameworkCore;
using Reporting.Application.Recognition;
using Reporting.Domain;
using Reporting.Domain.Recognition;
using Reporting.Infrastructure.Data;
using Reporting.Infrastructure.Data.Configurations;

namespace Reporting.Infrastructure.Recognition;

public sealed class RecognitionCatalogStore(ReportingDbContext dbContext) : IRecognitionCatalogStore
{
    public async Task<IReadOnlyList<ContributionType>> ListTypesAsync(ContributionTypeStatus? status, CancellationToken cancellationToken = default) =>
        await dbContext.ContributionTypes
            .Where(type => status == null || type.Status == status)
            .OrderBy(type => type.Code)
            .ToListAsync(cancellationToken);

    public Task<ContributionType?> GetTypeAsync(Guid typeId, CancellationToken cancellationToken = default) =>
        dbContext.ContributionTypes.FirstOrDefaultAsync(type => type.ContributionTypeId == typeId, cancellationToken);

    public Task<ContributionType?> GetTypeByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        dbContext.ContributionTypes.FirstOrDefaultAsync(type => type.Code == code, cancellationToken);

    public Task<bool> TypeCodeExistsAsync(string code, CancellationToken cancellationToken = default) =>
        dbContext.ContributionTypes.AnyAsync(type => type.Code == code, cancellationToken);

    public void AddType(ContributionType type) => dbContext.ContributionTypes.Add(type);

    public async Task<IReadOnlyList<RecognitionRule>> ListRulesAsync(
        RecognitionRuleStatus? status,
        Guid? contributionTypeId,
        CancellationToken cancellationToken = default) =>
        await dbContext.RecognitionRules
            .Where(rule => status == null || rule.Status == status)
            .Where(rule => contributionTypeId == null || rule.ContributionTypeId == contributionTypeId)
            .ToListAsync(cancellationToken);

    public Task<RecognitionRule?> GetRuleAsync(Guid ruleId, CancellationToken cancellationToken = default) =>
        dbContext.RecognitionRules.FirstOrDefaultAsync(rule => rule.RecognitionRuleId == ruleId, cancellationToken);

    public Task<bool> RuleNameExistsAsync(string name, CancellationToken cancellationToken = default) =>
        dbContext.RecognitionRules.AnyAsync(rule => rule.Name == name, cancellationToken);

    public async Task<int> LatestRuleVersionAsync(string name, CancellationToken cancellationToken = default) =>
        await dbContext.RecognitionRules.Where(rule => rule.Name == name).MaxAsync(rule => (int?)rule.RuleVersion, cancellationToken) ?? 0;

    public void AddRule(RecognitionRule rule) => dbContext.RecognitionRules.Add(rule);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation(ContributionTypeConfiguration.CodeIndex))
        {
            throw new DomainRuleException("CONTRIBUTION_TYPE_CODE_EXISTS", "This contribution type code already exists.");
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation(RecognitionRuleConfiguration.NameVersionIndex))
        {
            throw new DomainRuleException("RECOGNITION_RULE_CONFLICT", "Another version of this rule was created at the same time. Reload and retry.");
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainRuleException(
                "RECOGNITION_RULE_VERSION_MISMATCH",
                "The rule was changed by someone else. Reload it and try again.",
                DomainErrorKind.PreconditionFailed);
        }
    }
}
