using BuildingBlocks.Security;
using TaskExecution.Application.Assignments;
using TaskExecution.Application.Events;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Tasks;
using TaskExecution.Application.Users;
using TaskExecution.Contracts.Events;
using TaskExecution.Domain.Assignments;
using TaskExecution.Domain.Scopes;
using TaskExecution.Domain.Tasks;
using TaskState = TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.UnitTests.Application;

internal sealed class AssignmentTestFixture
{
    public Guid VolunteerId { get; } = Guid.NewGuid();
    public TestUser User { get; } = new();
    public WorkTask Task { get; }
    public WorkAreaScope Area { get; }
    public SeasonScope Season { get; }
    public TestAssignments Store { get; } = new();
    public TestUsers Identity { get; } = new();
    public TestEvents Events { get; } = new();
    public AssignmentService Service { get; }

    public AssignmentTestFixture()
    {
        Season = SeasonScope.Create(Guid.NewGuid(), User.ParishId!.Value, new DateOnly(2026,10,1),
            new DateOnly(2027,1,15), SeasonScopeStatus.Planning,1,DateTimeOffset.UtcNow);
        Area = WorkAreaScope.Create(Guid.NewGuid(),Season.SeasonId,User.ParishId.Value,"Area",null,
            User.UserId,null,null,null,WorkAreaScopeStatus.Planned,1,DateTimeOffset.UtcNow);
        Task = WorkTask.CreateOpen(Guid.NewGuid(),Area.WorkAreaId,User.UserId,"Task",null,null,
            TaskPriority.Medium,60,1,null,new DateTimeOffset(2026,12,20,17,0,0,TimeSpan.Zero),DateTimeOffset.UtcNow);
        Identity.Items[User.UserId] = new(User.UserId,"Leader","ACTIVE","LEADER",User.ParishId,null,[]);
        Identity.Items[VolunteerId] = new(VolunteerId,"Volunteer","ACTIVE","VOLUNTEER",User.ParishId,null,[]);
        Service = new AssignmentService(Store,new TestTasks(Task),new TaskScopeAccess(new TestScopes(Season,Area),User),
            new ActiveUserAccess(Identity,User),User,new TaskEventRecorder(Events,User,new TestCorrelation()));
    }

    public AssignVolunteerCommand Command() => new(Task.Id,VolunteerId,null,false,false,null);

    public TaskExecution.Application.Attendance.AttendanceService Attendance(TaskExecution.Application.Attendance.IAttendanceStore store) =>
        new(store,Store,new TestTasks(Task),new TaskScopeAccess(new TestScopes(Season,Area),User),
            new ActiveUserAccess(Identity,User),User,new TaskEventRecorder(Events,User,new TestCorrelation()));

    public TaskExecution.Application.Attendance.AttendanceAlertProcessor Alerts(TaskExecution.Application.Attendance.ILongOpenAttendanceStore alerts,
        TaskExecution.Application.Attendance.IAttendanceStore store) => new(alerts,store,Store,new TestTasks(Task),new TestScopes(Season,Area),
            new TaskEventRecorder(Events,User,new TestCorrelation()),new());

    internal sealed class TestUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid UserId { get; set; } = Guid.NewGuid();
        public string Role { get; set; } = "LEADER";
        public Guid? ParishId { get; set; } = Guid.NewGuid();
        public Guid? CommunityId => null;
    }
    internal sealed class TestAssignments : IAssignmentStore
    {
        private readonly SemaphoreSlim _gate = new(1,1);
        public List<TaskAssignment> Items { get; } = [];
        public bool ScheduleConflict { get; set; }
        public int Saves { get; private set; }
        public async Task<T> InTaskTransactionAsync<T>(Guid id,Func<Task<T>> action,CancellationToken ct)
        {
            await _gate.WaitAsync(ct);
            try { return await action(); }
            finally { _gate.Release(); }
        }
        public Task<IReadOnlyList<TaskAssignment>> ListAsync(Guid id,CancellationToken ct) =>
            System.Threading.Tasks.Task.FromResult<IReadOnlyList<TaskAssignment>>(Items.Where(row=>row.TaskId==id).ToList());
        public Task<TaskAssignment?> GetAsync(Guid id,CancellationToken ct) =>
            System.Threading.Tasks.Task.FromResult(Items.SingleOrDefault(row=>row.Id==id));
        public Task<(IReadOnlyList<TaskAssignment> Items,long Total)> ApplicationsPageAsync(Guid id,int page,int size,bool descending,CancellationToken ct)
        {
            var rows = Items.Where(row => row.TaskId == id && row.Type == AssignmentType.Application).OrderBy(row=>row.RequestedAt).ThenBy(row=>row.Id).ToList();
            if (descending) rows.Reverse();
            return System.Threading.Tasks.Task.FromResult<(IReadOnlyList<TaskAssignment>,long)>((rows.Skip((page-1)*size).Take(size).ToList(),rows.Count));
        }
        public Task<bool> HasScheduleConflictAsync(Guid volunteer,Guid task,DateTimeOffset? start,DateTimeOffset end,CancellationToken ct) =>
            System.Threading.Tasks.Task.FromResult(ScheduleConflict);
        public void Add(TaskAssignment row) => Items.Add(row);
        public Task SaveAsync(CancellationToken ct) { Saves++; return System.Threading.Tasks.Task.CompletedTask; }
    }
    internal sealed class TestUsers : IIdentityUserLookup
    {
        public Dictionary<Guid,IdentityUser> Items { get; } = [];
        public Task<IdentityUser?> FindAsync(Guid id,CancellationToken ct=default) =>
            System.Threading.Tasks.Task.FromResult(Items.GetValueOrDefault(id));
    }
    internal sealed class TestEvents : ITaskEventOutbox
    {
        public List<TaskIntegrationEvent> Items { get; } = [];
        public void Add(TaskIntegrationEvent message) => Items.Add(message);
    }
    private sealed class TestCorrelation : ITaskCorrelationContext { public string CorrelationId => "assignment-test"; }
    private sealed class TestScopes(SeasonScope season,WorkAreaScope area) : IScopeProjectionStore
    {
        public Task<SeasonScope?> GetSeasonAsync(Guid id,CancellationToken ct=default) => System.Threading.Tasks.Task.FromResult<SeasonScope?>(season);
        public Task<WorkAreaScope?> GetWorkAreaAsync(Guid id,CancellationToken ct=default) => System.Threading.Tasks.Task.FromResult<WorkAreaScope?>(area);
        public void Add(SeasonScope row) => throw new NotSupportedException();
        public void Add(WorkAreaScope row) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct=default) => throw new NotSupportedException();
    }
    private sealed class TestTasks(WorkTask task) : ITaskRepository
    {
        public Task<WorkTask?> GetByIdAsync(Guid id,CancellationToken ct=default) => System.Threading.Tasks.Task.FromResult(id==task.Id?task:null);
        public Task<WorkTask?> GetByIdForUpdateAsync(Guid id,CancellationToken ct=default) => GetByIdAsync(id,ct);
        public Task<(IReadOnlyList<WorkTask> Items,long TotalItems)> GetPagedByWorkAreaAsync(Guid area,TaskState? status,TaskType? type,int page,int size,string? sort,CancellationToken ct=default) => throw new NotSupportedException();
        public void Add(WorkTask row) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct=default) => throw new NotSupportedException();
    }
}
