using System.Text.Json;
using NotificationAudit.Application.Events;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class CompleteWorkflowNotificationTests
{
    [Fact]
    public async Task Resolved_audience_does_not_rewrite_the_immutable_source_context()
    {
        var store=new Store();var recipient=Guid.NewGuid();
        var message=new IncomingIntegrationEvent(Guid.NewGuid(),"planning.season.completed",DateTimeOffset.UtcNow,"correlation",
            Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"season",Guid.NewGuid(),JsonSerializer.SerializeToElement(new {}));
        await new IntegrationEventIngestionHandler(store,new Audience(recipient)).HandleAsync(message,default);
        Assert.Equal(recipient,Assert.Single(store.Plan!.Notifications).RecipientUserId);
        Assert.False(store.Plan.SafeContext!.Payload.TryGetProperty("recipientUserIds",out _));
        Assert.Equal("PROCESSED",store.Plan.Status);
    }
    private sealed class Store : INotificationEventStore
    {
        public EventIngestionPlan? Plan {get;private set;}
        public Task StoreAsync(EventIngestionPlan plan,CancellationToken ct){Plan=plan;return Task.CompletedTask;}
    }
    private sealed class Audience(Guid recipient) : INotificationAudienceResolver
    {
        public Task<IReadOnlyList<Guid>?> ResolveAsync(IncomingIntegrationEvent message,CancellationToken ct)=>
            Task.FromResult<IReadOnlyList<Guid>?>(new[] {recipient});
    }
    [Theory]
    [InlineData("planning.season.activated")][InlineData("task.progress.submitted")][InlineData("task.completion.reviewed")]
    public void Explicit_recipient_snapshots_are_deduplicated_without_guessing_other_users(string type)
    {
        var recipient=Guid.NewGuid();var aggregate=Guid.NewGuid();
        var message=new IncomingIntegrationEvent(Guid.NewGuid(),type,DateTimeOffset.UtcNow,"correlation",Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
            "task",aggregate,JsonSerializer.SerializeToElement(new {recipientUserIds=new[] {recipient,recipient},status="COMPLETED"}));
        var candidates=IntegrationEventIngestionHandler.MapNotifications(message,out var pending);
        Assert.False(pending);Assert.Single(candidates);Assert.Equal(recipient,candidates[0].RecipientUserId);
    }
    [Fact]
    public void Missing_recipient_context_is_retained_instead_of_broadcasting()
    {
        var message=new IncomingIntegrationEvent(Guid.NewGuid(),"planning.season.completed",DateTimeOffset.UtcNow,"correlation",Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
            "season",Guid.NewGuid(),JsonSerializer.SerializeToElement(new {}));
        Assert.Empty(IntegrationEventIngestionHandler.MapNotifications(message,out var pending));Assert.True(pending);
    }
}
