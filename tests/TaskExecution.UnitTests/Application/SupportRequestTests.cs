using BuildingBlocks.Security;
using FluentAssertions;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Support;
using TaskExecution.Application.Users;
using TaskExecution.Domain;
using TaskExecution.Domain.Scopes;
using TaskExecution.Domain.Support;

namespace TaskExecution.UnitTests.Application;

public sealed class SupportRequestTests
{
    [Fact]
    public async Task Leader_creates_planning_request_for_owned_work_area_and_get_returns_empty_responses()
    {
        var fixture = new Fixture();
        var result = await fixture.Service.CreateAsync(new(fixture.Season.SeasonId, fixture.Area.WorkAreaId,
            SupportType.Mixed, " help ", new DateOnly(2026,12,20),
            [new(null," Electricians ",2,null,[new(Guid.NewGuid(),null)])],
            [new(null,Guid.NewGuid(),3.5m," cable ")]));

        result.Status.Should().Be(SupportRequestStatus.Planning);
        result.Description.Should().Be("help");
        result.Positions.Single().Name.Should().Be("Electricians");
        result.Materials.Single().RequiredQuantity.Should().Be(3.5m);
        result.Responses.Should().BeEmpty();
        fixture.Repository.Saves.Should().Be(1);
        (await fixture.Service.GetAsync(result.Id)).Should().BeEquivalentTo(result);
    }

    [Fact]
    public async Task Season_must_match_the_owned_work_area()
    {
        var fixture = new Fixture();
        var act = () => fixture.Service.CreateAsync(new(Guid.NewGuid(),fixture.Area.WorkAreaId,
            SupportType.Staffing,null,null,[],[]));
        await act.Should().ThrowAsync<DomainRuleException>().Where(e=>e.Code=="SUPPORT_SEASON_MISMATCH");
        fixture.Repository.Rows.Should().BeEmpty();
    }

    [Fact]
    public void Duplicate_material_and_skill_lines_are_rejected()
    {
        var material=Guid.NewGuid();var skill=Guid.NewGuid();
        var act=()=>SupportRequest.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),SupportType.Mixed,null,null,
            [new(null,"Team",1,null,[new(skill,null),new(skill,null)])],[],DateTimeOffset.UtcNow);
        act.Should().Throw<DomainRuleException>().Which.Code.Should().Be("SUPPORT_SKILL_DUPLICATE");
        act=()=>SupportRequest.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),SupportType.Material,null,null,[],
            [new(null,material,1,null),new(null,material,2,null)],DateTimeOffset.UtcNow);
        act.Should().Throw<DomainRuleException>().Which.Code.Should().Be("SUPPORT_MATERIAL_DUPLICATE");
    }

    private sealed class Fixture
    {
        public User Current {get;}=new();
        public SeasonScope Season {get;}
        public WorkAreaScope Area {get;}
        public Repository Repository {get;}=new();
        public SupportRequestService Service {get;}
        public Fixture()
        {
            Season=SeasonScope.Create(Guid.NewGuid(),Current.ParishId!.Value,new(2026,10,1),new(2027,1,15),SeasonScopeStatus.Planning,1,DateTimeOffset.UtcNow);
            Area=WorkAreaScope.Create(Guid.NewGuid(),Season.SeasonId,Current.ParishId.Value,"Area",null,Current.UserId,null,null,null,
                WorkAreaScopeStatus.Planned,1,DateTimeOffset.UtcNow);
            var identity=new Identity();identity.Users[Current.UserId]=new(Current.UserId,"Leader","ACTIVE","LEADER",Current.ParishId,null,[]);
            Service=new(Repository,new TaskScopeAccess(new Scopes(Season,Area),Current),new ActiveUserAccess(identity,Current),Current);
        }
    }
    private sealed class User : ICurrentUser
    { public bool IsAuthenticated=>true;public Guid UserId{get;}=Guid.NewGuid();public string Role=>"LEADER";public Guid? ParishId{get;}=Guid.NewGuid();public Guid? CommunityId=>null; }
    private sealed class Identity : IIdentityUserLookup
    { public Dictionary<Guid,IdentityUser> Users{get;}=[];public Task<IdentityUser?> FindAsync(Guid id,CancellationToken ct=default)=>Task.FromResult(Users.GetValueOrDefault(id)); }
    private sealed class Scopes(SeasonScope season,WorkAreaScope area) : IScopeProjectionStore
    {
        public Task<SeasonScope?> GetSeasonAsync(Guid id,CancellationToken ct=default)=>Task.FromResult<SeasonScope?>(id==season.SeasonId?season:null);
        public Task<WorkAreaScope?> GetWorkAreaAsync(Guid id,CancellationToken ct=default)=>Task.FromResult<WorkAreaScope?>(id==area.WorkAreaId?area:null);
        public void Add(SeasonScope row)=>throw new NotSupportedException();public void Add(WorkAreaScope row)=>throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct=default)=>throw new NotSupportedException();
    }
    private sealed class Repository : ISupportRequestRepository
    {
        public List<SupportRequest> Rows{get;}=[];public int Saves{get;private set;}
        public Task<SupportRequest?> GetAsync(Guid id,bool tracked,CancellationToken ct)=>Task.FromResult(Rows.SingleOrDefault(r=>r.Id==id));
        public Task<(IReadOnlyList<SupportRequest> Items,long Total)> GetPageAsync(Guid? parish,Guid? season,Guid? area,
            SupportType? type,SupportRequestStatus? status,int page,int size,bool desc,CancellationToken ct)
        {
            var query=Rows.Where(r=>(!season.HasValue||r.SeasonId==season) && (!area.HasValue||r.WorkAreaId==area) &&
                (!type.HasValue||r.SupportType==type) && (!status.HasValue||r.Status==status)).OrderBy(r=>r.CreatedAt).ToList();
            if(desc)query.Reverse();return Task.FromResult<(IReadOnlyList<SupportRequest>,long)>((query.Skip((page-1)*size).Take(size).ToList(),query.Count));
        }
        public void Add(SupportRequest row)=>Rows.Add(row);public Task SaveAsync(CancellationToken ct){Saves++;return Task.CompletedTask;}
    }

    [Fact]
    public async Task List_filters_by_tenant_and_returns_stable_page()
    {
        var fixture=new Fixture();
        await fixture.Service.CreateAsync(new(fixture.Season.SeasonId,fixture.Area.WorkAreaId,SupportType.Staffing,null,null,[],[]));
        var page=await fixture.Service.ListAsync(fixture.Season.SeasonId,null,SupportType.Staffing,SupportRequestStatus.Planning,1,20,true);
        page.Total.Should().Be(1);page.Items.Single().WorkAreaId.Should().Be(fixture.Area.WorkAreaId);
    }
}
