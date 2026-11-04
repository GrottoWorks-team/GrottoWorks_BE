using FluentAssertions;
using TaskExecution.Application.Support;
using TaskExecution.Domain;
using TaskExecution.Domain.Support;

namespace TaskExecution.UnitTests.Application;

public sealed class SupportSummaryTests
{
    [Fact]
    public async Task Parish_reads_only_its_season_with_recorded_community_and_separate_staffing_material_totals()
    {
        var f=new AssignmentTestFixture(); f.User.Role="PARISH"; var store=new Store();
        var community=Guid.NewGuid(); var material=Guid.NewGuid();
        store.Data=new([new(SupportRequestStatus.Published,2),new(SupportRequestStatus.Closed,1)],
            [new(community,2,5,4,1,3,1,1,0,[new(Guid.NewGuid(),material,100,80,60,20,1)])]);
        var handler=new GetSupportSummaryHandler(new AssignmentTestFixture.TestScopes(f.Season,f.Area),store,f.User,TimeProvider.System);
        var dto=await handler.HandleAsync(f.Season.SeasonId);
        store.ParishId.Should().Be(f.Season.ParishId); dto.TotalRequests.Should().Be(3);
        var result=dto.Communities.Single(); result.RespondingCommunityId.Should().Be(community);
        result.OfferedMembers.Should().Be(5); result.AcceptedMembers.Should().Be(3); result.AwaitingConfirmationMembers.Should().Be(1);
        result.Materials.Single().ReceivedUsableQuantity.Should().Be(60); result.Materials.Single().ReceivedUnusableQuantity.Should().Be(20);
        dto.CalculatedAt.Should().NotBe(default);
    }
    [Fact]
    public async Task Empty_season_returns_real_zero_counts_and_no_invented_community()
    {
        var f=new AssignmentTestFixture(); f.User.Role="PARISH";
        var dto=await new GetSupportSummaryHandler(new AssignmentTestFixture.TestScopes(f.Season,f.Area),new Store(),f.User,TimeProvider.System)
            .HandleAsync(f.Season.SeasonId);
        dto.TotalRequests.Should().Be(0); dto.Communities.Should().BeEmpty();
    }
    [Theory]
    [InlineData("ADMIN")][InlineData("LEADER")][InlineData("VOLUNTEER")]
    public async Task Other_roles_do_not_bypass_scope_or_query_the_store(string role)
    {
        var f=new AssignmentTestFixture(); f.User.Role=role; var store=new Store();
        Func<Task> query=()=>new GetSupportSummaryHandler(new AssignmentTestFixture.TestScopes(f.Season,f.Area),store,f.User,TimeProvider.System).HandleAsync(f.Season.SeasonId);
        await query.Should().ThrowAsync<DomainRuleException>().Where(e=>e.Code=="PARISH_ACCESS_DENIED"); store.Reads.Should().Be(0);
    }
    [Fact]
    public async Task Missing_season_and_other_parish_are_rejected_before_aggregating()
    {
        var f=new AssignmentTestFixture(); f.User.Role="PARISH"; var store=new Store();
        var handler=new GetSupportSummaryHandler(new AssignmentTestFixture.TestScopes(f.Season,f.Area),store,f.User,TimeProvider.System);
        Func<Task> query=()=>handler.HandleAsync(Guid.NewGuid());
        await query.Should().ThrowAsync<DomainRuleException>().Where(e=>e.Code=="SEASON_NOT_FOUND");
        f.User.ParishId=Guid.NewGuid(); query=()=>handler.HandleAsync(f.Season.SeasonId);
        await query.Should().ThrowAsync<DomainRuleException>().Where(e=>e.Code=="PARISH_ACCESS_DENIED"); store.Reads.Should().Be(0);
    }
    private sealed class Store : ISupportSummaryStore
    {
        public SupportSummaryData Data=new([],[]); public Guid ParishId; public int Reads;
        public Task<SupportSummaryData> ReadAsync(Guid seasonId,Guid parishId,CancellationToken ct)
        {ParishId=parishId;Reads++;return Task.FromResult(Data);}
    }
}
