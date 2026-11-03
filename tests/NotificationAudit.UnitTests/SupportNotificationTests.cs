using System.Text.Json;
using FluentAssertions;
using NotificationAudit.Application.Events;
using NotificationAudit.Application.Notifications;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class SupportNotificationTests
{
    [Theory]
    [InlineData("support.material.approved","APPROVED","Material support approved")]
    [InlineData("support.material.reviewed","REJECTED","Material support rejected")]
    public async Task Material_decision_notifies_the_source_officer_and_retains_only_safe_context(string eventType,string status,string title)
    {
        var officer=Guid.NewGuid();var request=Guid.NewGuid();var item=Guid.NewGuid();var operation=Guid.NewGuid();var store=new Store();
        await new IntegrationEventIngestionHandler(store).HandleAsync(Message(eventType,new
        {
            supportRequestId=request,supportResponseMaterialId=item,approvalOperationId=operation,materialOfficerUserId=officer,
            status,approvedQuantity=5,token="secret",privateUrl="http://private/receipt"
        }),default);
        var notification=store.Plan!.Notifications.Should().ContainSingle().Which;
        notification.RecipientUserId.Should().Be(officer);notification.ReferenceId.Should().Be(request);notification.Title.Should().Be(title);
        var safe=store.Plan.SafeContext!.Payload;
        safe.GetProperty("approvalOperationId").GetGuid().Should().Be(operation);safe.GetProperty("approvedQuantity").GetDecimal().Should().Be(5);
        safe.GetRawText().Should().NotContain("secret").And.NotContain("private");
        await new IntegrationEventIngestionHandler(store).HandleAsync(store.Plan.SafeContext,default);
        store.Plan.Status.Should().Be("PROCESSED");store.Plan.Notifications.Should().ContainSingle();
    }

    [Fact]
    public async Task Durable_stock_failure_is_audited_without_a_false_approval_or_rejection_notification()
    {
        var store=new Store();
        await new IntegrationEventIngestionHandler(store).HandleAsync(Message("support.material.reviewed",new
        {supportRequestId=Guid.NewGuid(),status="PENDING",errorCode="MATERIAL_INSUFFICIENT_AVAILABLE"}),default);
        store.Plan!.Status.Should().Be("PROCESSED");store.Plan.Notifications.Should().BeEmpty();
        store.Plan.Audit.SafeNewValue.RootElement.GetProperty("errorCode").GetString().Should().Be("MATERIAL_INSUFFICIENT_AVAILABLE");
    }

    [Fact]
    public async Task Missing_material_recipient_stays_pending_and_invalid_quantities_are_rejected()
    {
        var store=new Store();var handler=new IntegrationEventIngestionHandler(store);
        await handler.HandleAsync(Message("support.material.approved",new {supportRequestId=Guid.NewGuid(),status="APPROVED",approvedQuantity=5}),default);
        store.Plan!.Status.Should().Be("REQUIRES_CONTEXT");
        Func<Task> invalid=()=>handler.HandleAsync(Message("support.material.approved",new {status="APPROVED",approvedQuantity=-1}),default);
        await invalid.Should().ThrowAsync<InvalidNotificationEventException>();
        NotificationRuleCatalog.All[Guid.Parse("71000000-0000-0000-0000-000000000028")].Should().Be("support.material.approved");
    }
    [Fact]
    public async Task Community_approval_requests_consent_and_retains_the_flag_for_replay()
    {
        var store=new Store();
        await new IntegrationEventIngestionHandler(store).HandleAsync(Message("support.member.reviewed",new
        {supportRequestId=Guid.NewGuid(),volunteerUserId=Guid.NewGuid(),decision="APPROVED",requiresConfirmation=true}),default);
        store.Plan!.Notifications.Should().ContainSingle().Which.Title.Should().Be("Support nomination awaiting your response");
        store.Plan.SafeContext!.Payload.GetProperty("requiresConfirmation").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Declined_nomination_notifies_the_leader_without_a_second_approval_notification()
    {
        var leader=Guid.NewGuid();
        var candidates=IntegrationEventIngestionHandler.MapNotifications(Message("task.assignment.responded",new
        {taskId=Guid.NewGuid(),assignmentType="SUPPORT",decision="DECLINED",leaderUserId=leader}),out var pending);
        pending.Should().BeFalse();candidates.Should().ContainSingle().Which.RecipientUserId.Should().Be(leader);
    }
    [Theory]
    [InlineData("APPROVED","Support participation approved")]
    [InlineData("REJECTED","Support participation rejected")]
    public void Decision_notifies_the_volunteer_and_links_the_request(string decision,string title)
    {
        var volunteer=Guid.NewGuid();var request=Guid.NewGuid();
        var candidates=IntegrationEventIngestionHandler.MapNotifications(Message("support.member.reviewed",new
        {supportRequestId=request,volunteerUserId=volunteer,decision}),out var pending);
        pending.Should().BeFalse();
        var candidate=candidates.Should().ContainSingle().Which;
        candidate.RecipientUserId.Should().Be(volunteer);candidate.ReferenceId.Should().Be(request);
        candidate.Title.Should().Be(title);
    }

    [Fact]
    public void The_assignment_fact_does_not_duplicate_the_support_decision_notification()
    {
        var candidates=IntegrationEventIngestionHandler.MapNotifications(Message("task.assignment.created",new
        {assignmentType="SUPPORT",status="ACCEPTED",volunteerUserId=Guid.NewGuid()}),out var pending);
        pending.Should().BeFalse();candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task Publication_retains_matching_requirements_but_does_not_fabricate_a_recipient()
    {
        var skill=Guid.NewGuid();var task=Guid.NewGuid();var store=new Store();
        var message=Message("support.request.published",new
        {
            supportRequestId=Guid.NewGuid(),supportType="STAFFING",note="private note",token="secret",
            positions=new[] {new {positionId=Guid.NewGuid(),taskId=task,requiredVolunteerCount=2,
                name="private name",skills=new[] {new {skillId=skill,requiredSkillLevel="ADVANCED"}}}},materials=Array.Empty<object>()
        });
        await new IntegrationEventIngestionHandler(store).HandleAsync(message,default);
        store.Plan!.Status.Should().Be("REQUIRES_CONTEXT");store.Plan.Notifications.Should().BeEmpty();
        var safe=store.Plan.SafeContext!.Payload;
        safe.GetProperty("positions")[0].GetProperty("taskId").GetGuid().Should().Be(task);
        safe.GetProperty("positions")[0].GetProperty("skills")[0].GetProperty("requiredSkillLevel").GetString().Should().Be("ADVANCED");
        safe.GetRawText().Should().NotContain("private").And.NotContain("secret");
        await new IntegrationEventIngestionHandler(store).HandleAsync(store.Plan.SafeContext,default);
        store.Plan.Status.Should().Be("REQUIRES_CONTEXT");
    }

    [Fact]
    public async Task Invalid_publication_schema_is_rejected_instead_of_retaining_incomplete_requirements()
    {
        var store=new Store();
        Func<Task> consume=()=>new IntegrationEventIngestionHandler(store).HandleAsync(Message("support.request.published",new
        {supportType="STAFFING",positions=Array.Empty<object>(),materials=Array.Empty<object>()}),default);
        await consume.Should().ThrowAsync<InvalidNotificationEventException>();store.Plan.Should().BeNull();
    }

    [Fact]
    public void New_rule_ids_are_stable()
    {
        NotificationRuleCatalog.All[Guid.Parse("71000000-0000-0000-0000-000000000026")].Should().Be("support.member.reviewed");
        NotificationRuleCatalog.All[Guid.Parse("71000000-0000-0000-0000-000000000027")].Should().Be("support.request.published");
    }

    private static IncomingIntegrationEvent Message(string type,object payload)=>new(Guid.NewGuid(),type,DateTimeOffset.UtcNow,
        Guid.NewGuid().ToString(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"support_request",Guid.NewGuid(),JsonSerializer.SerializeToElement(payload));
    private sealed class Store : INotificationEventStore
    {
        public EventIngestionPlan? Plan {get;private set;}
        public Task StoreAsync(EventIngestionPlan plan,CancellationToken cancellationToken) {Plan=plan;return Task.CompletedTask;}
    }
}
