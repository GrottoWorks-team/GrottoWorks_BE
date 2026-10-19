using BuildingBlocks.Security;
using FluentAssertions;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Tasks;
using TaskExecution.Domain;
using TaskExecution.Domain.Scopes;
using TaskExecution.Domain.Tasks;
using TaskState = TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.UnitTests.Application;

public sealed class TaskQueryHandlerTests
{
    [Fact]
    public async Task List_returns_filtered_page_for_same_parish()
    {
        var parishId = Guid.NewGuid();
        var workArea = CreateWorkArea(parishId);
        var repository = new FakeTaskRepository();
        repository.Items.Add(CreateTask(workArea.WorkAreaId, "Lighting"));
        var handler = new GetTasksQueryHandler(
            repository,
            CreateAccess(workArea, GrottoWorksRoles.Volunteer, parishId));

        var result = await handler.HandleAsync(new GetTasksQuery(
            workArea.WorkAreaId, TaskState.Draft, TaskType.Work, 1, 20, "name,asc"));

        result.Items.Should().ContainSingle().Which.Name.Should().Be("Lighting");
        result.Page.TotalItems.Should().Be(1);
        repository.LastStatus.Should().Be(TaskState.Draft);
        repository.LastType.Should().Be(TaskType.Work);
    }

    [Fact]
    public async Task Admin_can_read_task_from_any_parish()
    {
        var workArea = CreateWorkArea(Guid.NewGuid());
        var task = CreateTask(workArea.WorkAreaId, "Structure");
        var repository = new FakeTaskRepository();
        repository.Items.Add(task);
        var handler = new GetTaskQueryHandler(
            repository,
            CreateAccess(workArea, GrottoWorksRoles.Admin, null));

        var result = await handler.HandleAsync(task.Id);

        result.Id.Should().Be(task.Id);
        result.EstimatedMinutes.Should().Be(120);
    }

    [Fact]
    public async Task User_from_another_parish_is_denied()
    {
        var workArea = CreateWorkArea(Guid.NewGuid());
        var task = CreateTask(workArea.WorkAreaId, "Structure");
        var repository = new FakeTaskRepository();
        repository.Items.Add(task);
        var handler = new GetTaskQueryHandler(
            repository,
            CreateAccess(workArea, GrottoWorksRoles.Volunteer, Guid.NewGuid()));

        var act = () => handler.HandleAsync(task.Id);

        var exception = await act.Should().ThrowAsync<DomainRuleException>();
        exception.Which.Code.Should().Be("PARISH_ACCESS_DENIED");
    }

    [Fact]
    public async Task Missing_task_and_work_area_return_distinct_errors()
    {
        var repository = new FakeTaskRepository();
        var access = CreateAccess(null, GrottoWorksRoles.Volunteer, Guid.NewGuid());
        var detailHandler = new GetTaskQueryHandler(repository, access);
        var listHandler = new GetTasksQueryHandler(repository, access);

        var missingTask = () => detailHandler.HandleAsync(Guid.NewGuid());
        var missingArea = () => listHandler.HandleAsync(
            new GetTasksQuery(Guid.NewGuid(), null, null, 1, 20, null));

        (await missingTask.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("TASK_NOT_FOUND");
        (await missingArea.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("WORK_AREA_NOT_FOUND");
    }

    private static TaskScopeAccess CreateAccess(WorkAreaScope? workArea, string role, Guid? parishId) =>
        new(new FakeScopeStore(workArea), new FakeCurrentUser(role, parishId));

    private static WorkAreaScope CreateWorkArea(Guid parishId) =>
        WorkAreaScope.Create(
            Guid.NewGuid(), Guid.NewGuid(), parishId, "Area", null, null, null,
            null, null, WorkAreaScopeStatus.Planned, 1, DateTimeOffset.UtcNow);

    private static WorkTask CreateTask(Guid workAreaId, string name) =>
        WorkTask.CreateDraft(
            Guid.NewGuid(), workAreaId, Guid.NewGuid(), name, "Description", "Done safely",
            TaskPriority.High, 120, 2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1),
            DateTimeOffset.UtcNow);

    private sealed class FakeTaskRepository : ITaskRepository
    {
        public List<WorkTask> Items { get; } = [];
        public TaskState? LastStatus { get; private set; }
        public TaskType? LastType { get; private set; }

        public Task<WorkTask?> GetByIdAsync(Guid taskId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.SingleOrDefault(task => task.Id == taskId));
        public Task<WorkTask?> GetByIdForUpdateAsync(Guid taskId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.SingleOrDefault(task => task.Id == taskId));

        public Task<(IReadOnlyList<WorkTask> Items, long TotalItems)> GetPagedByWorkAreaAsync(
            Guid workAreaId,
            TaskState? status,
            TaskType? taskType,
            int page,
            int size,
            string? sort,
            CancellationToken cancellationToken = default)
        {
            LastStatus = status;
            LastType = taskType;
            var matches = Items.Where(task => task.WorkAreaId == workAreaId).ToList();
            return Task.FromResult<(IReadOnlyList<WorkTask>, long)>((matches, matches.Count));
        }

        public void Add(WorkTask task) => Items.Add(task);
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeScopeStore(WorkAreaScope? workArea) : IScopeProjectionStore
    {
        public Task<SeasonScope?> GetSeasonAsync(Guid seasonId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SeasonScope?>(null);

        public Task<WorkAreaScope?> GetWorkAreaAsync(Guid workAreaId, CancellationToken cancellationToken = default) =>
            Task.FromResult(workArea?.WorkAreaId == workAreaId ? workArea : null);

        public void Add(SeasonScope scope) => throw new NotSupportedException();
        public void Add(WorkAreaScope scope) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeCurrentUser(string role, Guid? parishId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid UserId { get; } = Guid.NewGuid();
        public string Role => role;
        public Guid? ParishId => parishId;
        public Guid? CommunityId => null;
    }
}
