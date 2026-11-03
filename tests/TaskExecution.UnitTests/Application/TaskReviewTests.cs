using System.Text.Json;
using FluentAssertions;
using TaskExecution.Application.Events;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Tasks;
using TaskExecution.Application.Users;
using TaskExecution.Domain;
using TaskExecution.Domain.Tasks;
using TaskExecution.Domain.Assignments;
using TaskState=TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.UnitTests.Application;
public sealed class TaskReviewTests
{
    [Fact]
    public async Task Submission_then_approval_preserves_evidence_and_completes_assignments()
    {
        var f=new ReviewFixture();var file=Guid.NewGuid();
        var progress=await f.Service.AddAsync(f.F.Task.Id,new(100,"done",[new(file,"photo")]),true,false,default);
        f.F.Task.Status.Should().Be(TaskState.Submitted);f.Files.Reserved.Should().ContainSingle();
        f.Store.Progress.Single().ApprovedAt.Should().BeNull();f.F.Events.Items.Single().EventType.Should().Be("task.progress.submitted");
        f.AsLeader();await f.Service.ReviewAsync(f.F.Task.Id,true,null,null,default);
        f.F.Task.Status.Should().Be(TaskState.Completed);f.F.Store.Items.Single().Status.Should().Be(AssignmentStatus.Completed);
        f.Store.Progress.Single().ApprovedAt.Should().NotBeNull();f.Store.Decisions.Should().ContainSingle();
        f.F.Events.Items.Last().Payload.GetProperty("recipientUserIds").EnumerateArray().Select(x=>x.GetGuid()).Should().Contain(f.F.VolunteerId);
        progress.Evidence.Single().FileId.Should().Be(file);
    }
    [Fact]
    public async Task Inactive_user_cannot_reserve_evidence()
    {
        var f=new ReviewFixture();f.F.Identity.Items[f.F.VolunteerId]=f.F.Identity.Items[f.F.VolunteerId] with {Status="LOCKED"};
        var action=()=>f.Service.AddAsync(f.F.Task.Id,new(50,null,[new(Guid.NewGuid(),null)]),false,false,default);
        await action.Should().ThrowAsync<DomainRuleException>();f.Files.Reserved.Should().BeEmpty();f.Store.Intents.Should().BeEmpty();
    }
    [Fact]
    public async Task Resource_failure_is_reported_by_review_package()
    {
        var f=new ReviewFixture();f.AsLeader();f.Resource.Fail=true;
        var action=()=>f.Service.PackageAsync(f.F.Task.Id,default);
        (await action.Should().ThrowAsync<DomainRuleException>()).Which.Kind.Should().Be(DomainErrorKind.ServiceUnavailable);
    }
    [Fact]
    public async Task Download_checks_exact_record_membership_before_file_service()
    {
        var f=new ReviewFixture();var progress=await f.Service.AddAsync(f.F.Task.Id,new(50,null,[new(Guid.NewGuid(),null)]),false,false,default);
        var action=()=>f.Service.DownloadAsync(f.F.Task.Id,progress.Id,Guid.NewGuid(),default);
        await action.Should().ThrowAsync<DomainRuleException>();f.Files.Downloads.Should().Be(0);
        await f.Service.DownloadAsync(f.F.Task.Id,progress.Id,progress.Evidence.Single().FileId,default);
        f.Files.Finalized.Should().ContainSingle();f.Files.Downloads.Should().Be(1);
    }
    private sealed class ReviewFixture
    {
        public AssignmentTestFixture F {get;}=new();public Store Store {get;}=new();public Files Files {get;}=new();public Resource Resource {get;}=new();
        public TaskReviewService Service {get;}
        public ReviewFixture()
        {
            var a=TaskAssignment.Assign(F.Task.Id,F.VolunteerId,F.User.UserId,null,false,false,null,DateTimeOffset.UtcNow);a.Accept(null,DateTimeOffset.UtcNow);F.Store.Items.Add(a);
            F.Task.MarkReady(null,DateTimeOffset.UtcNow);F.Task.BeginWork(DateTimeOffset.UtcNow);
            F.User.UserId=F.VolunteerId;F.User.Role="VOLUNTEER";
            Service=new(new AssignmentTestFixture.TestTasks(F.Task),F.Store,Store,new TaskScopeAccess(new AssignmentTestFixture.TestScopes(F.Season,F.Area),F.User),
                new ActiveUserAccess(F.Identity,F.User),F.User,new TaskEventRecorder(F.Events,F.User,new AssignmentTestFixture.TestCorrelation()),Files,Resource,F.Support);
        }
        public void AsLeader(){F.User.UserId=F.Area.LeaderUserId!.Value;F.User.Role="LEADER";}
    }
    private sealed class Store : ITaskReviewStore
    {
        public async Task<(IReadOnlyList<TaskProgressDto> Items,long Total)> GetPageAsync(Guid id,int page,int size,bool ascending,CancellationToken ct)
        {
            var rows=await ListAsync(id,ct);
            var ordered=ascending ? rows.OrderBy(row=>row.SubmittedAt).ThenBy(row=>row.Id) : rows.OrderByDescending(row=>row.SubmittedAt).ThenByDescending(row=>row.Id);
            return (ordered.Skip((page-1)*size).Take(size).ToArray(),rows.Count);
        }
        public List<TaskProgress> Progress {get;}=[];public List<ProgressEvidence> Evidence {get;}=[];public List<TaskCompletionReview> Decisions {get;}=[];public List<AttachmentIntent> Intents {get;}=[];
        public Task<IReadOnlyList<TaskProgressDto>> ListAsync(Guid id,CancellationToken ct)=>Task.FromResult<IReadOnlyList<TaskProgressDto>>(Progress.Where(p=>p.TaskId==id).Select(p=>new TaskProgressDto(p.Id,p.TaskId,p.SubmittedByUserId,p.PercentComplete,p.Note,p.SubmittedAt,p.IsSubmission,p.ApprovedByUserId,p.ApprovedAt,Evidence.Where(e=>e.ProgressId==p.Id).ToArray())).ToArray());
        public Task<IReadOnlyList<TaskCompletionReview>> ReviewsAsync(Guid id,CancellationToken ct)=>Task.FromResult<IReadOnlyList<TaskCompletionReview>>(Decisions);
        public Task<decimal> ApprovedHoursAsync(Guid id,CancellationToken ct)=>Task.FromResult(2m);
        public Task PrepareAttachmentsAsync(IReadOnlyList<AttachmentIntent> rows,CancellationToken ct){Intents.AddRange(rows);return Task.CompletedTask;}
        public Task EnsureAttachmentsActiveAsync(IReadOnlyList<AttachmentIntent> rows,CancellationToken ct)=>Task.CompletedTask;
        public Task<AttachmentIntent?> FindAttachmentAsync(Guid progress,Guid file,CancellationToken ct)=>Task.FromResult(Intents.SingleOrDefault(i=>i.ProgressId==progress && i.FileId==file));
        public void Add(TaskProgress p,IReadOnlyList<ProgressEvidence> evidence){Progress.Add(p);Evidence.AddRange(evidence);}
        public void Add(TaskCompletionReview review)=>Decisions.Add(review);
        public Task<TaskProgress?> LatestSubmissionAsync(Guid id,CancellationToken ct)=>Task.FromResult(Progress.LastOrDefault(p=>p.TaskId==id && p.IsSubmission));
    }
    private sealed class Files : ITaskFileGateway
    {
        public List<AttachmentIntent> Reserved {get;}=[];public List<AttachmentIntent> Finalized {get;}=[];public int Downloads {get;private set;}
        public Task ReserveAsync(AttachmentIntent row,CancellationToken ct){Reserved.Add(row);return Task.CompletedTask;}
        public Task FinalizeAsync(AttachmentIntent row,CancellationToken ct){Finalized.Add(row);return Task.CompletedTask;}
        public Task ReleaseAsync(AttachmentIntent row,CancellationToken ct){Reserved.Remove(row);return Task.CompletedTask;}
        public Task<FileDownload> DownloadAsync(Guid file,Guid progress,CancellationToken ct){Downloads++;return Task.FromResult(new FileDownload("https://files.test/download",DateTimeOffset.UtcNow.AddMinutes(5)));}
    }
    private sealed class Resource : ITaskResourceGateway
    {
        public bool Fail {get;set;}
        public Task<PurchaseRequestSnapshot> PurchaseRequestAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
        public Task<JsonElement> MaterialSummaryAsync(Guid id,CancellationToken ct)=>Fail ? throw new DomainRuleException("RESOURCE_SERVICE_UNAVAILABLE","Unavailable",DomainErrorKind.ServiceUnavailable) : Task.FromResult(JsonSerializer.SerializeToElement(new {taskId=id,materials=Array.Empty<object>()}));
    }
}
