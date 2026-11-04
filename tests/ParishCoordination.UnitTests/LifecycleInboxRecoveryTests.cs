using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ParishCoordination.Application.WorkAreas;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;
using ParishCoordination.Infrastructure.Data;
using ParishCoordination.Infrastructure.Lifecycle;
using Readiness.Contracts.Events;
using TaskExecution.Contracts.Events;

namespace ParishCoordination.UnitTests;

// InMemory tests verify ingestion decisions, not PostgreSQL transaction/lock behavior.
public sealed class LifecycleInboxRecoveryTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Snapshot_replay_after_jsonb_reformat_preserves_inbox_and_recovers_pending_parents(bool persistParents)
    {
        using var fixture = await Fixture.CreateAsync(persistParents);
        var message = fixture.Snapshot(Guid.NewGuid(), 1, "IN_PROGRESS");
        await fixture.Processor.ReceiveAsync(message, default);
        var stored = await fixture.Db.Set<LifecycleInboxMessage>().SingleAsync();
        var originalEnvelope = stored.Envelope;
        stored.Envelope = ReformatJsonb(stored.Envelope);
        stored.Envelope.Should().NotBe(originalEnvelope);
        var persistedEnvelope = stored.Envelope;
        var receivedAt = stored.ReceivedAt;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear(); // A new delivery reads the database's representation.
        if (!persistParents) await fixture.PersistParentsAsync();
        var version = (await fixture.Db.WorkAreas.SingleAsync()).Version;
        var outboxCount = await fixture.Db.PlanningEventOutbox.CountAsync();

        await fixture.Processor.ReceiveAsync(message, default);

        var inbox = await fixture.Db.Set<LifecycleInboxMessage>().SingleAsync();
        inbox.ProcessedAt.Should().NotBeNull();
        inbox.Envelope.Should().Be(persistedEnvelope);
        inbox.ReceivedAt.Should().Be(receivedAt);
        (await fixture.Db.Set<LifecycleTaskProjection>().SingleAsync()).Status.Should().Be("IN_PROGRESS");
        if (persistParents)
        {
            (await fixture.Db.WorkAreas.SingleAsync()).Version.Should().Be(version);
            (await fixture.Db.PlanningEventOutbox.CountAsync()).Should().Be(outboxCount);
        }
    }

    [Theory]
    [InlineData("status")]
    [InlineData("capturedAt")]
    [InlineData("parent")]
    [InlineData("assignees")]
    [InlineData("assigneeOrder")]
    [InlineData("materials")]
    [InlineData("extraField")]
    public async Task Same_event_id_with_changed_content_is_still_rejected_after_jsonb_reformat(string changed)
    {
        using var fixture = await Fixture.CreateAsync();
        var message = fixture.Snapshot(Guid.NewGuid(), 1, "IN_PROGRESS") with
            { AssigneeUserIds = [Guid.NewGuid(), Guid.NewGuid()] };
        await fixture.Processor.ReceiveAsync(message, default);
        var stored = await fixture.Db.Set<LifecycleInboxMessage>().SingleAsync();
        stored.Envelope = ReformatJsonb(stored.Envelope);
        if (changed == "extraField")
        {
            var json = JsonNode.Parse(stored.Envelope)!.AsObject();
            json["futureContext"] = "must remain immutable";
            stored.Envelope = json.ToJsonString();
        }
        var originalEnvelope = stored.Envelope;
        var processedAt = stored.ProcessedAt;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var version = (await fixture.Db.WorkAreas.SingleAsync()).Version;
        var outboxCount = await fixture.Db.PlanningEventOutbox.CountAsync();
        var altered = changed switch
        {
            "status" => message with { Status = "COMPLETED" },
            "capturedAt" => message with { CapturedAt = message.CapturedAt.AddSeconds(1) },
            "parent" => message with { WorkArea = message.WorkArea with { Name = "Changed parent" } },
            "assignees" => message with { AssigneeUserIds = [Guid.NewGuid()] },
            "assigneeOrder" => message with { AssigneeUserIds = message.AssigneeUserIds.Reverse().ToArray() },
            "materials" => message with { Materials = [] }, // Missing and empty are different contracts.
            _ => message
        };

        var consume = () => fixture.Processor.ReceiveAsync(altered, default);

        await consume.Should().ThrowAsync<FormatException>().WithMessage("An event ID was reused for a different lifecycle message.");
        var inbox = await fixture.Db.Set<LifecycleInboxMessage>().SingleAsync();
        inbox.Envelope.Should().Be(originalEnvelope);
        inbox.ProcessedAt.Should().Be(processedAt);
        (await fixture.Db.Set<LifecycleTaskProjection>().SingleAsync()).Status.Should().Be("IN_PROGRESS");
        (await fixture.Db.WorkAreas.SingleAsync()).Version.Should().Be(version);
        (await fixture.Db.PlanningEventOutbox.CountAsync()).Should().Be(outboxCount);
    }

    // Simulate jsonb's formatting/key-order rewrite at every object depth; preserve values and array order.
    private static string ReformatJsonb(string envelope)
    {
        static JsonNode? Reorder(JsonNode? node) => node switch
        {
            JsonObject obj => new JsonObject(obj.OrderBy(pair => pair.Key.Length).ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, Reorder(pair.Value)))),
            JsonArray array => new JsonArray(array.Select(Reorder).ToArray()),
            _ => node?.DeepClone()
        };
        return Reorder(JsonNode.Parse(envelope))!.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    [Fact]
    public async Task Duplicate_and_stale_snapshots_do_not_regress_state_or_add_outbox_versions()
    {
        using var fixture = await Fixture.CreateAsync();
        var latest = fixture.Snapshot(Guid.NewGuid(), 5, "COMPLETED");
        await fixture.Processor.ReceiveAsync(latest, default);
        var version = fixture.Area.Version;
        var count = await fixture.Db.PlanningEventOutbox.CountAsync();
        await fixture.Processor.ReceiveAsync(latest, default);
        await fixture.Processor.ReceiveAsync(fixture.Snapshot(latest.TaskId, 3, "IN_PROGRESS"), default);
        var task = await fixture.Db.Set<LifecycleTaskProjection>().SingleAsync();
        task.SourceVersion.Should().Be(5);
        task.Status.Should().Be("COMPLETED");
        fixture.Area.Status.Should().Be(WorkAreaStatus.ReadinessReview);
        fixture.Area.Version.Should().Be(version);
        (await fixture.Db.PlanningEventOutbox.CountAsync()).Should().Be(count);
    }

    [Fact]
    public async Task Missing_parent_is_retained_and_recovered_after_parent_arrives()
    {
        using var fixture = await Fixture.CreateAsync(persistParents: false);
        var message = fixture.Snapshot(Guid.NewGuid(), 1, "IN_PROGRESS");
        await fixture.Processor.ReceiveAsync(message, default);
        (await fixture.Db.Set<LifecycleInboxMessage>().SingleAsync()).ProcessedAt.Should().BeNull();
        await fixture.PersistParentsAsync();
        await fixture.Processor.RecoverAreaAsync(fixture.Area.Id, default);
        (await fixture.Db.Set<LifecycleInboxMessage>().SingleAsync()).ProcessedAt.Should().NotBeNull();
        fixture.Area.Status.Should().Be(WorkAreaStatus.InProgress);
    }

    [Fact]
    public async Task Partial_backfill_cannot_enter_review_when_source_contains_another_open_task()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Counts.Total = 2;
        fixture.Counts.Completed = 1;
        await fixture.Processor.ReceiveAsync(fixture.Snapshot(Guid.NewGuid(), 4, "COMPLETED"), default);
        fixture.Area.Status.Should().Be(WorkAreaStatus.InProgress);
        var secondId = Guid.NewGuid();
        await fixture.Processor.ReceiveAsync(fixture.Snapshot(secondId, 1, "OPEN"), default);
        fixture.Area.Status.Should().Be(WorkAreaStatus.InProgress);
        fixture.Counts.Completed = 2;
        await fixture.Processor.ReceiveAsync(fixture.Snapshot(secondId, 3, "COMPLETED"), default);
        fixture.Area.Status.Should().Be(WorkAreaStatus.ReadinessReview);
    }

    [Fact]
    public async Task Readiness_reopen_and_corrective_work_require_a_new_confirmation()
    {
        using var fixture = await Fixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        await fixture.Processor.ReceiveAsync(fixture.Snapshot(Guid.NewGuid(), 5, "COMPLETED"), default);
        await fixture.Processor.ReceiveAsync(fixture.Ready(now), default);
        fixture.Area.Status.Should().Be(WorkAreaStatus.Ready);
        fixture.Season.Status.Should().Be(SeasonStatus.Ready);
        await fixture.Processor.ReceiveAsync(fixture.Reopened(now.AddSeconds(2)), default);
        fixture.Area.Status.Should().Be(WorkAreaStatus.NeedsCorrection);
        fixture.Season.Status.Should().Be(SeasonStatus.Active);
        await fixture.Processor.ReceiveAsync(fixture.Ready(now.AddSeconds(1)), default);
        fixture.Area.Status.Should().Be(WorkAreaStatus.NeedsCorrection);
        fixture.Counts.Total = 2;
        var correctiveId = Guid.NewGuid();
        await fixture.Processor.ReceiveAsync(fixture.Snapshot(correctiveId, 1, "OPEN", "CORRECTIVE"), default);
        fixture.Area.Status.Should().Be(WorkAreaStatus.InProgress);
        fixture.Counts.Completed = 2;
        await fixture.Processor.ReceiveAsync(fixture.Snapshot(correctiveId, 4, "COMPLETED", "CORRECTIVE"), default);
        fixture.Area.Status.Should().Be(WorkAreaStatus.ReadinessReview);
        await fixture.Processor.ReceiveAsync(fixture.Ready(now.AddSeconds(3)), default);
        fixture.Area.Status.Should().Be(WorkAreaStatus.Ready);
    }

    [Fact]
    public async Task Count_dependency_failure_retains_task_projection_for_later_recovery()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Counts.Unavailable = true;
        await fixture.Processor.ReceiveAsync(fixture.Snapshot(Guid.NewGuid(), 1, "COMPLETED"), default);
        (await fixture.Db.Set<LifecycleTaskProjection>().CountAsync()).Should().Be(1);
        fixture.Area.Status.Should().NotBe(WorkAreaStatus.Ready);
        fixture.Counts.Unavailable = false;
        await fixture.Processor.RecoverAreaAsync(fixture.Area.Id, default);
        fixture.Area.Status.Should().Be(WorkAreaStatus.ReadinessReview);
    }

    [Fact]
    public async Task Archived_season_ignores_late_lifecycle_events_without_mutating_domain_state()
    {
        using var fixture = await Fixture.CreateAsync(status: SeasonStatus.Archived);
        await fixture.Processor.ReceiveAsync(fixture.Snapshot(Guid.NewGuid(), 1, "IN_PROGRESS"), default);
        fixture.Area.Version.Should().Be(1);
        fixture.Season.Status.Should().Be(SeasonStatus.Archived);
        (await fixture.Db.Set<LifecycleInboxMessage>().SingleAsync()).ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Task_status_receipt_and_same_version_snapshot_share_one_projection()
    {
        using var fixture = await Fixture.CreateAsync();
        var taskId = Guid.NewGuid();
        var message = new TaskIntegrationEvent(Guid.NewGuid(), TaskEventTypes.StatusChanged, 1, DateTimeOffset.UtcNow,
            "correlation", Guid.NewGuid(), fixture.Season.ParishId, fixture.Season.Id, "task", taskId, 3,
            JsonSerializer.SerializeToElement(new { taskId, workAreaId = fixture.Area.Id, status = "IN_PROGRESS" }));
        await fixture.Processor.ReceiveAsync(message, default);
        await fixture.Processor.ReceiveAsync(fixture.Snapshot(taskId, 3, "IN_PROGRESS", "CORRECTIVE"), default);
        var projection = await fixture.Db.Set<LifecycleTaskProjection>().SingleAsync();
        projection.TaskType.Should().Be("CORRECTIVE");
        fixture.Area.Status.Should().Be(WorkAreaStatus.InProgress);
    }

    [Fact]
    public async Task Malformed_schema_is_rejected_before_acknowledging_or_recording_inbox()
    {
        using var fixture = await Fixture.CreateAsync();
        var invalid = fixture.Snapshot(Guid.NewGuid(), 1, "COMPLETED") with { SchemaVersion = 900 };
        var consume = () => fixture.Processor.ReceiveAsync(invalid, default);
        await consume.Should().ThrowAsync<FormatException>();
        (await fixture.Db.Set<LifecycleInboxMessage>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Snapshot_with_optional_deadline_remains_compatible()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.Processor.ReceiveAsync(fixture.Snapshot(Guid.NewGuid(), 1, "COMPLETED") with
            { Deadline = DateTimeOffset.UtcNow.AddDays(-1) }, default);
        fixture.Area.Status.Should().Be(WorkAreaStatus.ReadinessReview);
    }

    private sealed class Fixture : IDisposable
    {
        public ParishCoordinationDbContext Db { get; } = new(new DbContextOptionsBuilder<ParishCoordinationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        public Season Season { get; private init; } = null!;
        public WorkArea Area { get; private init; } = null!;
        public CountClient Counts { get; } = new();
        public LifecycleEventProcessor Processor => new(Db, Counts, TimeProvider.System);
        private readonly Guid checklistId = Guid.NewGuid();

        public static async Task<Fixture> CreateAsync(bool persistParents = true, SeasonStatus status = SeasonStatus.Planning)
        {
            var season = SeasonReadinessAggregationTests.Season(status);
            var fixture = new Fixture { Season = season, Area = SeasonReadinessAggregationTests.Area(season.Id) };
            if (persistParents) await fixture.PersistParentsAsync();
            return fixture;
        }

        public async Task PersistParentsAsync()
        {
            Db.AddRange(Season, Area);
            await Db.SaveChangesAsync();
            if (Season.Status == SeasonStatus.Planning)
            {
                Season.Activate(DateTimeOffset.UtcNow);
                await Db.SaveChangesAsync();
            }
        }

        public TaskScopeSnapshot Snapshot(Guid taskId, long version, string status, string type = "WORK") => new(
            Guid.NewGuid(), 1, DateTimeOffset.UtcNow, taskId, Area.Id, "Task", type, status, null, [], version,
            new(Season.Id, Season.ParishId, Season.StartDate, Season.EndDate, "ACTIVE", Season.Version),
            new(Area.Id, Season.Id, Season.ParishId, Area.Name, null, Area.LeaderUserId, Area.MaterialOfficerUserId,
                null, null, "PLANNED", Area.Version));

        public ReadinessIntegrationEvent Ready(DateTimeOffset now) => Readiness(ReadinessAreaReady.EventType,
            JsonSerializer.SerializeToElement(new ReadinessAreaReady(checklistId, Area.Id, Season.Id, Season.ParishId,
                Guid.NewGuid(), now), new JsonSerializerOptions(JsonSerializerDefaults.Web)), now);
        public ReadinessIntegrationEvent Reopened(DateTimeOffset now) => Readiness(ReadinessAreaReopened.EventType,
            JsonSerializer.SerializeToElement(new ReadinessAreaReopened(checklistId, Area.Id, Season.Id, Season.ParishId,
                "Needs repair", Guid.NewGuid(), now), new JsonSerializerOptions(JsonSerializerDefaults.Web)), now);
        private ReadinessIntegrationEvent Readiness(string type, JsonElement payload, DateTimeOffset now) => new(
            Guid.NewGuid(), type, 1, now, "correlation", Guid.NewGuid(), Season.ParishId, Season.Id, "readiness_checklist", checklistId, payload);
        public void Dispose() => Db.Dispose();
    }

    private sealed class CountClient : IWorkAreaTaskCountClient
    {
        public long Total { get; set; } = 1;
        public long Completed { get; set; } = 1;
        public bool Unavailable { get; set; }
        public Task<WorkAreaTaskCounts> GetAsync(Guid areaId, CancellationToken cancellationToken = default) =>
            Unavailable ? throw new WorkAreaTaskCountUnavailableException("Dependency unavailable") : Task.FromResult(
                new WorkAreaTaskCounts(areaId, Total, 0, Total, Completed, 0, DateTimeOffset.UtcNow));
    }
}
