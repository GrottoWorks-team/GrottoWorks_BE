using FluentAssertions;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Tasks;
using TaskExecution.Application.Users;
using TaskExecution.Domain;
using TaskExecution.Domain.Assignments;
using Xunit;

namespace TaskExecution.UnitTests.Application;

public sealed class VolunteerMatchTests
{
    [Fact]
    public async Task Matches_filter_skill_level_and_existing_applications_but_keep_schedule_warnings()
    {
        var fixture = new AssignmentTestFixture();
        var skillId = Guid.NewGuid();
        fixture.Task.ReplaceSkills([new(skillId, "ADVANCED", null)], null, DateTimeOffset.UtcNow);
        var eligible = Candidate(fixture) with { SkillIds = [skillId], Skills = [new(skillId, "EXPERT")] };
        var insufficient = Candidate(fixture) with { SkillIds = [skillId], Skills = [new(skillId, "BASIC")] };
        var applied = eligible with { UserId = Guid.NewGuid() };
        fixture.Store.Items.Add(TaskAssignment.Apply(fixture.Task.Id, applied.UserId, null, DateTimeOffset.UtcNow));
        var directory = new Directory([eligible, insufficient, applied]);
        var schedules = new Schedules([eligible.UserId]);
        var handler = Handler(fixture, directory, schedules);

        var result = await handler.HandleAsync(fixture.Task.Id, null, 20, default);

        result.Should().ContainSingle().Which.VolunteerId.Should().Be(eligible.UserId);
        result.Single().Warnings.Should().Contain("SCHEDULE_CONFLICT");
        result.Single().AvailabilityMatched.Should().BeNull();
        schedules.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Wrong_leader_is_denied_before_calling_identity()
    {
        var fixture = new AssignmentTestFixture();
        fixture.User.UserId = Guid.NewGuid();
        var directory = new Directory([]);
        var handler = Handler(fixture, directory, new Schedules([]));
        Func<Task> query = () => handler.HandleAsync(fixture.Task.Id, null, 20, default);
        (await query.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("WORK_AREA_ACCESS_DENIED");
        directory.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Multiple_identity_pages_are_processed_without_per_volunteer_schedule_queries()
    {
        var fixture = new AssignmentTestFixture();
        var volunteers = Enumerable.Range(0, 101).Select(_ => Candidate(fixture)).ToArray();
        var directory = new Directory(volunteers);
        var schedules = new Schedules([]);
        var result = await Handler(fixture, directory, schedules).HandleAsync(fixture.Task.Id, null, 100, default);
        result.Should().HaveCount(100);
        directory.Calls.Should().Be(2);
        schedules.Calls.Should().Be(1);
    }

    [Fact]
    public void Adjacent_availability_windows_cover_a_task_but_a_gap_does_not()
    {
        var start = DateTimeOffset.Parse("2026-12-01T01:00:00Z");
        GetVolunteerMatchesHandler.AvailabilityCovers([new(start, start.AddHours(1)),
            new(start.AddHours(1), start.AddHours(2))], start, start.AddHours(2)).Should().BeTrue();
        GetVolunteerMatchesHandler.AvailabilityCovers([new(start, start.AddHours(1)),
            new(start.AddHours(1).AddMinutes(1), start.AddHours(2))], start, start.AddHours(2)).Should().BeFalse();
        GetVolunteerMatchesHandler.AvailabilityCovers([], start, start.AddHours(2)).Should().BeNull();
    }

    private static IdentityUser Candidate(AssignmentTestFixture fixture) => new(Guid.NewGuid(), "Volunteer", "ACTIVE", "VOLUNTEER",
        fixture.Area.ParishId, null, [], [], []);
    private static GetVolunteerMatchesHandler Handler(AssignmentTestFixture fixture, Directory directory, Schedules schedules) =>
        new(new AssignmentTestFixture.TestTasks(fixture.Task),
            new TaskScopeAccess(new AssignmentTestFixture.TestScopes(fixture.Season, fixture.Area), fixture.User),
            fixture.Store, directory, schedules);
    private sealed class Directory(IReadOnlyList<IdentityUser> users) : IVolunteerDirectory
    {
        public int Calls { get; private set; }
        public Task<VolunteerDirectoryPage> GetPageAsync(Guid parishId, Guid? communityId, int page, int size, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new VolunteerDirectoryPage(users.Skip((page - 1) * size).Take(size).ToArray(), users.Count));
        }
    }
    private sealed class Schedules(IReadOnlyCollection<Guid> conflicts) : IVolunteerScheduleReader
    {
        public int Calls { get; private set; }
        public Task<IReadOnlySet<Guid>> GetConflictsAsync(IReadOnlyCollection<Guid> volunteerIds, Guid taskId,
            DateTimeOffset? startAt, DateTimeOffset deadline, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult<IReadOnlySet<Guid>>(conflicts.ToHashSet()); }
    }
}
