namespace TaskExecution.Domain.Support;

public enum SupportResponseStatus { Pending, PartiallyAccepted, Accepted, Rejected, Cancelled }
public enum SupportMemberStatus { Pending, Approved, Rejected, Cancelled, Completed }
public sealed record SupportMemberInput(Guid PositionId,Guid VolunteerId);

public sealed class SupportResponse
{
    private readonly List<SupportResponseMember> members=[];
    private readonly List<SupportResponseMaterial> materials=[];
    private SupportResponse() { }
    public Guid Id {get;private set;}
    public Guid RequestId {get;private set;}
    public Guid SubmittedBy {get;private set;}
    public Guid? VolunteerId {get;private set;}
    public Guid? RespondingCommunityId {get;private set;}
    public SupportResponseStatus Status {get;private set;}=SupportResponseStatus.Pending;
    public int ResponseQuantity {get;private set;}
    public string? Note {get;private set;}
    public DateTimeOffset CreatedAt {get;private set;}
    public int Version {get;private set;}=1;
    public IReadOnlyCollection<SupportResponseMember> Members=>members;
    public IReadOnlyCollection<SupportResponseMaterial> Materials=>materials;

    internal static SupportResponse Create(Guid requestId,Guid actor,Guid? communityId,IReadOnlyList<SupportMemberInput> inputs,
        IReadOnlyList<SupportMaterialOfferInput> offers,IReadOnlyDictionary<Guid,SupportMaterial> demands,string? note,DateTimeOffset now)
    {
        if(actor==Guid.Empty || communityId==Guid.Empty || (inputs.Count==0 && offers.Count==0))
            throw DomainRuleException.Validation("SUPPORT_RESPONSE_INVALID","An actor and at least one participant or material offer are required.");
        if(inputs.Any(member=>member.PositionId==Guid.Empty || member.VolunteerId==Guid.Empty) ||
            inputs.Select(member=>member.VolunteerId).Distinct().Count()!=inputs.Count)
            throw DomainRuleException.Validation("SUPPORT_MEMBER_INVALID","Participants must have valid identities and may appear only once.");
        if(inputs.Count>0 && communityId is null && (inputs.Count!=1 || inputs[0].VolunteerId!=actor))
            throw DomainRuleException.Forbidden("SUPPORT_RESPONSE_ACCESS_DENIED","Individual volunteers may only offer themselves.");
        if(note?.Length>2000) throw DomainRuleException.Validation("SUPPORT_NOTE_INVALID","Note must not exceed 2000 characters.");
        var response=new SupportResponse
        {
            Id=Guid.NewGuid(),RequestId=requestId,SubmittedBy=actor,VolunteerId=communityId is null && inputs.Count>0 ? actor : null,
            RespondingCommunityId=communityId,ResponseQuantity=inputs.Count,Note=string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedAt=now.ToUniversalTime()
        };
        response.members.AddRange(inputs.Select(member=>SupportResponseMember.Offer(response.Id,requestId,member)));
        response.materials.AddRange(offers.Select(offer=>SupportResponseMaterial.Offer(response.Id,requestId,demands[offer.RequestMaterialId].MaterialId,offer)));
        return response;
    }

    internal void RefreshMemberStatus()
    {
        var accepted=members.Count(member=>(member.Status==SupportMemberStatus.Approved && member.ParticipationAccepted) || member.Status==SupportMemberStatus.Completed)
            + materials.Count(material=>material.Status is SupportMaterialStatus.Approved or SupportMaterialStatus.Transferred);
        var total=members.Count+materials.Count;
        Status=accepted==total ? SupportResponseStatus.Accepted
            : accepted>0 ? SupportResponseStatus.PartiallyAccepted
            : members.All(member=>member.Status==SupportMemberStatus.Cancelled) && materials.All(material=>material.Status==SupportMaterialStatus.Cancelled) ? SupportResponseStatus.Cancelled
            : members.All(member=>member.Status is SupportMemberStatus.Rejected or SupportMemberStatus.Cancelled) &&
              materials.All(material=>material.Status is SupportMaterialStatus.Rejected or SupportMaterialStatus.Cancelled) ? SupportResponseStatus.Rejected
            : SupportResponseStatus.Pending;
        Version++;
    }
}

public sealed class SupportResponseMember
{
    private SupportResponseMember() { }
    public Guid Id {get;private set;}
    public Guid ResponseId {get;private set;}
    public Guid RequestId {get;private set;}
    public Guid PositionId {get;private set;}
    public Guid VolunteerId {get;private set;}
    public SupportMemberStatus Status {get;private set;}=SupportMemberStatus.Pending;
    public Guid? AssignmentId {get;private set;}
    public int Version {get;private set;}=1;
    public Guid? ReviewedBy {get;private set;}
    public DateTimeOffset? ReviewedAt {get;private set;}
    public string? ReviewReason {get;private set;}
    public bool RequiresConfirmation {get;private set;}
    public bool ParticipationAccepted {get;private set;}
    public string? ParticipationReason {get;private set;}
    internal static SupportResponseMember Offer(Guid responseId,Guid requestId,SupportMemberInput input)=>new()
    {Id=Guid.NewGuid(),ResponseId=responseId,RequestId=requestId,PositionId=input.PositionId,VolunteerId=input.VolunteerId};

    public void EnsureReviewAllowed(int? version,bool approve,string? reason)
    {
        if(version is <=0) throw DomainRuleException.Validation("VALIDATION_FAILED","Version must be positive.");
        if(version.HasValue && version!=Version)
            throw new DomainRuleException("SUPPORT_MEMBER_VERSION_MISMATCH","The member changed.",DomainErrorKind.PreconditionFailed);
        if(Status!=SupportMemberStatus.Pending)
            throw new DomainRuleException("SUPPORT_MEMBER_INVALID_STATE","Only a pending member can be reviewed.");
        if(!approve && (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length>500))
            throw DomainRuleException.Validation("SUPPORT_REJECTION_REASON_REQUIRED","Rejection requires a reason of 1–500 characters.");
    }

    internal bool CancelAssignment(IReadOnlySet<Guid> assignmentIds)
    {
        if(Status!=SupportMemberStatus.Approved || !AssignmentId.HasValue || !assignmentIds.Contains(AssignmentId.Value)) return false;
        Status=SupportMemberStatus.Cancelled;Version++;
        return true;
    }

    internal void Review(bool approve,Guid? assignmentId,Guid reviewer,string? reason,int? expectedVersion,DateTimeOffset now,bool requiresConfirmation)
    {
        EnsureReviewAllowed(expectedVersion,approve,reason);
        if(reviewer==Guid.Empty || (approve && (!assignmentId.HasValue || assignmentId==Guid.Empty)))
            throw DomainRuleException.Validation("SUPPORT_MEMBER_REVIEW_INVALID","Approval requires a persisted assignment identity and reviewer.");
        AssignmentId=approve ? assignmentId : null;
        Status=approve ? SupportMemberStatus.Approved : SupportMemberStatus.Rejected;
        ReviewedBy=reviewer;ReviewedAt=now.ToUniversalTime();ReviewReason=approve ? null : reason!.Trim();Version++;
        RequiresConfirmation=approve && requiresConfirmation;
        ParticipationAccepted=approve && !requiresConfirmation;
    }

    internal void RespondToNomination(bool accept,string? reason)
    {
        if(Status!=SupportMemberStatus.Approved || !RequiresConfirmation || ParticipationAccepted)
            throw new DomainRuleException("SUPPORT_MEMBER_INVALID_STATE","Only a nomination awaiting its volunteer can be answered.");
        ParticipationAccepted=accept;
        if(!accept) {Status=SupportMemberStatus.Cancelled;ParticipationReason=reason?.Trim();}
        Version++;
    }
}

public sealed partial class SupportRequest
{
    public void EnsureMemberReviewAllowed(SupportResponseMember member,bool approve,string? reason,int? version)
    {
        if(Status is not (SupportRequestStatus.Published or SupportRequestStatus.PartiallyFulfilled or SupportRequestStatus.Fulfilled))
            throw new DomainRuleException("SUPPORT_REQUEST_INVALID_STATE","The support request is not open for review.");
        if(member.RequestId!=Id) throw DomainRuleException.Validation("SUPPORT_MEMBER_MEMBERSHIP_INVALID","The member belongs to another request.");
        member.EnsureReviewAllowed(version,approve,reason);
        var position=positions.Single(position=>position.Id==member.PositionId);
        var accepted=responses.SelectMany(response=>response.Members).Count(row=>row.PositionId==position.Id &&
            row.Status is SupportMemberStatus.Approved or SupportMemberStatus.Completed);
        if(approve && accepted>=position.RequiredVolunteerCount)
            throw new DomainRuleException("SUPPORT_POSITION_CAPACITY_EXCEEDED","The position has no remaining slots.");
    }

    public void ReviewMember(Guid memberId,bool approve,Guid? assignmentId,Guid reviewer,string? reason,int? expectedVersion,DateTimeOffset now,bool requiresConfirmation=false)
    {
        var response=responses.SingleOrDefault(response=>response.Members.Any(member=>member.Id==memberId))
            ?? throw DomainRuleException.NotFound("SUPPORT_MEMBER_NOT_FOUND","The member does not exist.");
        var member=response.Members.Single(member=>member.Id==memberId);
        EnsureMemberReviewAllowed(member,approve,reason,expectedVersion);
        member.Review(approve,assignmentId,reviewer,reason,expectedVersion,now,requiresConfirmation);
        response.RefreshMemberStatus();
        RefreshStaffingFulfillment();
        UpdatedAt=now.ToUniversalTime();Version++;
    }

    public bool CancelAssignments(IReadOnlyCollection<Guid> assignmentIds,DateTimeOffset now)
    {
        var ids=assignmentIds.ToHashSet();var changed=false;
        foreach(var response in responses)
        {
            var responseChanged=false;
            foreach(var member in response.Members) responseChanged|=member.CancelAssignment(ids);
            if(!responseChanged) continue;
            response.RefreshMemberStatus();changed=true;
        }
        if(!changed) return false;
        if(Status is not (SupportRequestStatus.Closed or SupportRequestStatus.Cancelled)) RefreshStaffingFulfillment();
        UpdatedAt=now.ToUniversalTime();Version++;
        return true;
    }

    private void RefreshStaffingFulfillment()
    {
        var accepted=responses.SelectMany(row=>row.Members)
            .Where(row=>(row.Status==SupportMemberStatus.Approved && row.ParticipationAccepted) || row.Status==SupportMemberStatus.Completed).ToList();
        var staffingSatisfied=positions.Count>0 && positions.All(position=>accepted.Count(row=>row.PositionId==position.Id)>=position.RequiredVolunteerCount);
        var receipts=responses.SelectMany(row=>row.Materials).Where(row=>row.Status==SupportMaterialStatus.Transferred).ToList();
        var materialSatisfied=materials.Count>0 && materials.All(demand=>receipts.Where(row=>row.RequestMaterialId==demand.Id)
            .Sum(row=>row.ReceivedUsableQuantity ?? 0)>=demand.RequiredQuantity);
        var fulfilled=(SupportType==SupportType.Material || staffingSatisfied) && (SupportType==SupportType.Staffing || materialSatisfied);
        Status=fulfilled ? SupportRequestStatus.Fulfilled
            : accepted.Count>0 || receipts.Any(row=>row.ReceivedUsableQuantity>0) ? SupportRequestStatus.PartiallyFulfilled : SupportRequestStatus.Published;
    }

    public void RespondToNomination(Guid assignmentId,bool accept,string? reason,DateTimeOffset now)
    {
        var response=responses.SingleOrDefault(row=>row.Members.Any(member=>member.AssignmentId==assignmentId))
            ?? throw DomainRuleException.NotFound("SUPPORT_MEMBER_NOT_FOUND","The assignment has no support member.");
        if(Status is SupportRequestStatus.Closed or SupportRequestStatus.Cancelled)
            throw new DomainRuleException("SUPPORT_REQUEST_INVALID_STATE","The support request is closed.");
        var member=response.Members.Single(row=>row.AssignmentId==assignmentId);
        member.RespondToNomination(accept,reason);response.RefreshMemberStatus();RefreshStaffingFulfillment();
        UpdatedAt=now.ToUniversalTime();Version++;
    }

    private readonly List<SupportResponse> responses=[];
    public IReadOnlyCollection<SupportResponse> Responses=>responses;

    public void EnsureAcceptsResponses()
    {
        if(Status is not (SupportRequestStatus.Published or SupportRequestStatus.PartiallyFulfilled))
            throw new DomainRuleException("SUPPORT_REQUEST_INVALID_STATE","Only an open published request accepts responses.");
    }

    public SupportResponse OfferStaffing(Guid actor,Guid? communityId,IReadOnlyList<SupportMemberInput> inputs,string? note,DateTimeOffset now)
        =>OfferResponse(actor,communityId,inputs,[],note,now);

    public void ValidateMaterialOffers(IReadOnlyList<SupportMaterialOfferInput> offers)
    {
        if(offers.Count>0 && SupportType==SupportType.Staffing)
            throw DomainRuleException.Validation("SUPPORT_TYPE_MISMATCH","This request does not accept material offers.");
        if(offers.Any(offer=>!materials.Any(demand=>demand.Id==offer.RequestMaterialId)))
            throw DomainRuleException.Validation("SUPPORT_MATERIAL_MEMBERSHIP_INVALID","A material demand belongs to another request.");
        if(offers.Any(offer=>offer.SourceWorkAreaId==WorkAreaId))
            throw DomainRuleException.Validation("SUPPORT_TRANSFER_SAME_WORK_AREA","Source and target must be different work areas.");
        if(offers.Select(offer=>(offer.RequestMaterialId,offer.SourceWorkAreaId)).Distinct().Count()!=offers.Count)
            throw DomainRuleException.Validation("SUPPORT_MATERIAL_DUPLICATE","A material demand/source pair may only appear once.");
        foreach(var offer in offers) SupportResponseMaterial.ValidateOffer(offer);
    }

    public SupportResponse OfferResponse(Guid actor,Guid? communityId,IReadOnlyList<SupportMemberInput> inputs,
        IReadOnlyList<SupportMaterialOfferInput> offers,string? note,DateTimeOffset now)
    {
        EnsureAcceptsResponses();
        if(inputs.Count>0 && SupportType==SupportType.Material)
            throw DomainRuleException.Validation("SUPPORT_TYPE_MISMATCH","This request does not recruit volunteers.");
        if(inputs.Any(member=>!positions.Any(position=>position.Id==member.PositionId)))
            throw DomainRuleException.Validation("SUPPORT_POSITION_MEMBERSHIP_INVALID","Each position must belong to this support request.");
        var active=responses.SelectMany(response=>response.Members)
            .Where(member=>member.Status is SupportMemberStatus.Pending or SupportMemberStatus.Approved or SupportMemberStatus.Completed)
            .Select(member=>member.VolunteerId).ToHashSet();
        if(inputs.Any(member=>active.Contains(member.VolunteerId)))
            throw new DomainRuleException("SUPPORT_MEMBER_DUPLICATE","A volunteer already has an active position in this request.");
        ValidateMaterialOffers(offers);
        var response=SupportResponse.Create(Id,actor,communityId,inputs,offers,materials.ToDictionary(demand=>demand.Id),note,now);
        responses.Add(response);
        UpdatedAt=now.ToUniversalTime();Version++;
        return response;
    }
}
