using FluentAssertions;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Support;
using TaskExecution.Application.Users;
using TaskExecution.Domain;
using TaskExecution.Domain.Support;

namespace TaskExecution.UnitTests.Application;

public sealed class SupportClosureTests
{
    [Fact]
    public async Task Leader_can_cancel_planning_request_with_reason_and_event_in_the_transaction()
    {
        var f=new SupportPublicationTests.Fixture();
        var handler=Handler(f);
        var result=await handler.HandleAsync(f.Request.Id,SupportRequestStatus.Cancelled," no longer needed ",1);
        result.Status.Should().Be(SupportRequestStatus.Cancelled); result.CloseReason.Should().Be("no longer needed");
        result.ClosedBy.Should().Be(f.Source.User.UserId); result.ClosedAt.Should().NotBeNull();
        f.Store.SavedInTransaction.Should().BeTrue(); f.Source.Events.Items.Should().ContainSingle(row=>row.EventType=="support.request.closed");
        var version=result.Version;
        (await handler.HandleAsync(f.Request.Id,SupportRequestStatus.Cancelled," no longer needed ",version)).Version.Should().Be(version);
        f.Source.Events.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Published_pending_people_are_cancelled_without_creating_or_deleting_assignment_history()
    {
        var f=new SupportPublicationTests.Fixture(); await f.Handler.HandleAsync(f.Request.Id,1);
        var response=f.Request.OfferStaffing(f.Source.VolunteerId,null,[new(f.Request.Positions.Single().Id,f.Source.VolunteerId)],null,DateTimeOffset.UtcNow);
        await Handler(f).HandleAsync(f.Request.Id,SupportRequestStatus.Cancelled,"cancel",f.Request.Version);
        response.Members.Single().Status.Should().Be(SupportMemberStatus.Cancelled);
        response.Status.Should().Be(SupportResponseStatus.Cancelled); response.Members.Single().AssignmentId.Should().BeNull();
    }

    [Fact]
    public void A_fulfilled_request_can_close_and_keeps_confirmed_receipts()
    {
        var request=MaterialRequest(); var item=request.Responses.Single().Materials.Single();
        request.BeginMaterialApproval(item.Id,5,Guid.NewGuid(),item.Version,"closure",DateTimeOffset.UtcNow);
        request.ResolveMaterialApproval(item.Id,item.ApprovalOperationId!.Value,null,DateTimeOffset.UtcNow);
        request.ReceiveMaterial(item.Id,5,0,Guid.NewGuid(),DateTimeOffset.UtcNow,DateTimeOffset.UtcNow);
        request.Close(SupportRequestStatus.Closed,null,Guid.NewGuid(),request.Version,DateTimeOffset.UtcNow).Should().BeTrue();
        request.Status.Should().Be(SupportRequestStatus.Closed); item.Status.Should().Be(SupportMaterialStatus.Transferred);
        item.ReceivedUsableQuantity.Should().Be(5);
    }

    [Fact]
    public void Pending_withdrawal_and_in_transit_receipt_block_closure_or_cancellation()
    {
        var request=MaterialRequest(); var item=request.Responses.Single().Materials.Single();
        request.BeginMaterialApproval(item.Id,5,Guid.NewGuid(),item.Version,"closure",DateTimeOffset.UtcNow);
        Action cancel=()=>request.Close(SupportRequestStatus.Cancelled,"cancel",Guid.NewGuid(),request.Version,DateTimeOffset.UtcNow);
        cancel.Should().Throw<DomainRuleException>().Which.Code.Should().Be("SUPPORT_TRANSFER_SYNC_PENDING");
        request.ResolveMaterialApproval(item.Id,item.ApprovalOperationId!.Value,null,DateTimeOffset.UtcNow);
        cancel.Should().Throw<DomainRuleException>().Which.Code.Should().Be("SUPPORT_TRANSFER_SYNC_PENDING");
        request.ReceiveMaterial(item.Id,0,5,Guid.NewGuid(),DateTimeOffset.UtcNow,DateTimeOffset.UtcNow);
        cancel.Should().Throw<DomainRuleException>().Which.Code.Should().Be("SUPPORT_REQUEST_HAS_COMMITMENTS");
    }

    [Fact]
    public async Task Missing_request_wrong_leader_stale_version_and_unfulfilled_close_are_rejected()
    {
        var f=new SupportPublicationTests.Fixture(); var handler=Handler(f);
        Func<Task> action=()=>handler.HandleAsync(Guid.NewGuid(),SupportRequestStatus.Closed,null,1);
        await action.Should().ThrowAsync<DomainRuleException>().Where(e=>e.Code=="SUPPORT_REQUEST_NOT_FOUND");
        action=()=>handler.HandleAsync(f.Request.Id,SupportRequestStatus.Closed,null,99);
        await action.Should().ThrowAsync<DomainRuleException>().Where(e=>e.Code=="SUPPORT_REQUEST_VERSION_MISMATCH");
        action=()=>handler.HandleAsync(f.Request.Id,SupportRequestStatus.Closed,null,1);
        await action.Should().ThrowAsync<DomainRuleException>().Where(e=>e.Code=="SUPPORT_REQUEST_NOT_FULFILLED");
        f.Source.User.Role="ADMIN";
        await action.Should().ThrowAsync<DomainRuleException>().Where(e=>e.Code=="WORK_AREA_ACCESS_DENIED");
        f.Source.Events.Items.Should().BeEmpty();
    }
    private static CloseSupportRequestHandler Handler(SupportPublicationTests.Fixture f)=>new(f.Store,
        new TaskScopeAccess(new AssignmentTestFixture.TestScopes(f.Source.Season,f.Source.Area),f.Source.User),
        new ActiveUserAccess(f.Source.Identity,f.Source.User),f.Source.Events,new AssignmentTestFixture.TestCorrelation(),TimeProvider.System);
    private static SupportRequest MaterialRequest()
    {
        var request=SupportRequest.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),SupportType.Material,null,null,[],[new(null,Guid.NewGuid(),5,null)],DateTimeOffset.UtcNow);
        request.Publish(1,DateTimeOffset.UtcNow);
        request.OfferResponse(Guid.NewGuid(),null,[],[new(request.Materials.Single().Id,Guid.NewGuid(),5)],null,DateTimeOffset.UtcNow);
        return request;
    }
}
