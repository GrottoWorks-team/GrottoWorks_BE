using System.Text.Json;
using Resource.Application.Purchases;
using Resource.Application.Scope;
using Resource.Contracts.Events;
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

    private readonly FakeInventoryStore _inventory = new(new FakeRequirementStore());
    private readonly FakeScopeStore _scopes = new();
    private readonly FakeIntegrationEventPublisher _events = new();

    private ApplyTaskEventHandler Handler() =>
        new(_tasks, new PurchaseBuyerSync(_requests), new FakeAllocationStore(_inventory), _scopes, _events, new FakeUnitOfWork());

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
    public async Task Cancelling_the_purchase_task_returns_the_request_to_approved()
    {
        // 12/11/2026: huỷ việc mua ⇒ đơn về lại APPROVED để trưởng khu tạo được việc mua thay thế
        // (trước đây đơn kẹt ở BUYER_ASSIGNED và POST /purchase-tasks trả PURCHASE_REQUEST_NOT_APPROVED).
        (await Handler().HandleAsync(Event(TaskEventTypes.AssignmentResponded,
            new { taskId = _task.TaskId, volunteerUserId = Guid.NewGuid(), decision = "ACCEPTED" }, 4))).Should().BeTrue();
        _request.Status.Should().Be(PurchaseRequestStatus.BuyerAssigned);

        (await Handler().HandleAsync(Event(TaskEventTypes.StatusChanged, new { status = "CANCELLED" }, 5))).Should().BeTrue();

        _task.Status.Should().Be(ProjectedTaskStatus.Cancelled);
        _request.Status.Should().Be(PurchaseRequestStatus.Approved);
    }

    [Fact]
    public async Task Cancelling_a_purchase_task_after_a_recorded_purchase_keeps_the_request_status()
    {
        // Đã ghi nhận mua (PURCHASED) thì huỷ việc không được kéo đơn về APPROVED.
        (await Handler().HandleAsync(Event(TaskEventTypes.AssignmentResponded,
            new { taskId = _task.TaskId, volunteerUserId = Guid.NewGuid(), decision = "ACCEPTED" }, 4))).Should().BeTrue();
        _request.MarkPurchased(Guid.NewGuid(), DateTimeOffset.UtcNow);

        await Handler().HandleAsync(Event(TaskEventTypes.StatusChanged, new { status = "CANCELLED" }, 5));

        _request.Status.Should().Be(PurchaseRequestStatus.Purchased);
    }

    [Fact]
    public async Task Cancelling_a_task_that_holds_materials_asks_the_officer_to_recall_them_once()
    {
        var officer = Guid.NewGuid();
        var area = _scopes.Seed(Guid.NewGuid(), officer);
        var task = TaskProjection.FromSnapshot(new TaskSnapshot(Guid.NewGuid(), area.WorkAreaId, "Dựng hang", ProjectedTaskType.Work,
            ProjectedTaskStatus.InProgress, null, [], 1), DateTimeOffset.UtcNow);
        _tasks.Add(task);
        var materialId = Guid.NewGuid();
        var allocation = Resource.Domain.Inventory.MaterialAllocation.Create(area.WorkAreaId, task.TaskId,
            [new Resource.Domain.Inventory.AllocationLineInput(materialId, 12)], DateTimeOffset.UtcNow, null, officer, DateTimeOffset.UtcNow);
        _inventory.Allocations.Add(allocation);

        (await Handler().HandleAsync(Event(TaskEventTypes.StatusChanged, new { status = "CANCELLED" }, 2, task.TaskId))).Should().BeTrue();
        (await Handler().HandleAsync(Event(TaskEventTypes.StatusChanged, new { status = "CANCELLED" }, 3, task.TaskId))).Should().BeTrue();

        var recall = _events.Events.Should().ContainSingle(e => e.EventType == MaterialAllocationRecallRequired.EventType).Which.Payload
            .Should().BeOfType<MaterialAllocationRecallRequired>().Which;
        recall.MaterialOfficerUserId.Should().Be(officer);
        recall.Items.Should().ContainSingle(line => line.MaterialId == materialId && line.AllocatedQuantity == 12);

        // A cancelled task without allocated materials needs no recall.
        (await Handler().HandleAsync(Event(TaskEventTypes.StatusChanged, new { status = "CANCELLED" }, 4))).Should().BeTrue();
        _events.Events.Should().ContainSingle();
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
