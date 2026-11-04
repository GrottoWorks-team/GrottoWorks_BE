using FluentAssertions;
using TaskExecution.Domain;
using TaskExecution.Domain.Tasks;
using TaskExecution.Domain.Assignments;
using TaskState=TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.UnitTests.Domain;
public sealed class TaskCompletionTests
{
    private static WorkTask Create()=>WorkTask.CreateOpen(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"Work",null,"Finished",TaskPriority.Medium,60,1,null,DateTimeOffset.UtcNow.AddDays(1),DateTimeOffset.UtcNow);
    [Theory]
    [InlineData(-1)] [InlineData(101)]
    public void Progress_rejects_invalid_percent(int percent)
    {
        Action action=()=>TaskProgress.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),percent,null,false,DateTimeOffset.UtcNow);
        action.Should().Throw<DomainRuleException>().Which.Code.Should().Be("TASK_PROGRESS_PERCENT_INVALID");
    }
    [Fact]
    public void Full_progress_requires_manual_review_and_retains_revision_cycle()
    {
        var task=Create();var now=DateTimeOffset.UtcNow;
        task.MarkReady(null,now);task.RecordProgress(false,false,null,now);
        task.Status.Should().Be(TaskState.InProgress);
        task.RecordProgress(true,false,null,now);task.Status.Should().Be(TaskState.Submitted);
        task.ReviewCompletion(false,null,now);task.Status.Should().Be(TaskState.RevisionRequired);
        task.RecordProgress(true,true,null,now);task.ReviewCompletion(true,null,now);
        task.Status.Should().Be(TaskState.Completed);
        Action another=()=>task.RecordProgress(false,false,null,now);another.Should().Throw<DomainRuleException>();
    }
    [Fact]
    public void Revision_requires_reason_and_submission_requires_full_progress()
    {
        Action revision=()=>TaskCompletionReview.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),false," ",DateTimeOffset.UtcNow);
        revision.Should().Throw<DomainRuleException>();
        Action submit=()=>TaskProgress.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),99,null,true,DateTimeOffset.UtcNow);
        submit.Should().Throw<DomainRuleException>();
    }
    [Fact]
    public void Purchase_cannot_use_manual_review_and_delivery_completion_is_idempotent()
    {
        var task=Create();task.SetSpecialType(TaskType.Purchase,Guid.NewGuid());
        Action progress=()=>task.RecordProgress(true,false,null,DateTimeOffset.UtcNow);progress.Should().Throw<DomainRuleException>();
        task.CompletePurchase(DateTimeOffset.UtcNow).Should().BeTrue();var version=task.Version;
        task.CompletePurchase(DateTimeOffset.UtcNow).Should().BeFalse();task.Version.Should().Be(version);
    }
    [Fact]
    public void Approval_requires_submission_and_preserves_author()
    {
        var author=Guid.NewGuid();var leader=Guid.NewGuid();var now=DateTimeOffset.UtcNow;
        var progress=TaskProgress.Create(Guid.NewGuid(),Guid.NewGuid(),author,100,"done",true,now);
        progress.Approve(leader,now.AddSeconds(1));progress.SubmittedByUserId.Should().Be(author);
        progress.ApprovedByUserId.Should().Be(leader);progress.ApprovedAt.Should().NotBeNull();
        Action again=()=>progress.Approve(leader,now.AddSeconds(2));again.Should().Throw<DomainRuleException>();
    }
    [Fact]
    public void Stale_review_and_wrong_resubmission_do_not_change_task()
    {
        var task=Create();var version=task.Version;
        Action submit=()=>task.RecordProgress(true,true,null,DateTimeOffset.UtcNow);submit.Should().Throw<DomainRuleException>();
        task.Version.Should().Be(version);task.Status.Should().Be(TaskState.Open);
        task.MarkReady(null,DateTimeOffset.UtcNow);task.BeginWork(DateTimeOffset.UtcNow);task.RecordProgress(true,false,null,DateTimeOffset.UtcNow);
        Action stale=()=>task.ReviewCompletion(true,version,DateTimeOffset.UtcNow);stale.Should().Throw<DomainRuleException>().Which.Kind.Should().Be(DomainErrorKind.PreconditionFailed);
        task.Status.Should().Be(TaskState.Submitted);
    }
    [Fact]
    public void Assignment_completion_preserves_declined_history()
    {
        var a=TaskAssignment.Assign(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),null,false,false,null,DateTimeOffset.UtcNow);
        a.Decline("Unavailable",null,DateTimeOffset.UtcNow);a.CompleteForTask();a.Status.Should().Be(AssignmentStatus.Declined);
    }
}
