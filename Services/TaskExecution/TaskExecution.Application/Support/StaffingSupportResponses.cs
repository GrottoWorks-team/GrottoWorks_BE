using BuildingBlocks.Security;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Users;
using TaskExecution.Domain;
using TaskExecution.Domain.Scopes;
using TaskExecution.Domain.Support;

namespace TaskExecution.Application.Support;

public sealed record SupportMemberDto(Guid Id,Guid PositionId,Guid VolunteerId,SupportMemberStatus Status,Guid? AssignmentId,
    int Version,Guid? ReviewedBy,DateTimeOffset? ReviewedAt,string? Reason,bool RequiresConfirmation=false,bool ParticipationAccepted=false,string? ParticipationReason=null)
{
    public static SupportMemberDto From(SupportResponseMember member)=>new(member.Id,member.PositionId,member.VolunteerId,
        member.Status,member.AssignmentId,member.Version,member.ReviewedBy,member.ReviewedAt,member.ReviewReason,
        member.RequiresConfirmation,member.ParticipationAccepted,member.ParticipationReason);
}
public sealed record SupportResponseDto(Guid Id,Guid RequestId,Guid? VolunteerId,Guid? RespondingCommunityId,SupportResponseStatus Status,
    int ResponseQuantity,string? Note,DateTimeOffset CreatedAt,int Version,IReadOnlyList<SupportMemberDto> Members,IReadOnlyList<SupportResponseMaterialDto> Materials)
{
    public static SupportResponseDto From(SupportResponse response)=>new(response.Id,response.RequestId,response.VolunteerId,
        response.RespondingCommunityId,response.Status,response.ResponseQuantity,response.Note,response.CreatedAt,response.Version,
        response.Members.OrderBy(member=>member.Id).Select(SupportMemberDto.From).ToList(),
        response.Materials.OrderBy(material=>material.Id).Select(SupportResponseMaterialDto.From).ToList());
}

public sealed record SupportResponseMaterialDto(Guid Id,Guid RequestMaterialId,Guid SourceWorkAreaId,Guid MaterialId,decimal OfferedQuantity,
    decimal? ApprovedQuantity,decimal? ReceivedUsableQuantity,decimal? ReceivedUnusableQuantity,SupportMaterialStatus Status,
    Guid? ApprovedBy,DateTimeOffset? ApprovedAt,Guid? ReceivedConfirmedBy,DateTimeOffset? ReceivedConfirmedAt,string? Note,int Version,
    bool PendingSync=false,string? ApprovalErrorCode=null,string? ReviewReason=null)
{
    public static SupportResponseMaterialDto From(SupportResponseMaterial row)=>new(row.Id,row.RequestMaterialId,row.SourceWorkAreaId,row.MaterialId,
        row.OfferedQuantity,row.ApprovedQuantity,row.ReceivedUsableQuantity,row.ReceivedUnusableQuantity,row.Status,row.ApprovedBy,row.ApprovedAt,
        row.ReceivedConfirmedBy,row.ReceivedConfirmedAt,row.Note,row.Version,row.ApprovalPending || row.Status==SupportMaterialStatus.Approved,
        row.ApprovalErrorCode,row.ReviewReason);
}

public sealed class SupportResponseHandler(ISupportRequestRepository requests,TaskScopeAccess scopes,IScopeProjectionStore scopeStore,
    ActiveUserAccess access,IIdentityUserBatchLookup identity,ICurrentUser user,TimeProvider clock,ValidateSupportMaterialOffers materialOffers)
{
    public Task<SupportResponseDto> HandleAsync(Guid requestId,Guid? communityId,IReadOnlyList<SupportMemberInput> members,string? note,
        CancellationToken cancellationToken=default)=>HandleAsync(requestId,communityId,members,[],note,cancellationToken);

    public Task<SupportResponseDto> HandleAsync(Guid requestId,Guid? communityId,IReadOnlyList<SupportMemberInput> members,
        IReadOnlyList<SupportMaterialOfferInput> materials,string? note,
        CancellationToken cancellationToken=default) => requests.InRequestTransactionAsync(requestId,async ()=>
    {
        if(user.Role is not (GrottoWorksRoles.Volunteer or GrottoWorksRoles.MaterialOfficer))
            throw DomainRuleException.Forbidden("SUPPORT_RESPONSE_ACCESS_DENIED","Volunteer or material officer access is required.");
        var request=await requests.GetAsync(requestId,true,cancellationToken)
            ?? throw DomainRuleException.NotFound("SUPPORT_REQUEST_NOT_FOUND","The request does not exist.");
        var area=await scopes.GetWorkAreaForReadAsync(request.WorkAreaId,cancellationToken);
        var season=await scopeStore.GetSeasonAsync(request.SeasonId,cancellationToken)
            ?? throw DomainRuleException.NotFound("SEASON_NOT_FOUND","The owning season is missing.");
        if(season.IsReadOnly) throw new DomainRuleException("SEASON_READ_ONLY","The archived season is read-only.");
        var caller=await access.EnsureCallerAsync(area.ParishId,cancellationToken);
        request.EnsureAcceptsResponses();
        if(members.Count==0 && materials.Count==0) throw DomainRuleException.Validation("SUPPORT_RESPONSE_INVALID","A response must offer people or materials.");
        if(members.Count>0) await ValidateMembersAsync(request,area,caller,communityId,members,cancellationToken);
        else if(communityId.HasValue && (communityId==Guid.Empty || caller.CommunityId!=communityId))
            throw DomainRuleException.Forbidden("SUPPORT_COMMUNITY_ACCESS_DENIED","The responding community does not match the caller.");
        await materialOffers.HandleAsync(request,area,caller,materials,cancellationToken);
        var response=request.OfferResponse(caller.UserId,communityId,members,materials,note,clock.GetUtcNow());
        await requests.SaveAsync(cancellationToken);
        return SupportResponseDto.From(response);
    },cancellationToken);

    private async Task ValidateMembersAsync(SupportRequest request,WorkAreaScope area,IdentityUser caller,Guid? communityId,
        IReadOnlyList<SupportMemberInput> members,CancellationToken cancellationToken)
    {
        if(members.Any(member=>member.PositionId==Guid.Empty || member.VolunteerId==Guid.Empty))
            throw DomainRuleException.Validation("SUPPORT_MEMBER_INVALID","At least one valid named participant is required.");
        if(communityId.HasValue)
        {
            if(communityId==Guid.Empty || caller.CommunityId!=communityId)
                throw DomainRuleException.Forbidden("SUPPORT_COMMUNITY_ACCESS_DENIED","Only members of the responding community may submit its response.");
        }
        else if(user.Role!=GrottoWorksRoles.Volunteer || members.Count!=1 || members[0].VolunteerId!=caller.UserId)
            throw DomainRuleException.Forbidden("SUPPORT_RESPONSE_ACCESS_DENIED","Individual volunteers may only offer themselves.");

        var positions=request.Positions.ToDictionary(position=>position.Id);
        if(members.Any(member=>!positions.ContainsKey(member.PositionId)))
            throw DomainRuleException.Validation("SUPPORT_POSITION_MEMBERSHIP_INVALID","A position belongs to another request.");
        var volunteers=await identity.FindManyAsync(members.Select(member=>member.VolunteerId).Distinct().ToArray(),cancellationToken);
        foreach(var member in members)
        {
            if(!volunteers.TryGetValue(member.VolunteerId,out var volunteer))
                throw DomainRuleException.NotFound("USER_NOT_FOUND","A proposed volunteer does not exist.");
            if(volunteer.Status!="ACTIVE") throw new DomainRuleException("USER_NOT_ACTIVE","A proposed volunteer is not active.");
            if(volunteer.Role!=GrottoWorksRoles.Volunteer) throw new DomainRuleException("USER_ROLE_MISMATCH","Participants must be volunteers.");
            if(volunteer.ParishId!=area.ParishId) throw DomainRuleException.Forbidden("PARISH_ACCESS_DENIED","Participants must belong to the same parish.");
            if(communityId.HasValue && volunteer.CommunityId!=communityId)
                throw DomainRuleException.Forbidden("SUPPORT_COMMUNITY_ACCESS_DENIED","Named members must belong to the responding community.");
            if(positions[member.PositionId].Skills.Any(skill=>!VolunteerSkills.Meets(volunteer,skill.SkillId,skill.RequiredSkillLevel)))
                throw new DomainRuleException("SUPPORT_SKILL_MISMATCH","Every required skill and minimum level must be met; Support has no skill override.");
        }
    }
}

public sealed class ListSupportResponsesHandler(ISupportRequestRepository requests,TaskScopeAccess scopes)
{
    public async Task<IReadOnlyList<SupportResponseDto>> HandleAsync(Guid requestId,CancellationToken cancellationToken=default)
    {
        var request=await requests.GetAsync(requestId,false,cancellationToken)
            ?? throw DomainRuleException.NotFound("SUPPORT_REQUEST_NOT_FOUND","The request does not exist.");
        await scopes.GetWorkAreaForLeaderReadAsync(request.WorkAreaId,cancellationToken);
        return request.Responses.OrderBy(response=>response.CreatedAt).ThenBy(response=>response.Id).Select(SupportResponseDto.From).ToList();
    }
}
