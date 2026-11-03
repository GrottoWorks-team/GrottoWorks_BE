using System.Data;
using Microsoft.EntityFrameworkCore;
using TaskExecution.Application.Support;
using TaskExecution.Domain;
using TaskExecution.Domain.Support;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.Infrastructure.Support;

public sealed class SupportSummaryStore(TaskExecutionDbContext db) : ISupportSummaryStore
{
    public async Task<SupportSummaryData> ReadAsync(Guid seasonId, Guid parishId, CancellationToken ct)
    {
        // A bounded number of grouped SQL queries, sharing one consistent snapshot; never load the response graph.
        await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
        var requests=db.SupportRequests.AsNoTracking().Where(request=>request.SeasonId==seasonId);
        if (await requests.AnyAsync(request=>!db.WorkAreaScopes.Any(area=>area.WorkAreaId==request.WorkAreaId && area.ParishId==parishId),ct))
            throw new DomainRuleException("SUPPORT_SCOPE_UNAVAILABLE","Some request ownership cannot be verified.",DomainErrorKind.ServiceUnavailable);
        var states=await requests.GroupBy(request=>request.Status)
            .Select(group=>new SupportRequestStateTotal(group.Key,group.LongCount())).ToListAsync(ct);
        var responses=db.Set<SupportResponse>().AsNoTracking().Where(response=>requests.Any(request=>request.Id==response.RequestId));
        var responseCounts=await responses.GroupBy(response=>response.RespondingCommunityId)
            .Select(group=>new {CommunityId=group.Key,Count=group.LongCount()}).ToListAsync(ct);
        var members=await (from member in db.Set<SupportResponseMember>().AsNoTracking()
            join response in responses on member.ResponseId equals response.Id
            group member by response.RespondingCommunityId into participants
            select new
            {
                CommunityId=participants.Key,Offered=participants.LongCount(),
                Approved=participants.LongCount(member=>member.Status==SupportMemberStatus.Approved || member.Status==SupportMemberStatus.Completed),
                Awaiting=participants.LongCount(member=>member.Status==SupportMemberStatus.Approved && member.RequiresConfirmation && !member.ParticipationAccepted),
                Accepted=participants.LongCount(member=>(member.Status==SupportMemberStatus.Approved && member.ParticipationAccepted) || member.Status==SupportMemberStatus.Completed),
                Completed=participants.LongCount(member=>member.Status==SupportMemberStatus.Completed),
                Rejected=participants.LongCount(member=>member.Status==SupportMemberStatus.Rejected),
                Cancelled=participants.LongCount(member=>member.Status==SupportMemberStatus.Cancelled)
            }).ToListAsync(ct);
        var materials=await (from item in db.Set<SupportResponseMaterial>().AsNoTracking()
            join response in responses on item.ResponseId equals response.Id
            group item by new {response.RespondingCommunityId,item.SourceWorkAreaId,item.MaterialId} into supplies
            select new
            {
                CommunityId=supplies.Key.RespondingCommunityId,supplies.Key.SourceWorkAreaId,supplies.Key.MaterialId,
                Offered=supplies.Sum(item=>item.OfferedQuantity),Approved=supplies.Sum(item=>item.ApprovedQuantity ?? 0),
                Usable=supplies.Sum(item=>item.Status==SupportMaterialStatus.Transferred ? item.ReceivedUsableQuantity ?? 0 : 0),
                Unusable=supplies.Sum(item=>item.Status==SupportMaterialStatus.Transferred ? item.ReceivedUnusableQuantity ?? 0 : 0),
                Pending=supplies.LongCount(item=>item.Status==SupportMaterialStatus.Approved ||
                    (item.Status==SupportMaterialStatus.Pending && item.ApprovalOperationId!=null && item.ApprovalErrorCode==null))
            }).ToListAsync(ct);
        var memberLookup=members.ToDictionary(row=>row.CommunityId ?? Guid.Empty);
        var materialLookup=materials.GroupBy(row=>row.CommunityId ?? Guid.Empty).ToDictionary(group=>group.Key,
            group=>(IReadOnlyList<SupportMaterialTotal>)group.OrderBy(row=>row.SourceWorkAreaId).ThenBy(row=>row.MaterialId)
                .Select(row=>new SupportMaterialTotal(row.SourceWorkAreaId,row.MaterialId,row.Offered,row.Approved,row.Usable,row.Unusable,row.Pending)).ToList());
        var communities=responseCounts.OrderBy(row=>row.CommunityId).Select(row=>
        {
            memberLookup.TryGetValue(row.CommunityId ?? Guid.Empty,out var member);
            return new SupportCommunityTotal(row.CommunityId,row.Count,member?.Offered ?? 0,member?.Approved ?? 0,member?.Awaiting ?? 0,
                member?.Accepted ?? 0,member?.Completed ?? 0,member?.Rejected ?? 0,member?.Cancelled ?? 0,
                materialLookup.GetValueOrDefault(row.CommunityId ?? Guid.Empty) ?? []);
        }).ToList();
        await transaction.CommitAsync(ct);
        return new(states.OrderBy(row=>row.Status).ToList(),communities);
    }
}
