// Luật 13/11/2026: trưởng khu chỉ phân công tình nguyện viên của khu mình; người khu khác chỉ vào được
// khi khu đó đang xin hỗ trợ (đăng ký), còn phân công trực tiếp thì phải qua luồng support.
using BuildingBlocks.Security;
using FluentAssertions;
using TaskExecution.Application.Assignments;
using TaskExecution.Application.Events;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Tasks;
using TaskExecution.Application.Users;
using TaskExecution.Contracts.Events;
using TaskExecution.Domain;
using TaskExecution.Domain.Assignments;
using TaskExecution.Domain.Scopes;
using TaskExecution.Domain.Tasks;
using TaskExecution.UnitTests.Application;

namespace TaskExecution.UnitTests.Application;

public sealed class AssignmentCommunityTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Leader_cannot_assign_a_volunteer_from_another_community()
    {
        var fixture = new Fixture();

        var error = await Assert.ThrowsAsync<DomainRuleException>(() =>
            fixture.Service.AssignAsync(new AssignVolunteerCommand(fixture.Task.Id, fixture.OutsideVolunteerId, null, false, false, null)));

        error.Code.Should().Be("TASK_ASSIGNEE_OUTSIDE_AREA");
        fixture.Store.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Leader_can_assign_a_volunteer_of_the_same_community()
    {
        var fixture = new Fixture();

        var assignment = await fixture.Service.AssignAsync(
            new AssignVolunteerCommand(fixture.Task.Id, fixture.InsideVolunteerId, null, false, false, null));

        assignment.VolunteerId.Should().Be(fixture.InsideVolunteerId);
        fixture.Store.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Application_from_another_community_is_rejected_until_the_area_asks_for_support()
    {
        var fixture = new Fixture(requestingSupport: false);
        fixture.SignIn(fixture.OutsideVolunteerId);

        var error = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Service.ApplyAsync(fixture.Task.Id, null));
        error.Code.Should().Be("TASK_APPLICATION_OUTSIDE_AREA");

        fixture.Coverage.RequestingSupport = true;
        var application = await fixture.Service.ApplyAsync(fixture.Task.Id, null);

        application.VolunteerId.Should().Be(fixture.OutsideVolunteerId);
        fixture.Store.Items.Should().ContainSingle(row => row.Type == AssignmentType.Application);
    }

    private sealed class Fixture
    {
        private readonly AssignmentTestFixture.TestUser _user = new();
        private readonly SeasonScope _season;
        private readonly WorkAreaScope _area;
        public readonly AssignmentTestFixture.TestAssignments Store = new();
        public readonly AssignmentTestFixture.TestUsers Identity = new();
        public readonly AssignmentTestFixture.TestSupportLifecycle Support = new();
        public readonly AssignmentTestFixture.TestEvents Events = new();
        public readonly NoSupportCoverage Coverage;
        public readonly WorkTask Task;
        public readonly Guid InsideVolunteerId = Guid.NewGuid();
        public readonly Guid OutsideVolunteerId = Guid.NewGuid();
        public readonly AssignmentService Service;

        public Fixture(bool requestingSupport = false)
        {
            Coverage = new NoSupportCoverage(requestingSupport);
            var parishId = _user.ParishId!.Value;
            var community = Guid.NewGuid();
            _season = SeasonScope.Create(Guid.NewGuid(), parishId, new DateOnly(2026, 10, 1), new DateOnly(2027, 1, 15),
                SeasonScopeStatus.Planning, 1, Now);
            _area = WorkAreaScope.Create(Guid.NewGuid(), _season.SeasonId, parishId, "Hang đá", community,
                _user.UserId, null, null, null, WorkAreaScopeStatus.Planned, 1, Now);
            Task = WorkTask.CreateOpen(Guid.NewGuid(), _area.WorkAreaId, _user.UserId, "Task", null, null,
                TaskPriority.Medium, 60, 1, null, new DateTimeOffset(2026, 12, 20, 17, 0, 0, TimeSpan.Zero), Now);
            Identity.Items[_user.UserId] = new(_user.UserId, "Leader", "ACTIVE", "LEADER", parishId, community, []);
            Identity.Items[InsideVolunteerId] = new(InsideVolunteerId, "Trong khu", "ACTIVE", "VOLUNTEER", parishId, community, []);
            Identity.Items[OutsideVolunteerId] = new(OutsideVolunteerId, "Khu khác", "ACTIVE", "VOLUNTEER", parishId, Guid.NewGuid(), []);
            Service = new AssignmentService(Store, new AssignmentTestFixture.TestTasks(Task),
                new TaskScopeAccess(new AssignmentTestFixture.TestScopes(_season, _area), _user),
                new ActiveUserAccess(Identity, _user), _user,
                new TaskEventRecorder(Events, _user, new AssignmentTestFixture.TestCorrelation()),
                Support, Coverage);
        }

        /// <summary>Chuyển người gọi sang tình nguyện viên (luồng tự đăng ký).</summary>
        public void SignIn(Guid volunteerId)
        {
            _user.Role = "VOLUNTEER";
            _user.UserId = volunteerId;
        }
    }
}
