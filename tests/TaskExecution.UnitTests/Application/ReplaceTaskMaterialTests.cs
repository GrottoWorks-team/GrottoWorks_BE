using FluentAssertions;
using Resource.Contracts.Catalog;
using TaskExecution.Application.Events;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Tasks;
using TaskExecution.Contracts.Events;
using TaskExecution.Domain;
using TaskExecution.Domain.Tasks;
using Xunit;

namespace TaskExecution.UnitTests.Application;

public sealed class ReplaceTaskMaterialTests
{
    [Fact]
    public async Task Successful_replacement_stages_event_and_noop_does_not_save_again()
    {
        var f = new Fixture();
        var item = new TaskMaterialRequirement(f.Material.MaterialId, 2.5m, " trim ");
        var result = await f.Handler.HandleAsync(new(f.Base.Task.Id, [item], 1));
        result.Version.Should().Be(2);
        f.Repository.Saves.Should().Be(1);
        var message = f.Base.Events.Items.Single();
        message.EventType.Should().Be(TaskEventTypes.MaterialsChanged);
        message.Payload.GetProperty("materials")[0].GetProperty("note").GetString().Should().Be("trim");
        await f.Handler.HandleAsync(new(f.Base.Task.Id, [item], 2));
        f.Repository.Saves.Should().Be(1);
        f.Base.Events.Items.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false, "MATERIAL_NOT_FOUND")]
    [InlineData(true, "MATERIAL_INACTIVE")]
    public async Task Invalid_catalog_reference_does_not_change_task(bool inactive, string code)
    {
        var f = new Fixture();
        f.Catalog.Items = inactive ? [f.Material with { Status = "INACTIVE" }] : [];
        Func<Task> replace = () => f.Handler.HandleAsync(new(f.Base.Task.Id, [new(f.Material.MaterialId, 1, null)], null));
        (await replace.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be(code);
        f.Base.Task.Materials.Should().BeEmpty();
        f.Repository.Saves.Should().Be(0);
    }

    [Fact]
    public async Task Wrong_leader_is_rejected_before_catalog_lookup_and_concurrency_is_412()
    {
        var f = new Fixture();
        var leader = f.Base.User.UserId;
        f.Base.User.UserId = Guid.NewGuid();
        Func<Task> replace = () => f.Handler.HandleAsync(new(f.Base.Task.Id, [new(f.Material.MaterialId, 1, null)], null));
        (await replace.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("WORK_AREA_ACCESS_DENIED");
        f.Catalog.Calls.Should().Be(0);
        f.Base.User.UserId = leader;
        f.Repository.FailConcurrency = true;
        (await replace.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("TASK_VERSION_MISMATCH");
    }

    private sealed class Fixture
    {
        public AssignmentTestFixture Base { get; } = new();
        public InternalMaterialDto Material { get; } = new(Guid.NewGuid(), "WOOD", "Wood", "piece", "ACTIVE");
        public Catalog Catalog { get; }
        public Repository Repository { get; }
        public ReplaceTaskMaterialsHandler Handler { get; }
        public Fixture()
        {
            Catalog = new() { Items = [Material] };
            Repository = new(Base.Task);
            Handler = new(Repository, new TaskScopeAccess(new AssignmentTestFixture.TestScopes(Base.Season, Base.Area), Base.User), Catalog,
                new TaskEventRecorder(Base.Events, Base.User, new AssignmentTestFixture.TestCorrelation()));
        }
    }
    private sealed class Catalog : IMaterialCatalogLookup
    {
        public IReadOnlyList<InternalMaterialDto> Items { get; set; } = [];
        public int Calls { get; private set; }
        public Task<IReadOnlyList<InternalMaterialDto>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(Items); }
    }
    private sealed class Repository(WorkTask task) : ITaskRepository
    {
        public int Saves { get; private set; }
        public bool FailConcurrency { get; set; }
        public Task<WorkTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<WorkTask?>(id == task.Id ? task : null);
        public Task<WorkTask?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default) => GetByIdAsync(id, cancellationToken);
        public void Add(WorkTask row) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        { if (FailConcurrency) throw new TaskConcurrencyException(new InvalidOperationException("Simulated concurrency race")); Saves++; return Task.CompletedTask; }
        public Task<(IReadOnlyList<WorkTask> Items, long TotalItems)> GetPagedByWorkAreaAsync(Guid id, TaskExecution.Domain.Tasks.TaskStatus? status,
            TaskType? type, int page, int size, string? sort, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
