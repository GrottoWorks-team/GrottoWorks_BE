using FluentAssertions;
using TaskExecution.Application.Events;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Support;
using TaskExecution.Application.Users;
using TaskExecution.Contracts.Events;
using TaskExecution.Domain;
using TaskExecution.Domain.Assignments;
using TaskExecution.Domain.Support;
using TaskState=TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.UnitTests.Application;

public sealed class SupportMemberReviewTests
{
    [Fact]
    public async Task Cancelling_the_task_cancels_support_members_and_recomputes_fulfillment_without_deleting_history()
    {
        var f=new Fixture();f.Base.Source.Support.Rows.Add(f.Base.Request);
        var approved=await f.Handler.HandleAsync(f.Member.Id,true,null,1);
        var reviewer=f.Member.ReviewedBy;var reviewedAt=f.Member.ReviewedAt;
        await f.Base.Source.Service.CancelTaskAsync(f.Base.Source.Task.Id,"Cancelled event",f.Base.Source.Task.Version);
        f.Member.Status.Should().Be(SupportMemberStatus.Cancelled);
        f.Member.AssignmentId.Should().Be(approved.AssignmentId);
        f.Member.ReviewedBy.Should().Be(reviewer);f.Member.ReviewedAt.Should().Be(reviewedAt);
        f.Base.Request.Status.Should().Be(SupportRequestStatus.Published);
        f.Base.Request.Responses.Should().ContainSingle().Which.Status.Should().Be(SupportResponseStatus.Cancelled);
        f.Base.Source.Store.Items.Should().ContainSingle().Which.Status.Should().Be(AssignmentStatus.Cancelled);
        var version=f.Base.Request.Version;
        f.Base.Request.CancelAssignments([approved.AssignmentId!.Value],DateTimeOffset.UtcNow).Should().BeFalse();
        f.Base.Request.Version.Should().Be(version);
    }
    [Theory]
    [InlineData(TaskState.Open)]
    [InlineData(TaskState.NotStarted)]
    [InlineData(TaskState.InProgress)]
    public async Task Approval_creates_accepted_support_assignment_without_reopening_the_task(TaskState state)
    {
        var f=new Fixture();
        if(state!=TaskState.Open) f.Base.Source.Task.MarkReady(1,DateTimeOffset.UtcNow);
        if(state==TaskState.InProgress) f.Base.Source.Task.BeginWork(DateTimeOffset.UtcNow);
        var dto=await f.Handler.HandleAsync(f.Member.Id,true,null,1);
        dto.Status.Should().Be(SupportMemberStatus.Approved);dto.Version.Should().Be(2);
        var assignment=f.Base.Source.Store.Items.Should().ContainSingle().Which;
        assignment.Status.Should().Be(AssignmentStatus.Accepted);assignment.Type.Should().Be(AssignmentType.Support);
        dto.AssignmentId.Should().Be(assignment.Id);
        f.Base.Source.Task.Status.Should().Be(state);
        f.Base.Request.Status.Should().Be(SupportRequestStatus.Fulfilled);
        f.Base.Source.Events.Items.Should().Contain(message=>message.EventType==TaskEventTypes.AssignmentCreated &&
            message.Payload.GetProperty("assignmentType").GetString()=="SUPPORT");
        f.Base.Source.Events.Items.Should().Contain(message=>message.EventType==TaskEventTypes.SupportMemberReviewed);
        f.Base.Store.SavedInTransaction.Should().BeTrue();
    }

    [Fact]
    public async Task Rejection_requires_reason_preserves_history_and_creates_no_assignment()
    {
        var f=new Fixture();
        Func<Task> reject=()=>f.Handler.HandleAsync(f.Member.Id,false," ",1);
        await reject.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="SUPPORT_REJECTION_REASON_REQUIRED");
        var dto=await f.Handler.HandleAsync(f.Member.Id,false," unavailable ",1);
        dto.Status.Should().Be(SupportMemberStatus.Rejected);dto.Reason.Should().Be("unavailable");
        f.Base.Source.Store.Items.Should().BeEmpty();f.Base.Request.Responses.Should().ContainSingle();
        f.Base.Source.Events.Items.Should().ContainSingle().Which.EventType.Should().Be(TaskEventTypes.SupportMemberReviewed);
    }

    [Fact]
    public async Task Capacity_in_the_task_is_checked_in_addition_to_position_capacity()
    {
        var f=new Fixture();
        f.Base.Source.Store.Add(TaskAssignment.Assign(f.Base.Source.Task.Id,Guid.NewGuid(),f.Base.Source.User.UserId,null,false,false,null,DateTimeOffset.UtcNow));
        Func<Task> approve=()=>f.Handler.HandleAsync(f.Member.Id,true,null,1);
        await approve.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="TASK_CAPACITY_EXCEEDED");
        f.Member.Status.Should().Be(SupportMemberStatus.Pending);
        f.Base.Source.Events.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Concurrent_reviews_of_the_last_position_do_not_create_two_assignments()
    {
        var f=new Fixture();var other=Guid.NewGuid();
        f.Base.Source.Identity.Items[other]=new(other,"Other","ACTIVE","VOLUNTEER",f.Base.Source.Area.ParishId,null,[]);
        var response=f.Base.Request.OfferStaffing(other,null,[new(f.Member.PositionId,other)],null,DateTimeOffset.UtcNow);
        var attempts=new[] {Approve(f,f.Member.Id),Approve(f,response.Members.Single().Id)};
        var results=await Task.WhenAll(attempts);
        results.Count(success=>success).Should().Be(1);
        f.Base.Source.Store.Items.Should().ContainSingle();
        f.Base.Request.Responses.SelectMany(row=>row.Members).Count(row=>row.Status==SupportMemberStatus.Approved).Should().Be(1);
    }

    [Fact]
    public async Task Locked_volunteer_and_changed_skill_requirements_are_revalidated_at_approval()
    {
        var f=new Fixture();var volunteer=f.Base.Source.VolunteerId;
        f.Base.Source.Identity.Items[volunteer]=f.Base.Source.Identity.Items[volunteer] with {Status="LOCKED"};
        Func<Task> approve=()=>f.Handler.HandleAsync(f.Member.Id,true,null,1);
        await approve.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="USER_NOT_ACTIVE");
        f.Base.Source.Identity.Items[volunteer]=f.Base.Source.Identity.Items[volunteer] with {Status="ACTIVE"};
        f.Base.Source.Task.ReplaceSkills([new(Guid.NewGuid(),"ADVANCED",1)],1,DateTimeOffset.UtcNow);
        await approve.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="SUPPORT_SKILL_MISMATCH");
        f.Member.Status.Should().Be(SupportMemberStatus.Pending);
    }

    [Fact]
    public async Task Stale_member_version_and_unauthorized_caller_are_rejected_before_mutation()
    {
        var f=new Fixture();
        Func<Task> approve=()=>f.Handler.HandleAsync(f.Member.Id,true,null,99);
        await approve.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="SUPPORT_MEMBER_VERSION_MISMATCH");
        f.Base.Source.User.Role="ADMIN";
        approve=()=>f.Handler.HandleAsync(f.Member.Id,true,null,1);
        await approve.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="WORK_AREA_ACCESS_DENIED");
        f.Base.Source.Events.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_member_returns_not_found()
    {
        var f=new Fixture();
        Func<Task> approve=()=>f.Handler.HandleAsync(Guid.NewGuid(),true,null,1);
        await approve.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="SUPPORT_MEMBER_NOT_FOUND");
    }

    private static async Task<bool> Approve(Fixture f,Guid id)
    {
        try {await f.Handler.HandleAsync(id,true,null,1);return true;}
        catch(DomainRuleException error) when(error.Code is "SUPPORT_POSITION_CAPACITY_EXCEEDED" or "TASK_CAPACITY_EXCEEDED") {return false;}
    }
    private sealed class Fixture
    {
        public SupportPublicationTests.Fixture Base {get;}=new();
        public SupportResponseMember Member {get;}
        public ReviewSupportMemberHandler Handler {get;}
        public Fixture()
        {
            Base.Request.Publish(1,DateTimeOffset.UtcNow);
            var response=Base.Request.OfferStaffing(Base.Source.VolunteerId,null,
                [new(Base.Request.Positions.Single().Id,Base.Source.VolunteerId)],null,DateTimeOffset.UtcNow);
            Member=response.Members.Single();var source=Base.Source;
            var scopes=new TaskScopeAccess(new AssignmentTestFixture.TestScopes(source.Season,source.Area),source.User);
            Handler=new(Base.Store,source.Store,new AssignmentTestFixture.TestTasks(source.Task),scopes,
                new ActiveUserAccess(source.Identity,source.User),source.Events,
                new TaskEventRecorder(source.Events,source.User,new AssignmentTestFixture.TestCorrelation()),
                new AssignmentTestFixture.TestCorrelation(),TimeProvider.System);
        }
    }
}
