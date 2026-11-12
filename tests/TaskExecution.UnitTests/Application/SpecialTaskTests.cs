using System.Text.Json;
using FluentAssertions;
using TaskExecution.Application.Events;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Tasks;
using TaskExecution.Application.Users;
using TaskExecution.Domain;
using TaskExecution.Domain.Tasks;
using TaskState=TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.UnitTests.Application;

public sealed class SpecialTaskTests
{
    [Fact]
    public async Task Internal_corrective_audit_uses_the_verified_requesting_leader_not_an_absent_JWT_user()
    {
        var f=new Fixture();var leader=f.Source.User.UserId;f.Source.User.UserId=Guid.Empty;
        var command=new CorrectiveTaskCommand(f.Source.Area.WorkAreaId,Guid.NewGuid(),leader,"Repair",null,null,TaskPriority.Medium,60,1,null,f.Command().Deadline);
        await f.Service.CorrectiveAsync(Guid.NewGuid().ToString("D"),command,default);
        f.Source.Events.Items.Single(row=>row.EventType=="task.created").ActorId.Should().Be(leader);
    }
    [Fact]
    public async Task Approved_purchase_derives_its_area_and_one_buyer_slot_from_the_source()
    {
        var f=new Fixture();var id=Guid.NewGuid();
        var task=await f.Service.PurchaseAsync(id,f.Command(),default);
        task.TaskType.Should().Be(TaskType.Purchase);task.Status.Should().Be(TaskState.Open);
        task.WorkAreaId.Should().Be(f.Source.Area.WorkAreaId);f.Tasks.Rows.Single().RequiredVolunteerCount.Should().Be(1);
    }
    [Fact]
    public async Task Unapproved_purchase_does_not_create_a_task_or_an_event()
    {
        var f=new Fixture();f.Resource.Status="PLANNING";
        Func<Task> create=()=>f.Service.PurchaseAsync(Guid.NewGuid(),f.Command(),default);
        await create.Should().ThrowAsync<DomainRuleException>().Where(error=>error.Code=="PURCHASE_REQUEST_NOT_APPROVED");
        f.Tasks.Rows.Should().BeEmpty();f.Source.Events.Items.Should().BeEmpty();
    }
    [Fact]
    public async Task Buyer_assigned_purchase_can_get_a_replacement_task()
    {
        // 12/11/2026: việc mua cũ bị huỷ ⇒ đơn có thể còn BUYER_ASSIGNED; vẫn phải tạo lại được việc thay thế.
        var f=new Fixture();f.Resource.Status="BUYER_ASSIGNED";
        var task=await f.Service.PurchaseAsync(Guid.NewGuid(),f.Command(),default);
        task.TaskType.Should().Be(TaskType.Purchase);
        f.Tasks.Rows.Should().ContainSingle();
    }
    [Fact]
    public async Task Corrective_replay_keeps_the_original_identity_and_does_not_duplicate_business_state()
    {
        var f=new Fixture();var command=new CorrectiveTaskCommand(f.Source.Area.WorkAreaId,Guid.NewGuid(),f.Source.User.UserId,
            "Repair",null,null,TaskPriority.Medium,60,1,null,f.Command().Deadline);var key=Guid.NewGuid().ToString("D");
        var first=await f.Service.CorrectiveAsync(key,command,default);
        var replay=await f.Service.CorrectiveAsync(key,command,default);
        replay.Id.Should().Be(first.Id);first.TaskType.Should().Be(TaskType.Corrective);f.Tasks.Rows.Should().ContainSingle();
    }
    private sealed class Fixture
    {
        public AssignmentTestFixture Source=new();public Tasks Tasks=new();public SourceResource Resource;
        public SpecialTaskService Service;
        public Fixture()
        {
            Resource=new(Source.Area.WorkAreaId);var scopes=new AssignmentTestFixture.TestScopes(Source.Season,Source.Area);
            Service=new(Tasks,new Store(),Resource,new TaskScopeAccess(scopes,Source.User),scopes,new ActiveUserAccess(Source.Identity,Source.User),
                Source.User,new TaskEventRecorder(Source.Events,Source.User,new AssignmentTestFixture.TestCorrelation()));
        }
        public CreateTaskCommand Command()=>new(Guid.Empty,"Buy",null,null,TaskPriority.Medium,60,1,null,new DateTimeOffset(2026,12,15,0,0,0,TimeSpan.Zero));
    }
    private sealed class Tasks : ITaskRepository
    {
        public readonly List<WorkTask> Rows=[];
        public void Add(WorkTask row)=>Rows.Add(row);
        public Task SaveChangesAsync(CancellationToken ct=default)=>Task.CompletedTask;
        public Task<WorkTask?> GetByIdAsync(Guid id,CancellationToken ct=default)=>Task.FromResult(Rows.SingleOrDefault(row=>row.Id==id));
        public Task<WorkTask?> GetByIdForUpdateAsync(Guid id,CancellationToken ct=default)=>GetByIdAsync(id,ct);
        public Task<(IReadOnlyList<WorkTask> Items,long TotalItems)> GetPagedByWorkAreaAsync(Guid area,TaskState? status,TaskType? type,int page,int size,string? sort,CancellationToken ct=default)=>throw new NotSupportedException();
    }
    private sealed class Store : ISpecialTaskStore
    {
        private TaskDto? saved;
        public Task<T> InTransactionAsync<T>(string key,Func<Task<T>> action,CancellationToken ct)=>action();
        public Task<WorkTask?> FindPurchaseAsync(Guid id,CancellationToken ct)=>Task.FromResult<WorkTask?>(null);
        public Task<TaskDto?> ReplayCorrectiveAsync(string key,Guid itemId,string fingerprint,CancellationToken ct)=>Task.FromResult(saved);
        public void SaveCorrective(string key,Guid itemId,string fingerprint,TaskDto task)=>saved=task;
    }
    private sealed class SourceResource(Guid area) : ITaskResourceGateway
    {
        public string Status="APPROVED";
        public Task<PurchaseRequestSnapshot> PurchaseRequestAsync(Guid id,CancellationToken ct)=>Task.FromResult(new PurchaseRequestSnapshot(id,area,Status,1,[]));
        public Task<JsonElement> MaterialSummaryAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
        public Task<IReadOnlyList<TaskMaterialAllocationLine>> TaskMaterialsAsync(Guid id,CancellationToken ct)=>Task.FromResult<IReadOnlyList<TaskMaterialAllocationLine>>([]);
    }
}
