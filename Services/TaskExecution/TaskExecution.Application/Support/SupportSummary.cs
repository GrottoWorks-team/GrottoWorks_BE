using BuildingBlocks.Security;
using TaskExecution.Application.Scopes;
using TaskExecution.Domain;
using TaskExecution.Domain.Support;

namespace TaskExecution.Application.Support;

public sealed record SupportRequestStateTotal(SupportRequestStatus Status, long Count);
public sealed record SupportMaterialTotal(Guid SourceWorkAreaId, Guid MaterialId, decimal OfferedQuantity, decimal ApprovedQuantity,
    decimal ReceivedUsableQuantity, decimal ReceivedUnusableQuantity, long PendingSyncItems);
public sealed record SupportCommunityTotal(Guid? RespondingCommunityId, long Responses, long OfferedMembers, long ApprovedMembers,
    long AwaitingConfirmationMembers, long AcceptedMembers, long CompletedMembers, long RejectedMembers, long CancelledMembers,
    IReadOnlyList<SupportMaterialTotal> Materials);
public sealed record SupportSummaryData(IReadOnlyList<SupportRequestStateTotal> Requests, IReadOnlyList<SupportCommunityTotal> Communities);
public sealed record SupportSummaryDto(Guid SeasonId, long TotalRequests, IReadOnlyList<SupportRequestStateTotal> Requests,
    IReadOnlyList<SupportCommunityTotal> Communities, DateTimeOffset CalculatedAt);

public interface ISupportSummaryStore
{
    Task<SupportSummaryData> ReadAsync(Guid seasonId, Guid parishId, CancellationToken ct);
}
public sealed class GetSupportSummaryHandler(IScopeProjectionStore scopes, ISupportSummaryStore store, ICurrentUser user, TimeProvider clock)
{
    public async Task<SupportSummaryDto> HandleAsync(Guid seasonId, CancellationToken ct = default)
    {
        // BR-57/§11: báo cáo là số liệu của giáo xứ — ban hành giáo (PARISH) và
        // trưởng ban vật tư (MO, cần số liệu hỗ trợ chéo để lập kế hoạch vật tư)
        // đọc được; vẫn chặn chéo giáo xứ bằng `ParishId` bên dưới.
        if (!user.IsAuthenticated ||
            (user.Role != GrottoWorksRoles.Parish && user.Role != GrottoWorksRoles.MaterialOfficer))
            throw DomainRuleException.Forbidden("PARISH_ACCESS_DENIED", "Only a parish coordinator or material officer can read its support summary.");
        var season = await scopes.GetSeasonAsync(seasonId, ct)
            ?? throw DomainRuleException.NotFound("SEASON_NOT_FOUND", "The season does not exist.");
        if (user.ParishId != season.ParishId)
            throw DomainRuleException.Forbidden("PARISH_ACCESS_DENIED", "The season belongs to another parish.");
        var calculatedAt = clock.GetUtcNow();
        var data = await store.ReadAsync(seasonId, season.ParishId, ct);
        return new(seasonId, data.Requests.Sum(row => row.Count), data.Requests, data.Communities, calculatedAt);
    }
}
