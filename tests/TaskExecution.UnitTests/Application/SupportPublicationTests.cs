using FluentAssertions;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Support;
using TaskExecution.Application.Users;
using TaskExecution.Contracts.Events;
using TaskExecution.Domain;
using TaskExecution.Domain.Scopes;
using TaskExecution.Domain.Support;
using TaskState = TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.UnitTests.Application;

public sealed class SupportPublicationTests
{
    [Fact]
    public async Task Assigned_leader_publishes_with_linked_tasks_and_stages_one_event_in_the_transaction()
    {
        var fixture = new Fixture();
        var before = DateTimeOffset.UtcNow;
        var dto = await fixture.Handler.HandleAsync(fixture.Request.Id, 1);
        dto.Status.Should().Be(SupportRequestStatus.Published);
        dto.Version.Should().Be(2);
        dto.UpdatedAt.Should().BeOnOrAfter(before);
        dto.Positions.Single().TaskId.Should().Be(fixture.Source.Task.Id);
        fixture.Store.TaskReads.Should().Be(1);
        fixture.Store.SavedInTransaction.Should().BeTrue();
        var message = fixture.Source.Events.Items.Should().ContainSingle().Which;
        message.EventType.Should().Be(TaskEventTypes.SupportRequestPublished);
        message.AggregateId.Should().Be(dto.Id);
        message.AggregateVersion.Should().Be(dto.Version);
        message.ParishId.Should().Be(fixture.Source.Area.ParishId);
        message.Payload.GetProperty("positions")[0].GetProperty("taskId").GetGuid().Should().Be(fixture.Source.Task.Id);
    }

    [Fact]
    public async Task A_second_publication_is_rejected_without_another_event_or_save()
    {
        var fixture = new Fixture();
        await fixture.Handler.HandleAsync(fixture.Request.Id, 1);
        Func<Task> repeat = () => fixture.Handler.HandleAsync(fixture.Request.Id, 2);
        await repeat.Should().ThrowAsync<DomainRuleException>().Where(error => error.Code == "SUPPORT_REQUEST_INVALID_STATE");
        fixture.Source.Events.Items.Should().ContainSingle();
        fixture.Store.Saves.Should().Be(1);
    }

    [Theory]
    [InlineData(SupportType.Staffing, "SUPPORT_POSITION_REQUIRED")]
    [InlineData(SupportType.Material, "SUPPORT_MATERIAL_REQUIRED")]
    [InlineData(SupportType.Mixed, "SUPPORT_POSITION_REQUIRED")]
    public void Publication_requires_the_lines_for_its_support_type(SupportType type, string code)
    {
        var request = SupportRequest.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), type, null, null, [], [], DateTimeOffset.UtcNow);
        Action publish = () => request.Publish(1, DateTimeOffset.UtcNow);
        publish.Should().Throw<DomainRuleException>().Which.Code.Should().Be(code);
        request.Version.Should().Be(1);
    }

    [Fact]
    public void Existing_planning_positions_can_remain_unlinked_but_cannot_be_published()
    {
        var request = SupportRequest.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), SupportType.Staffing, null, null,
            [new(null, "Team", 1, null, [])], [], DateTimeOffset.UtcNow);
        request.Positions.Single().TaskId.Should().BeNull();
        Action publish = () => request.Publish(1, DateTimeOffset.UtcNow);
        publish.Should().Throw<DomainRuleException>().Which.Code.Should().Be("SUPPORT_POSITION_TASK_REQUIRED");
    }

    [Theory]
    [InlineData(TaskState.Completed)]
    [InlineData(TaskState.Cancelled)]
    public async Task Closed_position_task_is_rejected_before_publication(TaskState status)
    {
        var fixture = new Fixture();
        fixture.Store.Tasks[0] = fixture.Store.Tasks[0] with { Status = status };
        Func<Task> publish = () => fixture.Handler.HandleAsync(fixture.Request.Id, 1);
        await publish.Should().ThrowAsync<DomainRuleException>().Where(error => error.Code == "TASK_INVALID_STATE");
        fixture.Request.Status.Should().Be(SupportRequestStatus.Planning);
        fixture.Source.Events.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_and_foreign_work_area_tasks_are_rejected()
    {
        var fixture = new Fixture();
        fixture.Store.Tasks.Clear();
        Func<Task> publish = () => fixture.Handler.HandleAsync(fixture.Request.Id, 1);
        await publish.Should().ThrowAsync<DomainRuleException>().Where(error => error.Code == "TASK_NOT_FOUND");
        fixture.Store.Tasks.Add(new(fixture.Source.Task.Id, Guid.NewGuid(), TaskState.Open));
        await publish.Should().ThrowAsync<DomainRuleException>().Where(error => error.Code == "SUPPORT_POSITION_TASK_MISMATCH");
        fixture.Store.Saves.Should().Be(0);
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("VOLUNTEER")]
    public async Task Other_roles_do_not_bypass_work_area_ownership(string role)
    {
        var fixture = new Fixture();
        fixture.Source.User.Role = role;
        Func<Task> publish = () => fixture.Handler.HandleAsync(fixture.Request.Id, 1);
        await publish.Should().ThrowAsync<DomainRuleException>().Where(error => error.Code == "WORK_AREA_ACCESS_DENIED");
        fixture.Source.Events.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_request_archived_season_and_stale_version_are_rejected()
    {
        var fixture = new Fixture();
        Func<Task> publish = () => fixture.Handler.HandleAsync(Guid.NewGuid(), 1);
        await publish.Should().ThrowAsync<DomainRuleException>().Where(error => error.Code == "SUPPORT_REQUEST_NOT_FOUND");
        publish = () => fixture.Handler.HandleAsync(fixture.Request.Id, 99);
        await publish.Should().ThrowAsync<DomainRuleException>().Where(error => error.Code == "SUPPORT_REQUEST_VERSION_MISMATCH");
        var season = fixture.Source.Season;
        season.Apply(season.ParishId, season.StartDate, season.EndDate, SeasonScopeStatus.Archived, 2, DateTimeOffset.UtcNow);
        publish = () => fixture.Handler.HandleAsync(fixture.Request.Id, 1);
        await publish.Should().ThrowAsync<DomainRuleException>().Where(error => error.Code == "SEASON_READ_ONLY");
    }

    internal sealed class Fixture
    {
        public AssignmentTestFixture Source { get; } = new();
        public Store Store { get; } = new();
        public SupportRequest Request { get; }
        public PublishSupportRequestHandler Handler { get; }
        public Fixture()
        {
            Request = SupportRequest.Create(Source.Season.SeasonId, Source.Area.WorkAreaId, Source.User.UserId, SupportType.Staffing,
                null, null, [new(null, "Team", 1, null, [], Source.Task.Id)], [], DateTimeOffset.UtcNow);
            Store.Rows.Add(Request);
            Store.Tasks.Add(new(Source.Task.Id, Source.Area.WorkAreaId, Source.Task.Status));
            Handler = new(Store, new TaskScopeAccess(new AssignmentTestFixture.TestScopes(Source.Season, Source.Area), Source.User),
                new ActiveUserAccess(Source.Identity, Source.User), Source.Events, new AssignmentTestFixture.TestCorrelation(), TimeProvider.System);
        }
    }

    internal sealed class Store : ISupportRequestRepository
    {
        private readonly SemaphoreSlim gate=new(1,1);
        public Task<Guid?> FindRequestIdByMemberAsync(Guid id,CancellationToken ct)=>
            Task.FromResult(Rows.Where(row=>row.Responses.Any(response=>response.Members.Any(member=>member.Id==id))).Select(row=>(Guid?)row.Id).SingleOrDefault());
        public List<SupportRequest> Rows { get; } = [];
        public List<SupportPositionTask> Tasks { get; } = [];
        public int TaskReads { get; private set; }
        public int Saves { get; private set; }
        private bool inTransaction;
        public bool SavedInTransaction { get; private set; }
        public async Task<T> InRequestTransactionAsync<T>(Guid requestId, Func<Task<T>> operation, CancellationToken ct)
        { await gate.WaitAsync(ct); inTransaction = true; try { return await operation(); } finally { inTransaction = false; gate.Release(); } }
        public Task<IReadOnlyList<SupportPositionTask>> GetPositionTasksAsync(IReadOnlyCollection<Guid> taskIds, CancellationToken ct)
        { TaskReads++; return Task.FromResult<IReadOnlyList<SupportPositionTask>>(Tasks.Where(task => taskIds.Contains(task.Id)).ToList()); }
    public Task<bool> IsTaskRequestingSupportAsync(Guid taskId, CancellationToken ct) =>
        Task.FromResult(Rows.Any(row => (row.Status == SupportRequestStatus.Published || row.Status == SupportRequestStatus.PartiallyFulfilled) &&
            row.Positions.Any(position => position.TaskId == taskId)));
        public Task<SupportRequest?> GetAsync(Guid id, bool tracked, CancellationToken ct) => Task.FromResult(Rows.SingleOrDefault(row => row.Id == id));
        public Task<(IReadOnlyList<SupportRequest> Items, long Total)> GetPageAsync(Guid? parish, Guid? season, Guid? area,
            SupportType? type, SupportRequestStatus? status, int page, int size, bool descending, CancellationToken ct) => throw new NotSupportedException();
        public void Add(SupportRequest request) => Rows.Add(request);
        public Task SaveAsync(CancellationToken ct) { Saves++; SavedInTransaction = inTransaction; return Task.CompletedTask; }
    }
}
