using FluentAssertions;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Support;
using TaskExecution.Application.Users;
using TaskExecution.Domain;
using TaskExecution.Domain.Scopes;
using TaskExecution.Domain.Support;

namespace TaskExecution.UnitTests.Application;

public sealed class StaffingSupportResponseTests
{
    [Fact]
    public async Task Volunteer_offers_self_without_assignment_or_capacity_reservation()
    {
        var fixture=Fixture();
        var dto=await Handler(fixture).HandleAsync(fixture.Request.Id,null,[Member(fixture)]," available ");
        dto.Status.Should().Be(SupportResponseStatus.Pending);
        dto.VolunteerId.Should().Be(fixture.Source.VolunteerId);
        dto.ResponseQuantity.Should().Be(1);dto.Note.Should().Be("available");
        dto.Members.Should().ContainSingle().Which.AssignmentId.Should().BeNull();
        fixture.Source.Store.Items.Should().BeEmpty();
        SupportRequestDto.From(fixture.Request).Responses.Should().ContainSingle().Which.Id.Should().Be(dto.Id);
    }

    [Fact]
    public async Task One_active_position_per_volunteer_is_enforced_across_separate_responses()
    {
        var fixture=Fixture();var handler=Handler(fixture);
        await handler.HandleAsync(fixture.Request.Id,null,[Member(fixture)],null);
        Func<Task> repeat=()=>handler.HandleAsync(fixture.Request.Id,null,[Member(fixture)],null);
        await repeat.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="SUPPORT_MEMBER_DUPLICATE");
        fixture.Request.Responses.Should().ContainSingle();
    }

    [Fact]
    public async Task Individual_response_cannot_name_another_person()
    {
        var fixture=Fixture();
        Func<Task> submit=()=>Handler(fixture).HandleAsync(fixture.Request.Id,null,
            [new(Member(fixture).PositionId,Guid.NewGuid())],null);
        await submit.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="SUPPORT_RESPONSE_ACCESS_DENIED");
    }

    [Fact]
    public async Task Community_response_verifies_every_named_members_current_community()
    {
        var fixture=Fixture();var community=Guid.NewGuid();
        fixture.Source.Identity.Items[fixture.Source.VolunteerId]=fixture.Source.Identity.Items[fixture.Source.VolunteerId] with {CommunityId=community};
        var other=Guid.NewGuid();
        fixture.Source.Identity.Items[other]=new(other,"Other","ACTIVE","VOLUNTEER",fixture.Source.Area.ParishId,Guid.NewGuid(),[]);
        Func<Task> submit=()=>Handler(fixture).HandleAsync(fixture.Request.Id,community,
            [new(Member(fixture).PositionId,other)],null);
        await submit.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="SUPPORT_COMMUNITY_ACCESS_DENIED");
        fixture.Source.Identity.Items[other]=fixture.Source.Identity.Items[other] with {CommunityId=community};
        var response=await Handler(fixture).HandleAsync(fixture.Request.Id,community,[new(Member(fixture).PositionId,other)],null);
        response.VolunteerId.Should().BeNull();response.RespondingCommunityId.Should().Be(community);
        response.Members.Should().ContainSingle().Which.VolunteerId.Should().Be(other);
    }

    [Theory]
    [InlineData("LOCKED","VOLUNTEER","USER_NOT_ACTIVE")]
    [InlineData("ACTIVE","LEADER","USER_ROLE_MISMATCH")]
    public async Task Invalid_named_user_cannot_enter_the_response(string status,string role,string code)
    {
        var fixture=Fixture();var community=Guid.NewGuid();
        fixture.Source.Identity.Items[fixture.Source.VolunteerId]=fixture.Source.Identity.Items[fixture.Source.VolunteerId] with {CommunityId=community};
        var other=Guid.NewGuid();fixture.Source.Identity.Items[other]=new(other,"Other",status,role,fixture.Source.Area.ParishId,community,[]);
        Func<Task> submit=()=>Handler(fixture).HandleAsync(fixture.Request.Id,community,[new(Member(fixture).PositionId,other)],null);
        await submit.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code==code);
    }

    [Fact]
    public async Task Every_required_skill_and_minimum_level_is_checked_without_override()
    {
        var fixture=Fixture();var skill=Guid.NewGuid();
        var request=SupportRequest.Create(fixture.Request.SeasonId,fixture.Request.WorkAreaId,fixture.Request.RequestedBy,SupportType.Staffing,
            null,null,[new(null,"Skilled",1,null,[new(skill,"ADVANCED")],fixture.Source.Task.Id)],[],DateTimeOffset.UtcNow);
        request.Publish(1,DateTimeOffset.UtcNow);fixture.Store.Rows.Add(request);
        var member=new SupportMemberInput(request.Positions.Single().Id,fixture.Source.VolunteerId);
        Func<Task> submit=()=>Handler(fixture).HandleAsync(request.Id,null,[member],null);
        await submit.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="SUPPORT_SKILL_MISMATCH");
        fixture.Source.Identity.Items[fixture.Source.VolunteerId]=fixture.Source.Identity.Items[fixture.Source.VolunteerId] with
        {SkillIds=[skill],Skills=[new(skill,"EXPERT")]};
        (await Handler(fixture).HandleAsync(request.Id,null,[member],null)).Members.Should().ContainSingle();
    }

    [Fact]
    public async Task Archived_season_and_foreign_position_do_not_mutate_the_request()
    {
        var fixture=Fixture();
        Func<Task> submit=()=>Handler(fixture).HandleAsync(fixture.Request.Id,null,[new(Guid.NewGuid(),fixture.Source.VolunteerId)],null);
        await submit.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="SUPPORT_POSITION_MEMBERSHIP_INVALID");
        var season=fixture.Source.Season;
        season.Apply(season.ParishId,season.StartDate,season.EndDate,SeasonScopeStatus.Archived,2,DateTimeOffset.UtcNow);
        submit=()=>Handler(fixture).HandleAsync(fixture.Request.Id,null,[Member(fixture)],null);
        await submit.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="SEASON_READ_ONLY");
        fixture.Request.Responses.Should().BeEmpty();
    }

    [Fact]
    public async Task Only_the_current_assigned_leader_lists_responses()
    {
        var fixture=Fixture();await Handler(fixture).HandleAsync(fixture.Request.Id,null,[Member(fixture)],null);
        var list=new ListSupportResponsesHandler(fixture.Store,Scopes(fixture));
        Func<Task> read=()=>list.HandleAsync(fixture.Request.Id);
        await read.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="WORK_AREA_ACCESS_DENIED");
        fixture.Source.User.UserId=fixture.Source.Area.LeaderUserId!.Value;fixture.Source.User.Role="LEADER";
        (await list.HandleAsync(fixture.Request.Id)).Should().ContainSingle();
    }

    internal static SupportPublicationTests.Fixture Fixture()
    {
        var fixture=new SupportPublicationTests.Fixture();fixture.Request.Publish(1,DateTimeOffset.UtcNow);
        fixture.Source.User.UserId=fixture.Source.VolunteerId;fixture.Source.User.Role="VOLUNTEER";return fixture;
    }
    internal static SupportMemberInput Member(SupportPublicationTests.Fixture fixture)=>new(fixture.Request.Positions.Single().Id,fixture.Source.VolunteerId);
    private static TaskScopeAccess Scopes(SupportPublicationTests.Fixture fixture)=>new(
        new AssignmentTestFixture.TestScopes(fixture.Source.Season,fixture.Source.Area),fixture.Source.User);
    private static StaffingSupportResponseHandler Handler(SupportPublicationTests.Fixture fixture)=>new(fixture.Store,Scopes(fixture),
        new AssignmentTestFixture.TestScopes(fixture.Source.Season,fixture.Source.Area),new ActiveUserAccess(fixture.Source.Identity,fixture.Source.User),
        fixture.Source.Identity,fixture.Source.User,TimeProvider.System);
}
