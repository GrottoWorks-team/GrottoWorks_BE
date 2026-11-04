using System.Text.Json;
using FluentAssertions;
using Resource.Contracts.Events;
using Resource.Contracts.Inventory;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Support;
using TaskExecution.Domain;
using TaskExecution.Domain.Support;

namespace TaskExecution.UnitTests.Application;

public sealed class SupportTransferSynchronizationTests
{
    [Fact]
    public async Task Reconciliation_records_usable_and_unusable_once_and_does_not_count_the_full_offer()
    {
        var f=await SupportMaterialReviewTests.Fixture.Create(); await f.Review(true,10);
        var lookup=new Lookup(Snapshot(f,7,3)); var sync=Sync(f,lookup);
        await sync.ReconcileAsync(f.Item.Id,default);
        f.Item.Status.Should().Be(SupportMaterialStatus.Transferred);
        f.Item.ReceivedUsableQuantity.Should().Be(7); f.Item.ReceivedUnusableQuantity.Should().Be(3);
        f.Request.Status.Should().Be(SupportRequestStatus.PartiallyFulfilled);
        SupportResponseMaterialDto.From(f.Item).PendingSync.Should().BeFalse(); var version=f.Item.Version;
        await sync.ApplyAsync(lookup.Row!,"replay",default);
        f.Item.Version.Should().Be(version);
        f.Source.Events.Items.Count(row=>row.EventType=="support.transfer.synchronized").Should().Be(1);
        f.Source.Events.Items.Single(row=>row.EventType=="support.transfer.synchronized").Payload.GetProperty("status").GetString()
            .Should().Be("PARTIALLY_FULFILLED");
    }

    [Fact]
    public async Task Missing_transfer_or_wrong_tenant_remains_pending_and_never_fabricates_a_receipt()
    {
        var f=await SupportMaterialReviewTests.Fixture.Create(); await f.Review(true,10);
        var lookup=new Lookup(null); var sync=Sync(f,lookup);
        Func<Task> reconcile=()=>sync.ReconcileAsync(f.Item.Id,default);
        await reconcile.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="SUPPORT_TRANSFER_SYNC_PENDING");
        lookup.Row=Snapshot(f,5,0) with {ParishId=Guid.NewGuid()};
        await reconcile.Should().ThrowAsync<DomainRuleException>();
        f.Item.Status.Should().Be(SupportMaterialStatus.Approved); f.Item.ReceivedUsableQuantity.Should().BeNull();
    }

    [Fact]
    public async Task Conflicting_or_oversized_receipt_cannot_rewrite_the_source_result()
    {
        var f=await SupportMaterialReviewTests.Fixture.Create(); await f.Review(true,10); var sync=Sync(f,new Lookup(null));
        Func<Task> invalid=()=>sync.ApplyAsync(Snapshot(f,11,0),"bad",default);
        await invalid.Should().ThrowAsync<DomainRuleException>();
        var row=Snapshot(f,5,1); await sync.ApplyAsync(row,"first",default);
        invalid=()=>sync.ApplyAsync(row with {ReceivedUsableQuantity=6},"different",default);
        await invalid.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="SUPPORT_TRANSFER_RESULT_CONFLICT");
        f.Item.ReceivedUsableQuantity.Should().Be(5);
    }

    [Fact]
    public async Task Receipt_event_can_recover_timed_out_approval_and_duplicate_delivery_is_a_no_op()
    {
        var f=await SupportMaterialReviewTests.Fixture.Create(); f.Gateway.FailAfterCommit=true;
        Func<Task> approve=()=>f.Review(true,10); await approve.Should().ThrowAsync<DomainRuleException>();
        var transferredAt=DateTimeOffset.UtcNow;
        var officer=Guid.NewGuid(); var confirmedAt=transferredAt.AddSeconds(1);
        var sync=Sync(f,new Lookup(new SupportTransferSnapshot(f.Item.Id,f.Item.SourceWorkAreaId,f.Request.WorkAreaId,
            f.Source.Area.ParishId,f.Request.SeasonId,f.Item.MaterialId,10,"RECEIVED",f.Item.ApprovalRequestedBy!.Value,
            transferredAt,8,2,officer,confirmedAt))); var inbox=new Inbox();
        var handler=new SupportTransferReceiptIngestion(inbox,new SupportMaterialReviewTests.SyncStore(f.Request),f.Offers.Base.Store,f.Processor,sync);
        var receipt=new SupportTransferConfirmed(f.Item.Id,f.Item.SourceWorkAreaId,f.Request.WorkAreaId,f.Source.Area.ParishId,
            f.Item.MaterialId,10,8,2,officer,confirmedAt);
        var message=new ResourceIntegrationEvent(Guid.NewGuid(),SupportTransferConfirmed.EventType,1,DateTimeOffset.UtcNow,"receipt",
            receipt.ConfirmedByUserId,receipt.ParishId,null,"support_transfer",receipt.SupportResponseMaterialId,JsonSerializer.SerializeToElement(receipt,JsonSerializerOptions.Web));
        await handler.HandleAsync(message,default); var version=f.Request.Version;
        await handler.HandleAsync(message,default);
        f.Request.Version.Should().Be(version); f.Item.ReceivedUsableQuantity.Should().Be(8); f.Gateway.Withdrawals.Should().Be(1);
        inbox.Processed.Should().HaveCount(1);
    }

    [Fact]
    public void Fulfillment_uses_received_usable_quantity_not_the_withdrawn_or_unusable_quantity()
    {
        var request=SupportRequest.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),SupportType.Material,null,null,[],
            [new(null,Guid.NewGuid(),5,null)],DateTimeOffset.UtcNow);
        request.Publish(1,DateTimeOffset.UtcNow); var demand=request.Materials.Single();
        var response=request.OfferResponse(Guid.NewGuid(),null,[],[new(demand.Id,Guid.NewGuid(),10)],null,DateTimeOffset.UtcNow);
        var item=response.Materials.Single();
        request.BeginMaterialApproval(item.Id,10,Guid.NewGuid(),item.Version,"test",DateTimeOffset.UtcNow);
        request.ResolveMaterialApproval(item.Id,item.ApprovalOperationId!.Value,null,DateTimeOffset.UtcNow);
        request.Status.Should().Be(SupportRequestStatus.Published);
        request.ReceiveMaterial(item.Id,5,5,Guid.NewGuid(),DateTimeOffset.UtcNow,DateTimeOffset.UtcNow);
        request.Status.Should().Be(SupportRequestStatus.Fulfilled);
    }

    private static SupportTransferSynchronization Sync(SupportMaterialReviewTests.Fixture f,Lookup lookup)=>
        new(f.Offers.Base.Store,new SupportMaterialReviewTests.SyncStore(f.Request),lookup,f.Offers.Scopes,f.Source.Events,TimeProvider.System);
    private static SupportTransferSnapshot Snapshot(SupportMaterialReviewTests.Fixture f,decimal usable,decimal unusable)=>
        new(f.Item.Id,f.Item.SourceWorkAreaId,f.Request.WorkAreaId,f.Source.Area.ParishId,f.Request.SeasonId,f.Item.MaterialId,
            f.Item.ApprovedQuantity!.Value,"RECEIVED",f.Item.ApprovedBy!.Value,f.Item.ApprovedAt!.Value,usable,unusable,Guid.NewGuid(),DateTimeOffset.UtcNow.AddSeconds(1));
    private sealed class Lookup(SupportTransferSnapshot? row) : ISupportTransferReceiptLookup
    {
        public SupportTransferSnapshot? Row=row;
        public Task<SupportTransferSnapshot?> GetAsync(Guid id,string correlation,CancellationToken ct)=>Task.FromResult(Row);
    }
    private sealed class Inbox : IScopeEventInbox
    {
        public readonly HashSet<Guid> Processed=[];
        public async Task ExecuteAsync(string consumer,Guid id,Func<Task> apply,CancellationToken ct)
        { if(Processed.Contains(id)) return; await apply(); Processed.Add(id); }
    }
}
