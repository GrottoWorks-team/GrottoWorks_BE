using System.Text.Json;
using FluentAssertions;
using NotificationAudit.Application.Events;
using NotificationAudit.Application.Notifications;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class SupportNotificationTests
{
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
