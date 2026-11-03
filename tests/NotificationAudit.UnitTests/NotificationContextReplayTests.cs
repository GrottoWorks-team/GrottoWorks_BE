using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NotificationAudit.Application.Events;
using NotificationAudit.Infrastructure.Data;
using NotificationAudit.Infrastructure.Events;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class NotificationContextReplayTests
{
    [Fact]
    public async Task Replay_preserves_identity_and_occurred_time_and_uses_the_same_ingestion_boundary()
    {
        var state = new State();
        var row = Row();
        var store = new EventStore(state);
        await new ReplayNotificationContextHandler(new IntegrationEventIngestionHandler(store)).HandleAsync(row, default);
        var plan = state.Plans.Should().ContainSingle().Which;
        plan.EventId.Should().Be(row.EventId);
        plan.Status.Should().Be("PROCESSED");
        plan.Notifications.Should().ContainSingle();
        plan.Audit.OccurredAt.Should().Be(DateTimeOffset.Parse("2026-11-03T01:00:00Z"));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task Invalid_retained_context_is_rejected_before_any_effect(string envelope)
    {
        var state = new State();
        var handler = new ReplayNotificationContextHandler(new IntegrationEventIngestionHandler(new EventStore(state)));
        Func<Task> replay = () => handler.HandleAsync(Row() with { SafeEnvelope = envelope }, default);
        await replay.Should().ThrowAsync<InvalidNotificationEventException>();
        state.Plans.Should().BeEmpty();
    }

    [Fact]
    public async Task One_transient_failure_does_not_reuse_its_tracked_scope_or_block_the_next_event()
    {
        var state = new State();
        state.Rows.AddRange([Row(), Row()]);
        state.FailEventId = state.Rows[0].EventId;
        using var provider = Services(state).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var worker = Worker(provider, 2);
        var cursor = await worker.ReplayPageAsync(DateTimeOffset.UtcNow, null, default);

        state.Plans.Should().ContainSingle().Which.EventId.Should().Be(state.Rows[1].EventId);
        state.DisposedEventStores.Should().Be(2);
        state.Quarantined.Should().BeEmpty();
        cursor!.EventId.Should().Be(state.Rows[1].EventId);
        state.FailEventId = null;
        await worker.ReplayPageAsync(DateTimeOffset.UtcNow, null, default);
        state.Plans.Should().Contain(plan => plan.EventId == state.Rows[0].EventId);
    }

    [Fact]
    public async Task Permanent_context_error_is_quarantined_and_does_not_stop_the_batch()
    {
        var state = new State();
        var bad = Row() with { SafeEnvelope = "invalid" };
        var valid = Row();
        state.Rows.AddRange([bad, valid]);
        using var provider = Services(state).BuildServiceProvider();
        var cursor = await Worker(provider, 100).ReplayPageAsync(DateTimeOffset.UtcNow, null, default);
        state.Quarantined.Should().Equal(bad.EventId);
        state.Plans.Should().ContainSingle().Which.EventId.Should().Be(valid.EventId);
        cursor.Should().BeNull();
    }

    [Fact]
    public async Task Shutdown_is_propagated_without_quarantine_or_processing_later_rows()
    {
        var state = new State();
        state.Rows.Add(Row());
        using var provider = Services(state).BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Func<Task> replay = () => Worker(provider, 100).ReplayPageAsync(DateTimeOffset.UtcNow, null, cancellation.Token);
        await replay.Should().ThrowAsync<OperationCanceledException>();
        state.Quarantined.Should().BeEmpty();
        state.Plans.Should().BeEmpty();
    }

    [Fact]
    public void Pending_page_query_translates_keyset_cursor_to_postgresql_without_offset_scanning()
    {
        using var db = new NotificationAuditDbContext(new DbContextOptionsBuilder<NotificationAuditDbContext>()
            .UseNpgsql("Host=localhost;Database=not_opened;Username=test").Options);
        var cutoff = DateTimeOffset.UtcNow;
        var sql = UnresolvedNotificationEventStore.BuildPageQuery(db, cutoff,
            new NotificationReplayCursor(cutoff.AddMinutes(-1), Guid.NewGuid()), 100).ToQueryString();
        sql.Should().Contain("REQUIRES_CONTEXT").And.Contain("ORDER BY").And.Contain("LIMIT").And.NotContain("OFFSET");
        sql.Should().Contain("received_at").And.Contain("event_id").And.Contain("safe_envelope");
    }

    private static NotificationContextReplayWorker Worker(ServiceProvider provider, int batchSize) => new(
        provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(new NotificationContextReplayOptions { BatchSize = batchSize }),
        TimeProvider.System, NullLogger<NotificationContextReplayWorker>.Instance);

    private static ServiceCollection Services(State state)
    {
        var services = new ServiceCollection();
        services.AddScoped<IUnresolvedNotificationEventStore>(_ => new PendingStore(state));
        services.AddScoped<INotificationEventStore>(_ => new EventStore(state));
        services.AddScoped<IntegrationEventIngestionHandler>();
        services.AddScoped<ReplayNotificationContextHandler>();
        return services;
    }

    private static RetainedNotificationEvent Row()
    {
        var message = new IncomingIntegrationEvent(Guid.NewGuid(), "task.assignment.created",
            DateTimeOffset.Parse("2026-11-03T01:00:00Z"), Guid.NewGuid().ToString(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "task", Guid.NewGuid(), JsonSerializer.SerializeToElement(new
            { volunteerUserId = Guid.NewGuid(), assignmentType = "DIRECT" }));
        return new RetainedNotificationEvent(message.EventId, message.EventType, message.OccurredAt,
            JsonSerializer.Serialize(message, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private sealed class State
    {
        public List<RetainedNotificationEvent> Rows { get; } = [];
        public List<EventIngestionPlan> Plans { get; } = [];
        public List<Guid> Quarantined { get; } = [];
        public Guid? FailEventId { get; set; }
        public int DisposedEventStores { get; set; }
    }

    private sealed class PendingStore(State state) : IUnresolvedNotificationEventStore
    {
        public Task<IReadOnlyList<RetainedNotificationEvent>> GetPageAsync(DateTimeOffset cutoff, NotificationReplayCursor? after,
            int size, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<RetainedNotificationEvent>>(state.Rows.Take(size).ToArray());
        }
        public Task QuarantineAsync(Guid eventId, CancellationToken cancellationToken)
        { state.Quarantined.Add(eventId); return Task.CompletedTask; }
    }

    private sealed class EventStore(State state) : INotificationEventStore, IDisposable
    {
        public Task StoreAsync(EventIngestionPlan plan, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (state.FailEventId == plan.EventId) throw new TimeoutException();
            state.Plans.Add(plan);
            return Task.CompletedTask;
        }
        public void Dispose() => state.DisposedEventStores++;
    }
}
