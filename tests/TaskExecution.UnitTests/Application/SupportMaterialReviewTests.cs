using FluentAssertions;
using TaskExecution.Application.Events;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Support;
using TaskExecution.Application.Users;
using TaskExecution.Domain;
using TaskExecution.Domain.Support;

namespace TaskExecution.UnitTests.Application;

public sealed class SupportMaterialReviewTests
{
    [Fact]
    public async Task Approval_commits_intent_before_remote_call_and_does_not_fulfill_without_receipt()
    {
        var f=await Fixture.Create();
        f.Gateway.BeforeExecute=()=>f.Item.ApprovalPending.Should().BeTrue();
        var dto=await f.Review(true,5);
        dto.Status.Should().Be(SupportMaterialStatus.Approved); dto.ApprovedQuantity.Should().Be(5);
        dto.PendingSync.Should().BeTrue(); f.Request.Status.Should().Be(SupportRequestStatus.Published);
        f.Source.Events.Items.Should().Contain(row=>row.EventType=="support.material.approved");
    }

    [Fact]
    public async Task Timeout_retains_immutable_intent_and_reconciliation_recovers_without_another_withdrawal()
    {
        var f=await Fixture.Create(); f.Gateway.FailAfterCommit=true;
        Func<Task> approve=()=>f.Review(true,5);
        await approve.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="RESOURCE_SERVICE_UNAVAILABLE");
        var operation=f.Item.ApprovalOperationId;
        f.Item.Status.Should().Be(SupportMaterialStatus.Pending); f.Item.ApprovalPending.Should().BeTrue();
        Func<Task> reject=()=>f.Review(false,null,"reject");
        await reject.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="SUPPORT_MATERIAL_APPROVAL_PENDING");
        (await f.Processor.ProcessAsync(f.Item.Id,default)).Item.Status.Should().Be(SupportMaterialStatus.Approved);
        f.Item.ApprovalOperationId.Should().Be(operation); f.Gateway.Withdrawals.Should().Be(1);
    }

    [Fact]
    public async Task Persisted_stock_rejection_is_committed_and_allows_a_new_explicit_attempt()
    {
        var f=await Fixture.Create(); f.Gateway.Rejection="MATERIAL_INSUFFICIENT_AVAILABLE";
        Func<Task> approve=()=>f.Review(true,5);
        await approve.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="MATERIAL_INSUFFICIENT_AVAILABLE");
        f.Item.ApprovalPending.Should().BeFalse(); var old=f.Item.ApprovalOperationId;
        f.Gateway.Rejection=null;
        await f.Review(true,4);
        f.Item.ApprovalOperationId.Should().NotBe(old!.Value); f.Item.ApprovedQuantity.Should().Be(4);
    }

    [Theory]
    [InlineData(0)] [InlineData(11)] [InlineData(1.001)]
    public async Task Invalid_quantity_cannot_create_an_intent(double quantity)
    {
        var f=await Fixture.Create(); Func<Task> act=()=>f.Review(true,(decimal)quantity);
        await act.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Kind==DomainErrorKind.Validation);
        f.Gateway.Withdrawals.Should().Be(0); f.Item.ApprovalOperationId.Should().BeNull();
    }

    [Fact]
    public async Task Rejection_requires_reason_preserves_offer_note_and_never_calls_resource()
    {
        var f=await Fixture.Create(); Func<Task> reject=()=>f.Review(false,null," ");
        await reject.Should().ThrowAsync<DomainRuleException>();
        var dto=await f.Review(false,null," no need ");
        dto.Status.Should().Be(SupportMaterialStatus.Rejected); dto.ReviewReason.Should().Be("no need");
        dto.Note.Should().Be("offer"); f.Gateway.Withdrawals.Should().Be(0);
    }

    [Fact]
    public async Task Foreign_leader_archived_season_and_stale_version_are_rejected_before_withdrawal()
    {
        var f=await Fixture.Create(); var user=f.Source.User.UserId; f.Source.User.UserId=Guid.NewGuid();
        Func<Task> approve=()=>f.Review(true,5);
        await approve.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="WORK_AREA_ACCESS_DENIED");
        f.Source.User.UserId=user;
        approve=()=>f.Handler.HandleAsync(f.Item.Id,true,5,null,999);
        await approve.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="SUPPORT_MATERIAL_VERSION_MISMATCH");
        var s=f.Source.Season; s.Apply(s.ParishId,s.StartDate,s.EndDate,TaskExecution.Domain.Scopes.SeasonScopeStatus.Archived,2,DateTimeOffset.UtcNow);
        approve=()=>f.Review(true,5);
        await approve.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="SEASON_READ_ONLY");
        f.Gateway.Withdrawals.Should().Be(0);
    }

    internal sealed class Fixture
    {
        public SupportMaterialOfferTests.Fixture Offers {get;}=new();
        public AssignmentTestFixture Source=>Offers.Base.Source;
        public SupportRequest Request=>Offers.Base.Request;
        public SupportResponseMaterial Item=>Request.Responses.Single().Materials.Single();
        public Gateway Gateway {get;}=new();
        public ReviewSupportMaterialHandler Handler {get;private set;}=null!;
        public SupportMaterialApprovalProcessor Processor {get;private set;}=null!;
        public static async Task<Fixture> Create()
        {
            var f=new Fixture(); await f.Offers.Submit(10);
            f.Source.User.Role="LEADER";
            f.Source.Identity.Items[f.Source.User.UserId]=f.Source.Identity.Items[f.Source.User.UserId] with {Role="LEADER"};
            var store=new SyncStore(f.Request); var events=new SupportMaterialReviewEvents(f.Source.Events);
            f.Processor=new(f.Offers.Base.Store,store,f.Gateway,f.Offers.Scopes,events,TimeProvider.System);
            f.Handler=new(f.Offers.Base.Store,store,new TaskScopeAccess(f.Offers.Scopes,f.Source.User),
                new ActiveUserAccess(f.Source.Identity,f.Source.User),new AssignmentTestFixture.TestCorrelation(),TimeProvider.System,f.Processor,events);
            return f;
        }
        public Task<SupportResponseMaterialDto> Review(bool approve,decimal? quantity,string? reason=null)=>Handler.HandleAsync(Item.Id,approve,quantity,reason,Item.Version);
    }
    private sealed class SyncStore(SupportRequest request) : ISupportMaterialSyncStore
    {
        public Task<Guid?> FindRequestIdAsync(Guid id,CancellationToken ct)=>Task.FromResult<Guid?>(request.Responses.SelectMany(r=>r.Materials).Any(r=>r.Id==id) ? request.Id : null);
        public Task<IReadOnlyList<Guid>> GetPendingItemsAsync(Guid? after,int size,CancellationToken ct)=>throw new NotSupportedException();
    }
    internal sealed class Gateway : ISupportTransferGateway
    {
        public readonly Dictionary<Guid,SupportTransferDecision> Decisions=[]; public int Withdrawals;
        public bool FailAfterCommit; public string? Rejection; public Action? BeforeExecute;
        public Task<SupportTransferDecision?> FindDecisionAsync(SupportTransferAttempt attempt,CancellationToken ct)=>Task.FromResult(Decisions.GetValueOrDefault(attempt.OperationId));
        public Task<SupportTransferDecision> ExecuteAsync(SupportTransferAttempt attempt,CancellationToken ct)
        {
            BeforeExecute?.Invoke();
            var result=new SupportTransferDecision(attempt.OperationId,attempt.ItemId,attempt.SourceWorkAreaId,attempt.TargetWorkAreaId,
                attempt.MaterialId,attempt.Quantity,attempt.ApproverId,Rejection is null ? "COMPLETED" : "REJECTED",Rejection,DateTimeOffset.UtcNow);
            Decisions[attempt.OperationId]=result; if(Rejection is null) Withdrawals++;
            if(FailAfterCommit) throw new DomainRuleException("RESOURCE_SERVICE_UNAVAILABLE","timeout",DomainErrorKind.ServiceUnavailable);
            return Task.FromResult(result);
        }
    }
}
