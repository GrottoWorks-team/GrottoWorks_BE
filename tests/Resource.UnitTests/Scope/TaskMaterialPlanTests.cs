using System.Text.Json;
using Resource.Application.Scope;
using Resource.Domain.Scope;
using TaskExecution.Contracts.Events;

namespace Resource.UnitTests.Scope;

public sealed class TaskMaterialPlanTests
{
    [Fact]
    public async Task Material_events_are_idempotent_and_stale_events_do_not_restore_cleared_items()
    {
        var store = new FakeTaskMaterialPlanStore();
        var inbox = new Inbox();
        var handler = new TaskMaterialEventIngestion(inbox, new(store));
        var message = Message();
        await handler.HandleAsync(message, default);
        await handler.HandleAsync(message, default);
        store.Rows.Should().ContainSingle().Which.Items.Should().ContainSingle();
        store.Saves.Should().Be(1);
        var newer = message with { EventId = Guid.NewGuid(), AggregateVersion = 3,
            Payload = JsonSerializer.SerializeToElement(new { taskId = message.AggregateId,
                workAreaId = store.Rows.Single().WorkAreaId, materials = Array.Empty<TaskMaterialSnapshot>() }) };
        await handler.HandleAsync(newer, default);
        await handler.HandleAsync(message with { EventId = Guid.NewGuid() }, default);
        store.Rows.Single().SourceVersion.Should().Be(3);
        store.Rows.Single().Items.Should().BeEmpty();
        store.Saves.Should().Be(2);
    }

    [Fact]
    public void Projection_cannot_move_to_another_tenant_or_work_area()
    {
        var task = Guid.NewGuid();
        var area = Guid.NewGuid();
        var parish = Guid.NewGuid();
        var season = Guid.NewGuid();
        var plan = TaskMaterialPlan.Create(task, area, parish, season, 1, [], DateTimeOffset.UtcNow);
        Action update = () => plan.Apply(Guid.NewGuid(), parish, season, 2, [], DateTimeOffset.UtcNow);
        update.Should().Throw<FormatException>();
        plan.SourceVersion.Should().Be(1);
    }

    [Fact]
    public async Task Invalid_source_payload_does_not_complete_the_inbox()
    {
        var inbox = new Inbox();
        var store = new FakeTaskMaterialPlanStore();
        var handler = new TaskMaterialEventIngestion(inbox, new(store));
        var message = Message();
        var invalid = message with { Payload = JsonSerializer.SerializeToElement(new
        {
            taskId = message.AggregateId, workAreaId = Guid.NewGuid(),
            materials = new[] { new TaskMaterialSnapshot(Guid.NewGuid(), -1, null) }
        }) };
        Func<Task> apply = () => handler.HandleAsync(invalid, default);
        await apply.Should().ThrowAsync<FormatException>();
        inbox.Applied.Should().Be(0);
        store.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Full_snapshot_bootstraps_materials_and_legacy_null_does_not_clear_them()
    {
        var season = new TaskSeasonSnapshot(Guid.NewGuid(), Guid.NewGuid(), new(2026, 10, 1), new(2027, 1, 15), "ACTIVE", 1);
        var area = new TaskWorkAreaSnapshot(Guid.NewGuid(), season.SeasonId, season.ParishId, "Area", null,
            Guid.NewGuid(), Guid.NewGuid(), null, null, "PLANNED", 1);
        var snapshot = new TaskScopeSnapshot(Guid.NewGuid(), 1, DateTimeOffset.UtcNow, Guid.NewGuid(), area.WorkAreaId,
            "Task", "WORK", "OPEN", null, [], 2, season, area, [new(Guid.NewGuid(), 4, null)]);
        var scopes = new FakeScopeStore();
        var tasks = new FakeTaskProjectionStore();
        var plans = new FakeTaskMaterialPlanStore();
        var handler = new ScopeReplicationHandler(new Inbox(), new(scopes), new(scopes),
            new(tasks, scopes, new(new FakePurchaseRequestStore())), new(plans));
        await handler.ApplyAsync(snapshot, default);
        await handler.ApplyAsync(snapshot with { EventId = Guid.NewGuid(), SourceVersion = 3, Materials = null }, default);
        plans.Rows.Single().Items.Single().RequiredQuantity.Should().Be(4);
        plans.Rows.Single().SourceVersion.Should().Be(2);
        await handler.ApplyAsync(snapshot with { EventId = Guid.NewGuid(), SourceVersion = 4, Materials = [] }, default);
        plans.Rows.Single().Items.Should().BeEmpty();
    }

    private static TaskIntegrationEvent Message()
    {
        var task = Guid.NewGuid();
        return new(Guid.NewGuid(), TaskEventTypes.MaterialsChanged, 1, DateTimeOffset.UtcNow, "materials-test", Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), "task", task, 2, JsonSerializer.SerializeToElement(new
            {
                taskId = task, workAreaId = Guid.NewGuid(), materials = new[] { new TaskMaterialSnapshot(Guid.NewGuid(), 2.5m, "note") }
            }));
    }

    private sealed class Inbox : IScopeReplicationInbox
    {
        private readonly HashSet<(string, Guid)> seen = [];
        public int Applied { get; private set; }
        public async Task ExecuteAsync(string consumer, Guid eventId, Guid seasonId, Func<Task> apply, CancellationToken cancellationToken)
        {
            if (seen.Contains((consumer, eventId))) return;
            await apply();
            seen.Add((consumer, eventId));
            Applied++;
        }
    }
}

internal sealed class FakeTaskMaterialPlanStore : ITaskMaterialPlanStore
{
    public List<TaskMaterialPlan> Rows { get; } = [];
    public int Saves { get; private set; }
    public Task<TaskMaterialPlan?> GetAsync(Guid taskId, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.SingleOrDefault(row => row.TaskId == taskId));
    public void Add(TaskMaterialPlan plan) => Rows.Add(plan);
    public Task SaveAsync(CancellationToken cancellationToken) { Saves++; return Task.CompletedTask; }
}
