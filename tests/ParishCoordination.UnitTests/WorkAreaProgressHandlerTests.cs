using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using FluentAssertions;
using ParishCoordination.Application.WorkAreas;
using ParishCoordination.Application.WorkAreas.Queries.GetWorkAreaProgress;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.UnitTests;

public sealed class WorkAreaProgressHandlerTests
{
    private static readonly Guid ParishId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherParishId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SeasonId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid WorkAreaId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid LeaderId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset CalculatedAt = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(GrottoWorksRoles.Parish)]
    [InlineData(GrottoWorksRoles.Leader)]
    public async Task Authorized_owner_receives_parish_fields_merged_with_task_counts(string role)
    {
        var client = new FakeTaskCountClient();
        var handler = CreateHandler(role, ParishId, role == GrottoWorksRoles.Leader ? LeaderId : Guid.NewGuid(), client);

        var result = await handler.HandleAsync(new GetWorkAreaProgressQuery(WorkAreaId));

        result.Outcome.Should().Be(GetWorkAreaProgressOutcome.Success);
        result.Progress.Should().Be(new WorkAreaProgressDto(
            WorkAreaId,
            SeasonId,
            WorkAreaStatus.InProgress,
            11,
            1,
            10,
            6,
            2,
            CalculatedAt));
        client.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Different_parish_is_denied_before_calling_TaskExecution()
    {
        var client = new FakeTaskCountClient();
        var handler = CreateHandler(GrottoWorksRoles.Parish, OtherParishId, Guid.NewGuid(), client);

        var action = () => handler.HandleAsync(new GetWorkAreaProgressQuery(WorkAreaId));

        (await action.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("PARISH_ACCESS_DENIED");
        client.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Unassigned_leader_is_denied_before_calling_TaskExecution()
    {
        var client = new FakeTaskCountClient();
        var handler = CreateHandler(GrottoWorksRoles.Leader, ParishId, Guid.NewGuid(), client);

        var action = () => handler.HandleAsync(new GetWorkAreaProgressQuery(WorkAreaId));

        (await action.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("WORK_AREA_ACCESS_DENIED");
        client.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Missing_data_and_dependency_failure_return_explicit_outcomes()
    {
        var missingAreaHandler = new GetWorkAreaProgressQueryHandler(
            new FakeWorkAreaRepository(),
            new FakeSeasonRepository(),
            new FakeTaskCountClient(),
            User(GrottoWorksRoles.Parish, ParishId, Guid.NewGuid()));

        var missingArea = await missingAreaHandler.HandleAsync(new GetWorkAreaProgressQuery(WorkAreaId));

        missingArea.Outcome.Should().Be(GetWorkAreaProgressOutcome.WorkAreaNotFound);

        var unavailableClient = new FakeTaskCountClient { ThrowUnavailable = true };
        var unavailableHandler = CreateHandler(
            GrottoWorksRoles.Parish,
            ParishId,
            Guid.NewGuid(),
            unavailableClient);

        var unavailable = await unavailableHandler.HandleAsync(new GetWorkAreaProgressQuery(WorkAreaId));

        unavailable.Outcome.Should().Be(GetWorkAreaProgressOutcome.TaskExecutionUnavailable);
    }

    private static GetWorkAreaProgressQueryHandler CreateHandler(
        string role,
        Guid parishId,
        Guid userId,
        FakeTaskCountClient client)
    {
        var workAreas = new FakeWorkAreaRepository();
        workAreas.Items.Add(WorkArea.Create(
            WorkAreaId,
            SeasonId,
            null,
            null,
            LeaderId,
            null,
            "Area",
            null,
            null,
            null,
            WorkAreaStatus.InProgress,
            DateTimeOffset.UtcNow));

        var seasons = new FakeSeasonRepository();
        seasons.Items.Add(Season.Create(
            SeasonId,
            ParishId,
            Guid.NewGuid(),
            "Season",
            2026,
            new DateOnly(2026, 10, 1),
            new DateOnly(2027, 1, 15),
            SeasonStatus.Active,
            null,
            null,
            DateTimeOffset.UtcNow));

        return new GetWorkAreaProgressQueryHandler(
            workAreas,
            seasons,
            client,
            User(role, parishId, userId));
    }

    private static FakeCurrentUser User(string role, Guid parishId, Guid userId) => new()
    {
        IsAuthenticated = true,
        Role = role,
        ParishId = parishId,
        UserId = userId
    };

    private sealed class FakeTaskCountClient : IWorkAreaTaskCountClient
    {
        public int CallCount { get; private set; }
        public bool ThrowUnavailable { get; init; }

        public Task<WorkAreaTaskCounts> GetAsync(
            Guid workAreaId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (ThrowUnavailable)
            {
                throw new WorkAreaTaskCountUnavailableException("simulated outage");
            }

            return Task.FromResult(new WorkAreaTaskCounts(
                workAreaId,
                11,
                1,
                10,
                6,
                2,
                CalculatedAt));
        }
    }
}
