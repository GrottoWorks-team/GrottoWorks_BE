using System.Text.Json;
using Resource.Application.Purchases;
using Resource.Application.Scope;
using Resource.Domain;
using Resource.Domain.Purchases;
using Resource.Domain.Scope;
using TaskExecution.Contracts.Events;

namespace Resource.UnitTests.Scope;

/// <summary>TaskExecution outbox events keep task_projection current (status, accepted assignees, PURCHASE buyer).</summary>
public sealed class TaskEventTests
{
    private readonly FakeTaskProjectionStore _tasks = new();
    private readonly FakePurchaseRequestStore _requests = new();
    private readonly PurchaseRequest _request;
    private readonly TaskProjection _task;

    public TaskEventTests()
    {
        _request = PurchaseRequest.Create(Guid.NewGuid(), Guid.NewGuid(), "Thiếu dây",
            [new PurchaseRequestItemInput(Guid.NewGuid(), 50, 500_000m, null, null)], DateTimeOffset.UtcNow);
        _request.Submit(Guid.NewGuid(), DateTimeOffset.UtcNow);
        _request.Approve(Guid.NewGuid(), null, DateTimeOffset.UtcNow);
        _requests.Add(_request);
        _task = TaskProjection.FromSnapshot(new TaskSnapshot(Guid.NewGuid(), _request.WorkAreaId, "Mua dây", ProjectedTaskType.Purchase,
            ProjectedTaskStatus.Open, _request.PurchaseRequestId, [], 3), DateTimeOffset.UtcNow);
        _tasks.Add(_task);
    }

    private ApplyTaskEventHandler Handler() => new(_tasks, new PurchaseBuyerSync(_requests));

    private TaskIntegrationEvent Event(string type, object payload, long version, Guid? taskId = null) =>
        new(Guid.NewGuid(), type, 1, DateTimeOffset.UtcNow, "c", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "task", taskId ?? _task.TaskId,
            (int)version, JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    [Fact]
    public async Task Accepted_assignment_adds_the_buyer_and_marks_the_request()
    {
        var buyer = Guid.NewGuid();

        (await Handler().HandleAsync(Event(TaskEventTypes.AssignmentResponded,
            new { taskId = _task.TaskId, volunteerUserId = buyer, decision = "ACCEPTED" }, 4))).Should().BeTrue();

        _task.AssigneeUserIds.Should().Equal(buyer);
        _request.Status.Should().Be(PurchaseRequestStatus.BuyerAssigned);
    }

    [Fact]
    public async Task Declines_stale_versions_unknown_tasks_and_other_events_change_nothing()
    {
        var handler = Handler();
        (await handler.HandleAsync(Event(TaskEventTypes.AssignmentResponded, new { volunteerUserId = Guid.NewGuid(), decision = "DECLINED" }, 5))).Should().BeFalse();
        (await handler.HandleAsync(Event(TaskEventTypes.StatusChanged, new { status = "COMPLETED" }, 2))).Should().BeFalse();
        (await handler.HandleAsync(Event(TaskEventTypes.StatusChanged, new { status = "COMPLETED" }, 9, Guid.NewGuid()))).Should().BeFalse();
        (await handler.HandleAsync(Event(TaskEventTypes.HoursApproved, new { approvedMinutes = 60 }, 9))).Should().BeFalse();

        _task.Status.Should().Be(ProjectedTaskStatus.Open);
        _task.AssigneeUserIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Status_change_moves_the_task_and_bad_payloads_are_rejected()
    {
        (await Handler().HandleAsync(Event(TaskEventTypes.StatusChanged, new { status = "REVISION_REQUIRED" }, 4))).Should().BeTrue();
        _task.Status.Should().Be(ProjectedTaskStatus.RevisionRequired);
        _task.SourceVersion.Should().Be(4);

        var bad = () => Handler().HandleAsync(Event(TaskEventTypes.StatusChanged, new { status = "NOPE" }, 5));
        (await bad.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("EVENT_PAYLOAD_INVALID");
    }
}
