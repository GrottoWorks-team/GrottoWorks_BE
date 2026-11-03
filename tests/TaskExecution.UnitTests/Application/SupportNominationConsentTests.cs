using FluentAssertions;
using TaskExecution.Application.Events;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Support;
using TaskExecution.Application.Users;
using TaskExecution.Domain;
using TaskExecution.Domain.Assignments;
using TaskExecution.Domain.Support;

namespace TaskExecution.UnitTests.Application;

public sealed class SupportNominationConsentTests
{
    [Fact]
    public async Task Community_nomination_reserves_capacity_but_does_not_count_as_fulfilled_before_consent()
    {
        var f=new Fixture();var member=await f.Approve();
        member.RequiresConfirmation.Should().BeTrue();member.ParticipationAccepted.Should().BeFalse();
        var assignment=f.Base.Source.Store.Items.Single();assignment.Status.Should().Be(AssignmentStatus.Pending);
        assignment.ApprovedAt.Should().BeNull();assignment.ReservesCapacity.Should().BeTrue();
        f.Base.Request.Status.Should().Be(SupportRequestStatus.Published);
        f.Base.Request.Responses.Single().Status.Should().Be(SupportResponseStatus.Pending);
        f.Base.Source.Events.Items.Last().Payload.GetProperty("requiresConfirmation").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Named_volunteer_responds_through_existing_assignment_api_and_fulfillment_follows_their_decision(bool accept)
    {
        var f=new Fixture();var member=await f.Approve();f.AsVolunteer();
        var result=await f.Base.Source.Service.RespondAsync(member.AssignmentId!.Value,accept,accept ? null : "Unavailable",1);
        result.Status.Should().Be(accept ? AssignmentStatus.Accepted : AssignmentStatus.Declined);
        var saved=f.Base.Request.Responses.Single().Members.Single();
        saved.ParticipationAccepted.Should().Be(accept);
        saved.Status.Should().Be(accept ? SupportMemberStatus.Approved : SupportMemberStatus.Cancelled);
        f.Base.Request.Status.Should().Be(accept ? SupportRequestStatus.Fulfilled : SupportRequestStatus.Published);
        if(!accept) {saved.ParticipationReason.Should().Be("Unavailable");f.Base.Source.Store.Items.Single().ReservesCapacity.Should().BeFalse();}
        f.Base.Source.Events.Items.Last().Payload.GetProperty("assignmentType").GetString().Should().Be("SUPPORT");
    }

    [Fact]
    public async Task Another_volunteer_cannot_accept_the_nomination()
    {
        var f=new Fixture();var member=await f.Approve();f.AsVolunteer();f.Base.Source.User.UserId=Guid.NewGuid();
        Func<Task> accept=()=>f.Base.Source.Service.RespondAsync(member.AssignmentId!.Value,true,null,1);
        await accept.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="TASK_ASSIGNMENT_ACCESS_DENIED");
        f.Base.Request.Status.Should().Be(SupportRequestStatus.Published);
    }

    [Fact]
    public async Task Cancelling_the_task_releases_an_unconfirmed_support_nomination()
    {
        var f=new Fixture();await f.Approve();
        await f.Base.Source.Service.CancelTaskAsync(f.Base.Source.Task.Id,"Event cancelled",f.Base.Source.Task.Version);
        f.Base.Request.Responses.Single().Members.Single().Status.Should().Be(SupportMemberStatus.Cancelled);
        f.Base.Source.Store.Items.Single().Status.Should().Be(AssignmentStatus.Cancelled);
    }

    [Fact]
    public async Task Individual_self_offer_still_creates_an_accepted_assignment()
    {
        var f=new Fixture(false);var member=await f.Approve();
        member.RequiresConfirmation.Should().BeFalse();member.ParticipationAccepted.Should().BeTrue();
        f.Base.Source.Store.Items.Single().Status.Should().Be(AssignmentStatus.Accepted);
        f.Base.Request.Status.Should().Be(SupportRequestStatus.Fulfilled);
    }

    private sealed class Fixture
    {
        public SupportPublicationTests.Fixture Base {get;}=new();
        private readonly Guid memberId;
        private readonly ReviewSupportMemberHandler handler;
        public Fixture(bool community=true)
        {
            var source=Base.Source;var communityId=Guid.NewGuid();
            Base.Request.Publish(1,DateTimeOffset.UtcNow);
            source.Identity.Items[source.VolunteerId]=source.Identity.Items[source.VolunteerId] with {CommunityId=communityId};
            var response=Base.Request.OfferStaffing(community ? Guid.NewGuid() : source.VolunteerId,community ? communityId : null,
                [new(Base.Request.Positions.Single().Id,source.VolunteerId)],null,DateTimeOffset.UtcNow);
            memberId=response.Members.Single().Id;source.Support.Rows.Add(Base.Request);
            handler=new(Base.Store,source.Store,new AssignmentTestFixture.TestTasks(source.Task),
                new TaskScopeAccess(new AssignmentTestFixture.TestScopes(source.Season,source.Area),source.User),new ActiveUserAccess(source.Identity,source.User),
                source.Events,new TaskEventRecorder(source.Events,source.User,new AssignmentTestFixture.TestCorrelation()),new AssignmentTestFixture.TestCorrelation(),TimeProvider.System);
        }
        public Task<SupportMemberDto> Approve()=>handler.HandleAsync(memberId,true,null,1);
        public void AsVolunteer() {Base.Source.User.UserId=Base.Source.VolunteerId;Base.Source.User.Role="VOLUNTEER";}
    }
}
