namespace TaskExecution.Domain.Support;

public enum SupportResponseStatus { Pending, PartiallyAccepted, Accepted, Rejected, Cancelled }
public enum SupportMemberStatus { Pending, Approved, Rejected, Cancelled, Completed }
public sealed record SupportMemberInput(Guid PositionId,Guid VolunteerId);

public sealed class SupportResponse
{
    private readonly List<SupportResponseMember> members=[];
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

    internal static SupportResponse Staffing(Guid requestId,Guid actor,Guid? communityId,IReadOnlyList<SupportMemberInput> inputs,
        string? note,DateTimeOffset now)
    {
        if(actor==Guid.Empty || communityId==Guid.Empty || inputs.Count==0)
            throw DomainRuleException.Validation("SUPPORT_RESPONSE_INVALID","An actor and at least one named participant are required.");
        if(inputs.Any(member=>member.PositionId==Guid.Empty || member.VolunteerId==Guid.Empty) ||
            inputs.Select(member=>member.VolunteerId).Distinct().Count()!=inputs.Count)
            throw DomainRuleException.Validation("SUPPORT_MEMBER_INVALID","Participants must have valid identities and may appear only once.");
        if(communityId is null && (inputs.Count!=1 || inputs[0].VolunteerId!=actor))
            throw DomainRuleException.Forbidden("SUPPORT_RESPONSE_ACCESS_DENIED","Individual volunteers may only offer themselves.");
        if(note?.Length>2000) throw DomainRuleException.Validation("SUPPORT_NOTE_INVALID","Note must not exceed 2000 characters.");
        var response=new SupportResponse
        {
            Id=Guid.NewGuid(),RequestId=requestId,SubmittedBy=actor,VolunteerId=communityId is null ? actor : null,
            RespondingCommunityId=communityId,ResponseQuantity=inputs.Count,Note=string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedAt=now.ToUniversalTime()
        };
        response.members.AddRange(inputs.Select(member=>SupportResponseMember.Offer(response.Id,requestId,member)));
        return response;
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
    internal static SupportResponseMember Offer(Guid responseId,Guid requestId,SupportMemberInput input)=>new()
    {Id=Guid.NewGuid(),ResponseId=responseId,RequestId=requestId,PositionId=input.PositionId,VolunteerId=input.VolunteerId};
}

public sealed partial class SupportRequest
{
    private readonly List<SupportResponse> responses=[];
    public IReadOnlyCollection<SupportResponse> Responses=>responses;

    public void EnsureAcceptsResponses()
    {
        if(Status is not (SupportRequestStatus.Published or SupportRequestStatus.PartiallyFulfilled))
            throw new DomainRuleException("SUPPORT_REQUEST_INVALID_STATE","Only an open published request accepts responses.");
    }

    public SupportResponse OfferStaffing(Guid actor,Guid? communityId,IReadOnlyList<SupportMemberInput> inputs,string? note,DateTimeOffset now)
    {
        EnsureAcceptsResponses();
        if(SupportType==SupportType.Material)
            throw DomainRuleException.Validation("SUPPORT_TYPE_MISMATCH","This request does not recruit volunteers.");
        if(inputs.Any(member=>!positions.Any(position=>position.Id==member.PositionId)))
            throw DomainRuleException.Validation("SUPPORT_POSITION_MEMBERSHIP_INVALID","Each position must belong to this support request.");
        var active=responses.SelectMany(response=>response.Members)
            .Where(member=>member.Status is SupportMemberStatus.Pending or SupportMemberStatus.Approved or SupportMemberStatus.Completed)
            .Select(member=>member.VolunteerId).ToHashSet();
        if(inputs.Any(member=>active.Contains(member.VolunteerId)))
            throw new DomainRuleException("SUPPORT_MEMBER_DUPLICATE","A volunteer already has an active position in this request.");
        var response=SupportResponse.Staffing(Id,actor,communityId,inputs,note,now);
        responses.Add(response);
        UpdatedAt=now.ToUniversalTime();Version++;
        return response;
    }
}
