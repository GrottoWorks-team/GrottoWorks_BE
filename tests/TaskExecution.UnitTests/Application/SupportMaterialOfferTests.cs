using System.Globalization;
using FluentAssertions;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Support;
using TaskExecution.Application.Users;
using TaskExecution.Domain;
using TaskExecution.Domain.Scopes;
using TaskExecution.Domain.Support;

namespace TaskExecution.UnitTests.Application;

public sealed class SupportMaterialOfferTests
{
    [Fact]
    public async Task Assigned_source_officer_offers_material_without_creating_stock_movements_or_counting_it_as_fulfilled()
    {
        var f=new Fixture();
        var dto=await f.Submit(5);
        dto.ResponseQuantity.Should().Be(0);dto.VolunteerId.Should().BeNull();dto.Members.Should().BeEmpty();
        var material=dto.Materials.Should().ContainSingle().Which;
        material.OfferedQuantity.Should().Be(5);material.MaterialId.Should().Be(f.MaterialId);
        material.SourceWorkAreaId.Should().Be(f.Source.WorkAreaId);material.Status.Should().Be(SupportMaterialStatus.Pending);
        material.ApprovedQuantity.Should().BeNull();material.ReceivedUsableQuantity.Should().BeNull();
        material.Note.Should().Be("offer");f.Base.Request.Status.Should().Be(SupportRequestStatus.Published);
        f.Inventory.Calls.Should().Be(1);f.Base.Store.Saves.Should().Be(1);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.001")]
    public async Task Invalid_quantity_is_rejected_before_reading_resource(string quantity)
    {
        var f=new Fixture();Func<Task> submit=()=>f.Submit(decimal.Parse(quantity,CultureInfo.InvariantCulture));
        await submit.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="SUPPORT_MATERIAL_OFFER_INVALID");
        f.Inventory.Calls.Should().Be(0);f.Base.Request.Responses.Should().BeEmpty();
    }

    [Fact]
    public async Task Insufficient_stock_and_inactive_material_are_rejected()
    {
        var f=new Fixture();Func<Task> submit=()=>f.Submit(11);
        await submit.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="MATERIAL_INSUFFICIENT_AVAILABLE");
        f.Inventory.Rows[0]=f.Inventory.Rows[0] with {MaterialStatus="INACTIVE"};submit=()=>f.Submit(5);
        await submit.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="MATERIAL_INACTIVE");
        f.Base.Store.Saves.Should().Be(0);
    }

    [Fact]
    public async Task Only_the_current_source_officer_can_offer_and_season_must_match()
    {
        var f=new Fixture();
        f.Scopes.Areas[f.Source.WorkAreaId]=f.Area(f.Source.WorkAreaId,f.Source.SeasonId,Guid.NewGuid());
        Func<Task> submit=()=>f.Submit(5);
        await submit.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="WORK_AREA_ACCESS_DENIED");
        f.Scopes.Areas[f.Source.WorkAreaId]=f.Area(f.Source.WorkAreaId,Guid.NewGuid(),f.Base.Source.User.UserId);
        await submit.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="SUPPORT_TRANSFER_OTHER_SEASON");
        f.Inventory.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Other_request_demand_same_area_and_duplicate_lines_are_rejected()
    {
        var f=new Fixture();
        Func<Task> submit=()=>f.Handler.HandleAsync(f.Base.Request.Id,null,[],[new(Guid.NewGuid(),f.Source.WorkAreaId,1)],null);
        await submit.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="SUPPORT_MATERIAL_MEMBERSHIP_INVALID");
        submit=()=>f.Handler.HandleAsync(f.Base.Request.Id,null,[],[new(f.DemandId,f.Base.Request.WorkAreaId,1)],null);
        await submit.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="SUPPORT_TRANSFER_SAME_WORK_AREA");
        var offer=new SupportMaterialOfferInput(f.DemandId,f.Source.WorkAreaId,1);
        submit=()=>f.Handler.HandleAsync(f.Base.Request.Id,null,[],[offer,offer],null);
        await submit.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="SUPPORT_MATERIAL_DUPLICATE");
    }

    [Fact]
    public async Task Unsynchronized_resource_scope_is_unavailable_rather_than_authorizing_from_stale_data()
    {
        var f=new Fixture();f.Inventory.Rows[0]=f.Inventory.Rows[0] with {MaterialOfficerUserId=Guid.NewGuid()};
        Func<Task> submit=()=>f.Submit(5);
        await submit.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="RESOURCE_SERVICE_UNAVAILABLE");
        f.Base.Request.Responses.Should().BeEmpty();
    }

    internal sealed class Fixture
    {
        public SupportPublicationTests.Fixture Base {get;}=new();
        public Guid MaterialId {get;}=Guid.NewGuid();
        public Guid DemandId=>Base.Request.Materials.Single().Id;
        public WorkAreaScope Source {get;}
        public ScopeStore Scopes {get;}
        public Stock Inventory {get;}=new();
        public SupportResponseHandler Handler {get;}
        public Fixture()
        {
            Base.Request.Update(SupportType.Mixed,false,null,false,null,false,null,true,[new(null,MaterialId,20,null)],1,DateTimeOffset.UtcNow);
            Base.Request.Publish(2,DateTimeOffset.UtcNow);
            Base.Source.User.Role="MO";
            var caller=Base.Source.User.UserId;Base.Source.Identity.Items[caller]=Base.Source.Identity.Items[caller] with {Role="MO"};
            Source=Area(Guid.NewGuid(),Base.Source.Season.SeasonId,caller);
            Scopes=new ScopeStore(Base.Source.Season,[Base.Source.Area,Source]);
            Inventory.Rows.Add(new(Source.WorkAreaId,Source.ParishId,Source.SeasonId,caller,Source.SourceVersion,MaterialId,"ACTIVE",10,DateTimeOffset.UtcNow));
            Handler=new(Base.Store,new TaskScopeAccess(Scopes,Base.Source.User),Scopes,new ActiveUserAccess(Base.Source.Identity,Base.Source.User),
                Base.Source.Identity,Base.Source.User,TimeProvider.System,new ValidateSupportMaterialOffers(Scopes,Inventory));
        }
        public WorkAreaScope Area(Guid id,Guid seasonId,Guid officer)=>WorkAreaScope.Create(id,seasonId,Base.Source.Area.ParishId,"Source",null,
            null,officer,null,null,WorkAreaScopeStatus.Planned,1,DateTimeOffset.UtcNow);
        public Task<SupportResponseDto> Submit(decimal quantity)=>Handler.HandleAsync(Base.Request.Id,null,[],[new(DemandId,Source.WorkAreaId,quantity," offer ")],null);
    }
    internal sealed class Stock : ISupportMaterialAvailability
    {
        public List<SupportMaterialAvailability> Rows {get;}=[];public int Calls {get;private set;}
        public Task<IReadOnlyList<SupportMaterialAvailability>> GetAsync(Guid id,IReadOnlyCollection<Guid> ids,CancellationToken ct)
        {Calls++;return Task.FromResult<IReadOnlyList<SupportMaterialAvailability>>(Rows.Where(row=>row.WorkAreaId==id && ids.Contains(row.MaterialId)).ToArray());}
    }
    internal sealed class ScopeStore(SeasonScope season,IReadOnlyList<WorkAreaScope> areas) : IScopeProjectionStore
    {
        public Dictionary<Guid,WorkAreaScope> Areas {get;}=areas.ToDictionary(area=>area.WorkAreaId);
        public Task<SeasonScope?> GetSeasonAsync(Guid id,CancellationToken ct=default)=>Task.FromResult<SeasonScope?>(id==season.SeasonId ? season : null);
        public Task<WorkAreaScope?> GetWorkAreaAsync(Guid id,CancellationToken ct=default)=>Task.FromResult(Areas.GetValueOrDefault(id));
        public void Add(SeasonScope row)=>throw new NotSupportedException();public void Add(WorkAreaScope row)=>throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct=default)=>throw new NotSupportedException();
    }
}
