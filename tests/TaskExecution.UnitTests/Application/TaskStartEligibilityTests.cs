using BuildingBlocks.Security;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Tasks;
using TaskExecution.Domain;
using TaskExecution.Domain.Assignments;
using TaskExecution.Domain.Scopes;
using TaskExecution.Domain.Tasks;

namespace TaskExecution.UnitTests.Application;

/// <summary>
/// 12/11/2026: nhiệm vụ chỉ được BẮT ĐẦU khi đã tới giờ bắt đầu, đã đủ thành viên nhận việc và
/// đã phân bổ đủ vật tư theo kế hoạch (Resource).
/// </summary>
public sealed class TaskStartEligibilityTests
{
    [Fact]
    public async Task Ready_task_with_due_time_enough_members_and_materials_can_start()
    {
        var fixture = new Fixture();
        var eligibility = await fixture.Service().HandleAsync(fixture.Task.Id);

        eligibility.CanStart.Should().BeTrue();
        eligibility.Blockers.Should().BeEmpty();
        eligibility.AcceptedMembers.Should().Be(1);
        eligibility.MaterialsKnown.Should().BeTrue();
    }

    [Fact]
    public async Task Task_cannot_start_before_its_start_time()
    {
        var fixture = new Fixture(startInFuture: true);
        var eligibility = await fixture.Service().HandleAsync(fixture.Task.Id);

        eligibility.CanStart.Should().BeFalse();
        eligibility.Blockers.Should().ContainSingle(row => row.Code == "TASK_START_NOT_DUE");
        await FluentActions.Awaiting(() => fixture.Service().EnsureCanStartAsync(fixture.Task))
            .Should().ThrowAsync<DomainRuleException>()
            .Where(exception => exception.Code == "TASK_START_NOT_DUE");
    }

    [Fact]
    public async Task Task_cannot_start_without_enough_accepted_members()
    {
        var fixture = new Fixture(requiredVolunteers: 3, acceptedMembers: 2);
        var eligibility = await fixture.Service().HandleAsync(fixture.Task.Id);

        eligibility.CanStart.Should().BeFalse();
        eligibility.Blockers.Should().ContainSingle(row => row.Code == "TASK_NOT_ENOUGH_MEMBERS");
        eligibility.AcceptedMembers.Should().Be(2);
        eligibility.RequiredMembers.Should().Be(3);
    }

    [Fact]
    public async Task Declined_members_do_not_count_towards_the_required_team()
    {
        var fixture = new Fixture(requiredVolunteers: 2, acceptedMembers: 1, declinedMembers: 1);
        var eligibility = await fixture.Service().HandleAsync(fixture.Task.Id);

        eligibility.CanStart.Should().BeFalse();
        eligibility.Blockers.Should().ContainSingle(row => row.Code == "TASK_NOT_ENOUGH_MEMBERS");
    }

    [Fact]
    public async Task Task_cannot_start_while_required_materials_are_not_allocated()
    {
        var materialId = Guid.NewGuid();
        var fixture = new Fixture(materials: [new TaskMaterialRequirement(materialId, 10m, null)]);
        fixture.Materials.Lines = [new TaskMaterialAllocationLine(materialId, 10m, 4m)];

        var eligibility = await fixture.Service().HandleAsync(fixture.Task.Id);

        eligibility.CanStart.Should().BeFalse();
        eligibility.Blockers.Should().ContainSingle(row => row.Code == "TASK_NOT_ENOUGH_MATERIALS");
        eligibility.MaterialShortages.Should().ContainSingle(row =>
            row.MaterialId == materialId && row.RequiredQuantity == 10m && row.AllocatedQuantity == 4m);
    }

    [Fact]
    public async Task Enough_allocated_materials_let_the_task_start()
    {
        var materialId = Guid.NewGuid();
        var fixture = new Fixture(materials: [new TaskMaterialRequirement(materialId, 10m, null)]);
        fixture.Materials.Lines = [new TaskMaterialAllocationLine(materialId, 10m, 10m)];

        var eligibility = await fixture.Service().HandleAsync(fixture.Task.Id);

        eligibility.CanStart.Should().BeTrue();
        eligibility.MaterialShortages.Should().BeEmpty();
    }

    [Fact]
    public async Task Material_service_outage_does_not_block_the_start()
    {
        // Fail-open: sự cố dịch vụ kho không được chặn tình nguyện viên vào ca.
        var fixture = new Fixture(materials: [new TaskMaterialRequirement(Guid.NewGuid(), 5m, null)]);
        fixture.Materials.Unavailable = true;

        var eligibility = await fixture.Service().HandleAsync(fixture.Task.Id);

        eligibility.CanStart.Should().BeTrue();
        eligibility.MaterialsKnown.Should().BeFalse();
    }

    private sealed class Fixture
    {
        private readonly AssignmentTestFixture.TestUser _user = new() { Role = GrottoWorksRoles.Leader };
        public WorkTask Task { get; }
        public Materials Materials { get; } = new();
        private readonly AssignmentTestFixture.TestAssignments _assignments = new();
        private readonly AssignmentTestFixture.TestTasks _tasks;
        private readonly SeasonScope _season;
        private readonly WorkAreaScope _area;

        public Fixture(bool startInFuture = false, int requiredVolunteers = 1, int acceptedMembers = 1, int declinedMembers = 0,
            IReadOnlyList<TaskMaterialRequirement>? materials = null)
        {
            _season = SeasonScope.Create(Guid.NewGuid(), _user.ParishId!.Value, new DateOnly(2026, 10, 1),
                new DateOnly(2027, 1, 15), SeasonScopeStatus.Planning, 1, DateTimeOffset.UtcNow);
            _area = WorkAreaScope.Create(Guid.NewGuid(), _season.SeasonId, _user.ParishId.Value, "Area", null,
                _user.UserId, null, null, null, WorkAreaScopeStatus.Planned, 1, DateTimeOffset.UtcNow);
            var now = DateTimeOffset.UtcNow;
            Task = WorkTask.CreateOpen(Guid.NewGuid(), _area.WorkAreaId, _user.UserId, "Task", null, null,
                TaskPriority.Medium, 60, requiredVolunteers,
                startInFuture ? now.AddHours(3) : now.AddHours(-1),
                new DateTimeOffset(2026, 12, 20, 17, 0, 0, TimeSpan.Zero), now);
            if (materials is not null) Task.ReplaceMaterials(materials, null, now);
            Task.MarkReady(null, now); // OPEN → NOT_STARTED: trưởng khu đã phát hành việc.
            _tasks = new(Task);
            for (var index = 0; index < acceptedMembers; index++) AddMember(AssignmentStatus.Accepted);
            for (var index = 0; index < declinedMembers; index++) AddMember(AssignmentStatus.Declined);
        }

        public TaskStartEligibilityService Service() => new(_tasks, _assignments, Materials,
            new TaskScopeAccess(new AssignmentTestFixture.TestScopes(_season, _area), _user));

        private void AddMember(AssignmentStatus status)
        {
            var assignment = TaskAssignment.Assign(Task.Id, Guid.NewGuid(), _user.UserId, null, false, false, null, DateTimeOffset.UtcNow);
            if (status == AssignmentStatus.Accepted) assignment.Accept(1, DateTimeOffset.UtcNow);
            else assignment.Decline("not available", 1, DateTimeOffset.UtcNow);
            _assignments.Items.Add(assignment);
        }
    }

    private sealed class Materials : ITaskResourceGateway
    {
        public IReadOnlyList<TaskMaterialAllocationLine> Lines { get; set; } = [];
        public bool Unavailable { get; set; }

        public Task<PurchaseRequestSnapshot> PurchaseRequestAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();

        public Task<System.Text.Json.JsonElement> MaterialSummaryAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();

        public System.Threading.Tasks.Task<IReadOnlyList<TaskMaterialAllocationLine>> TaskMaterialsAsync(Guid id, CancellationToken ct) =>
            Unavailable
                ? throw new DomainRuleException("RESOURCE_SERVICE_UNAVAILABLE", "Unavailable", DomainErrorKind.ServiceUnavailable)
                : System.Threading.Tasks.Task.FromResult(Lines);
    }
}
