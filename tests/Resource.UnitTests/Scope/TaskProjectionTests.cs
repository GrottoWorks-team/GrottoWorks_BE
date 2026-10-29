using Resource.Application.Scope;
using Resource.Domain;
using Resource.Domain.Scope;

namespace Resource.UnitTests.Scope;

/// <summary>F-RES-01: task projection — idempotent by source version, work area must be projected first.</summary>
public sealed class TaskProjectionTests
{
    private readonly FakeScopeStore _scopes = new();
    private readonly FakeTaskProjectionStore _tasks = new();
    private readonly WorkAreaScope _area;

    public TaskProjectionTests() => _area = _scopes.Seed(Guid.NewGuid(), Guid.NewGuid());

    private ApplyTaskSnapshotHandler Handler() => new(_tasks, _scopes, new Resource.Application.Purchases.PurchaseBuyerSync(new FakePurchaseRequestStore()));

    private TaskSnapshot Snapshot(Guid id, long version, ProjectedTaskStatus status = ProjectedTaskStatus.Open, params Guid[] assignees) =>
        new(id, _area.WorkAreaId, " Treo đèn ", ProjectedTaskType.Work, status, null, assignees, version);

    [Fact]
    public async Task Newer_snapshots_apply_and_older_ones_are_ignored()
    {
        var id = Guid.NewGuid();
        var volunteer = Guid.NewGuid();

        (await Handler().HandleAsync(Snapshot(id, 1))).Applied.Should().BeTrue();
        var (task, applied) = await Handler().HandleAsync(Snapshot(id, 3, ProjectedTaskStatus.Completed, volunteer, volunteer));
        applied.Should().BeTrue();
        task.Status.Should().Be(ProjectedTaskStatus.Completed);
        task.Name.Should().Be("Treo đèn");
        task.AssigneeUserIds.Should().Equal(volunteer);

        (await Handler().HandleAsync(Snapshot(id, 2, ProjectedTaskStatus.InProgress))).Applied.Should().BeFalse();
        _tasks.Tasks.Single().Status.Should().Be(ProjectedTaskStatus.Completed);
    }

    [Fact]
    public async Task Unknown_work_area_and_purchase_link_rules_are_rejected()
    {
        var unknownArea = Snapshot(Guid.NewGuid(), 1) with { WorkAreaId = Guid.NewGuid() };
        (await Handler().Invoking(h => h.HandleAsync(unknownArea)).Should().ThrowAsync<DomainRuleException>())
            .Which.Code.Should().Be("WORK_AREA_NOT_PROJECTED");

        var purchaseWithoutRequest = Snapshot(Guid.NewGuid(), 1) with { Type = ProjectedTaskType.Purchase };
        (await Handler().Invoking(h => h.HandleAsync(purchaseWithoutRequest)).Should().ThrowAsync<DomainRuleException>())
            .Which.Code.Should().Be("TASK_PROJECTION_INVALID");
    }
}
