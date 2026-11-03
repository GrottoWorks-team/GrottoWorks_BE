using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Resource.Contracts.Events;
using TaskExecution.Application.Assignments;
using TaskExecution.Application.Events;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Support;
using TaskExecution.Infrastructure.Data;
using TaskExecution.Infrastructure.Data.Configurations;
using TaskExecution.Domain.Assignments;
using TaskExecution.Domain.Tasks;

namespace TaskExecution.Infrastructure.Tasks;

public sealed class PurchaseApprovalProjection
{
    public Guid PurchaseRequestId {get;set;}
    public Guid WorkAreaId {get;set;}
    public string Decision {get;set;}="";
    public DateTimeOffset DecidedAt {get;set;}
}
public sealed class PurchaseApprovalProjectionConfiguration : IEntityTypeConfiguration<PurchaseApprovalProjection>
{
    public void Configure(EntityTypeBuilder<PurchaseApprovalProjection> b)
    {
        b.ToTable("purchase_approval_projection");b.HasKey(x=>x.PurchaseRequestId);
        b.Property(x=>x.PurchaseRequestId).HasColumnName("purchase_request_id");b.Property(x=>x.WorkAreaId).HasColumnName("work_area_id");
        b.Property(x=>x.Decision).HasColumnName("decision").HasMaxLength(30);b.Property(x=>x.DecidedAt).HasColumnName("decided_at");
    }
}
public sealed class PurchaseTaskEventHandler(TaskExecutionDbContext db,IScopeEventInbox inbox,IAssignmentStore transactions,
    ITaskEventOutbox outbox,ISupportAssignmentLifecycle support)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public async Task ReconcileAsync(Guid taskId,TaskExecution.Application.Tasks.PurchaseRequestSnapshot snapshot,CancellationToken ct)
    {
        var delivery=snapshot.Purchases?.Where(row=>row.TaskId==taskId && row.Status=="CONFIRMED" && row.ConfirmedAt.HasValue)
            .OrderBy(row=>row.ConfirmedAt).FirstOrDefault();
        if(delivery is null) return;
        await transactions.InTaskTransactionAsync(taskId,async ()=>
        {
            var task=await db.Tasks.SingleOrDefaultAsync(row=>row.Id==taskId,ct) ?? throw new InvalidOperationException("Purchase task disappeared.");
            if(task.PurchaseRequestId!=snapshot.Id || task.WorkAreaId!=snapshot.WorkAreaId) throw new FormatException("Purchase snapshot ownership mismatch.");
            if(task.Status is TaskExecution.Domain.Tasks.TaskStatus.Completed or TaskExecution.Domain.Tasks.TaskStatus.Cancelled) return false;
            var area=await db.WorkAreaScopes.SingleAsync(row=>row.WorkAreaId==task.WorkAreaId,ct);
            var members=await db.Assignments.Where(row=>row.TaskId==taskId && (row.Status==AssignmentStatus.Accepted || row.Status==AssignmentStatus.InProgress)).ToListAsync(ct);
            if(members.Count!=1 || members[0].VolunteerId!=delivery.BuyerUserId || delivery.ConfirmedByUserId is null || delivery.ConfirmedByUserId==Guid.Empty)
                throw new InvalidOperationException("A confirmed purchase must match its authoritative buyer and confirming officer.");
            task.CompletePurchase(DateTimeOffset.UtcNow);
            foreach(var member in members) member.CompleteForTask();
            await support.CompleteAssignmentsAsync(members.Select(row=>row.Id).ToArray(),DateTimeOffset.UtcNow,ct);
            outbox.Add(new TaskExecution.Contracts.Events.TaskIntegrationEvent(Guid.NewGuid(),"task.completion.reviewed",1,DateTimeOffset.UtcNow,
                $"purchase-reconciliation:{delivery.PurchaseId:D}",delivery.ConfirmedByUserId.Value,area.ParishId,area.SeasonId,"task",taskId,task.Version,
                JsonSerializer.SerializeToElement(new {taskId,workAreaId=area.WorkAreaId,purchaseId=delivery.PurchaseId,purchaseRequestId=snapshot.Id,
                    approved=true,decision="APPROVED",status="COMPLETED",leaderUserId=area.LeaderUserId,
                    seasonId=area.SeasonId,parishId=area.ParishId,taskName=task.Name,taskType="PURCHASE",
                    volunteerUserIds=members.Select(row=>row.VolunteerId).Distinct().ToArray(),
                    reviewedByUserId=delivery.ConfirmedByUserId.Value,reviewedAt=DateTimeOffset.UtcNow,
                    recipientUserIds=members.Select(row=>row.VolunteerId).Concat(area.LeaderUserId.HasValue ? new[]{area.LeaderUserId.Value} : []).Distinct().ToArray()},Json)));
            await db.SaveChangesAsync(ct);return true;
        },ct);
    }
    public async Task HandleAsync(ResourceIntegrationEvent message,CancellationToken ct)
    {
        if(message.EventType is not (PurchaseRequestDecided.EventType or PurchaseDeliveryConfirmed.EventType)) return;
        if(message.EventId==Guid.Empty || message.Version!=1) throw new FormatException("Invalid purchase event envelope.");
        await inbox.ExecuteAsync("task-purchases",message.EventId,async ()=>
        {
            if(message.EventType==PurchaseRequestDecided.EventType)
            {
                var payload=message.Payload.Deserialize<PurchaseRequestDecided>(Json) ?? throw new FormatException("Missing purchase decision.");
                if(payload.PurchaseRequestId==Guid.Empty || payload.WorkAreaId==Guid.Empty || payload.Decision is not ("APPROVED" or "REJECTED")) throw new FormatException("Invalid purchase decision.");
                var row=await db.Set<PurchaseApprovalProjection>().SingleOrDefaultAsync(row=>row.PurchaseRequestId==payload.PurchaseRequestId,ct);
                if(row is not null && row.DecidedAt>=payload.DecidedAt) return;
                if(row is null) {row=new(){PurchaseRequestId=payload.PurchaseRequestId};db.Add(row);}
                row.WorkAreaId=payload.WorkAreaId;row.Decision=payload.Decision;row.DecidedAt=payload.DecidedAt;
                return;
            }
            var delivery=message.Payload.Deserialize<PurchaseDeliveryConfirmed>(Json) ?? throw new FormatException("Missing purchase delivery.");
            if(delivery.TaskId==Guid.Empty || delivery.PurchaseRequestId==Guid.Empty || delivery.BuyerUserId==Guid.Empty || delivery.PurchaseId==Guid.Empty ||
                delivery.RequestStatus is not ("PARTIALLY_RECEIVED" or "COMPLETED") || delivery.Items is null || delivery.Items.Count==0 ||
                delivery.Items.Any(row=>row.MaterialId==Guid.Empty || row.ReceivedUsableQuantity<0 || row.ReceivedUnusableQuantity<0 ||
                    row.ReceivedUsableQuantity+row.ReceivedUnusableQuantity>row.PurchasedQuantity)) throw new FormatException("Invalid purchase delivery.");
            await transactions.InTaskTransactionAsync(delivery.TaskId,async ()=>
            {
                var task=await db.Tasks.SingleOrDefaultAsync(row=>row.Id==delivery.TaskId,ct)
                    ?? throw new InvalidOperationException("Purchase task is missing; retry/reconcile before acknowledgement.");
                var area=await db.WorkAreaScopes.SingleAsync(row=>row.WorkAreaId==task.WorkAreaId,ct);
                if(task.Type!=TaskType.Purchase || task.PurchaseRequestId!=delivery.PurchaseRequestId || task.WorkAreaId!=delivery.WorkAreaId ||
                    area.ParishId!=delivery.ParishId || area.SeasonId!=delivery.SeasonId) throw new FormatException("Purchase delivery scope mismatch.");
                var members=await db.Assignments.Where(row=>row.TaskId==task.Id).ToListAsync(ct);
                if(!members.Any(row=>row.VolunteerId==delivery.BuyerUserId && row.Status is AssignmentStatus.Accepted or AssignmentStatus.InProgress or AssignmentStatus.Completed))
                    throw new InvalidOperationException("Purchase buyer assignment is missing; reconcile before acknowledgement.");
                if(!task.CompletePurchase(DateTimeOffset.UtcNow)) return false;
                var active=members.Where(row=>row.Status is AssignmentStatus.Accepted or AssignmentStatus.InProgress).ToArray();
                foreach(var assignment in active) assignment.CompleteForTask();
                await support.CompleteAssignmentsAsync(active.Select(row=>row.Id).ToArray(),DateTimeOffset.UtcNow,ct);
                outbox.Add(new TaskExecution.Contracts.Events.TaskIntegrationEvent(Guid.NewGuid(),"task.completion.reviewed",1,DateTimeOffset.UtcNow,
                    message.CorrelationId,delivery.ConfirmedByUserId,area.ParishId,area.SeasonId,"task",task.Id,task.Version,
                    JsonSerializer.SerializeToElement(new {taskId=task.Id,workAreaId=area.WorkAreaId,approved=true,status="COMPLETED",decision="APPROVED",
                        seasonId=area.SeasonId,parishId=area.ParishId,taskName=task.Name,taskType="PURCHASE",
                        volunteerUserIds=active.Select(row=>row.VolunteerId).Distinct().ToArray(),
                        reviewedByUserId=delivery.ConfirmedByUserId,reviewedAt=DateTimeOffset.UtcNow,
                        purchaseRequestId=delivery.PurchaseRequestId,purchaseId=delivery.PurchaseId,leaderUserId=area.LeaderUserId,
                        recipientUserIds=active.Select(row=>row.VolunteerId).Concat(area.LeaderUserId.HasValue ? new[]{area.LeaderUserId.Value} : []).Distinct().ToArray()},Json)));
                return true;
            },ct);
        },ct);
    }
}
public sealed class PurchaseTaskConsumer(PurchaseTaskEventHandler handler) : IConsumer<ResourceIntegrationEvent>
{
    public Task Consume(ConsumeContext<ResourceIntegrationEvent> context)=>handler.HandleAsync(context.Message,context.CancellationToken);
}
public sealed class PurchaseTaskConsumerDefinition : ConsumerDefinition<PurchaseTaskConsumer>
{
    public PurchaseTaskConsumerDefinition(){EndpointName="task-purchase-events";ConcurrentMessageLimit=4;}
    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpoint,IConsumerConfigurator<PurchaseTaskConsumer> consumer,IRegistrationContext context)=>
        endpoint.UseMessageRetry(retry=>{retry.Ignore<FormatException>();retry.Intervals(TimeSpan.FromSeconds(1),TimeSpan.FromSeconds(5),TimeSpan.FromSeconds(15));});
}
