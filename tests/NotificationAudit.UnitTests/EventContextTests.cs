using System.Text.Json;
using FluentAssertions;
using NotificationAudit.Application.Events;
using NotificationAudit.Infrastructure.Events;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class EventContextTests
{
    [Fact]
    public async Task Pending_context_retains_only_typed_identifiers_and_known_states()
    {
        var task = Guid.NewGuid();
        using var payload = JsonDocument.Parse($$$"""
            {"taskId":"{{{task}}}","status":{"password":"secret"},"decision":"TOKEN_SECRET",
             "note":"private note","token":"secret","downloadUrl":"https://private.example/file"}
            """);
        var store = new Store();
        await new TaskEventIngestionHandler(store).HandleAsync(Message("attendance.session.open-too-long", payload.RootElement), default);
        store.Status.Should().Be("REQUIRES_CONTEXT");
        store.Context!.Payload.GetProperty("taskId").GetGuid().Should().Be(task);
        var json = store.Context.Payload.GetRawText();
        json.Should().NotContain("secret").And.NotContain("private").And.NotContain("decision");
        store.Context.OccurredAt.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Stored_jsonb_key_order_does_not_turn_a_retry_into_a_conflict()
    {
        var inbox = NotificationEventInbox.Create(Guid.NewGuid(), "task.status.changed", "REQUIRES_CONTEXT",
            DateTimeOffset.UtcNow, "{\"a\":1,\"b\":2}");
        inbox.EnsureSameContext("{\"b\":2, \"a\":1}").Should().BeFalse();
        Action changed = () => inbox.EnsureSameContext("{\"a\":9,\"b\":2}");
        changed.Should().Throw<InvalidNotificationEventException>();
    }

    [Fact]
    public void Resolving_a_pending_event_preserves_its_original_processed_time_on_replay()
    {
        var now = DateTimeOffset.UtcNow;
        var inbox = NotificationEventInbox.Create(Guid.NewGuid(), "task.status.changed", "REQUIRES_CONTEXT", now, "{}");
        inbox.MarkProcessed(now.AddMinutes(1));
        inbox.MarkProcessed(now.AddMinutes(2));
        inbox.Status.Should().Be("PROCESSED");
        inbox.ProcessedAt.Should().Be(now.AddMinutes(1));
    }

    [Fact]
    public async Task Unsupported_schema_is_rejected_before_storage()
    {
        var store = new Store();
        using var payload = JsonDocument.Parse("{}");
        Func<Task> ingest = () => new TaskEventIngestionHandler(store)
            .HandleAsync(Message("task.status.changed", payload.RootElement) with { SchemaVersion = 99 }, default);
        await ingest.Should().ThrowAsync<InvalidNotificationEventException>();
        store.Context.Should().BeNull();
    }

    private static IncomingTaskEvent Message(string type, JsonElement payload) => new(Guid.NewGuid(), type,
        new DateTimeOffset(2026, 11, 3, 7, 0, 0, TimeSpan.FromHours(7)), Guid.NewGuid().ToString(),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "task", Guid.NewGuid(), payload.Clone());
    private sealed class Store : INotificationEventStore
    {
        public IncomingTaskEvent? Context { get; private set; }
        public string? Status { get; private set; }
        public Task StoreAsync(EventIngestionPlan plan, CancellationToken cancellationToken)
        { Context = plan.SafeContext; Status = plan.Status; return Task.CompletedTask; }
    }
}
