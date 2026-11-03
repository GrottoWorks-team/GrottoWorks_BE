using FluentAssertions;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;
using ParishCoordination.Infrastructure.Lifecycle;

namespace ParishCoordination.UnitTests;

public sealed class WorkAreaLifecycleTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-03T12:00:00Z");

    [Fact]
    public void Recruitment_does_not_start_work_and_started_task_does()
    {
        var area = Area();
        area.ReconcileExecution(1, 0, false, false, false, false, Now).Should().BeFalse();
        area.Status.Should().Be(WorkAreaStatus.Planned);
        area.ReconcileExecution(1, 0, true, false, false, false, Now).Should().BeTrue();
        area.Status.Should().Be(WorkAreaStatus.InProgress);
        area.Version.Should().Be(2);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(2, 1, true)]
    [InlineData(2, 2, false)]
    public void Empty_cancelled_incomplete_or_unverified_tasks_never_enter_review(long eligible, long completed, bool verified)
    {
        var area = Area(WorkAreaStatus.InProgress);
        area.ReconcileExecution(eligible, completed, true, verified, true, false, Now);
        area.Status.Should().Be(WorkAreaStatus.InProgress);
    }

    [Fact]
    public void Completed_work_enters_review_then_readiness_and_new_work_revokes_ready()
    {
        var area = Area();
        area.ReconcileExecution(2, 2, true, true, false, false, Now);
        area.Status.Should().Be(WorkAreaStatus.ReadinessReview);
        area.ReconcileExecution(2, 2, true, true, true, false, Now);
        area.Status.Should().Be(WorkAreaStatus.Ready);
        area.ReconcileExecution(3, 2, true, false, false, false, Now);
        area.Status.Should().Be(WorkAreaStatus.InProgress);
    }

    [Fact]
    public void Reopen_requires_correction_and_completed_area_is_terminal()
    {
        var area = Area(WorkAreaStatus.Ready);
        area.ReconcileExecution(1, 1, true, true, false, true, Now);
        area.Status.Should().Be(WorkAreaStatus.NeedsCorrection);
        var completed = Area(WorkAreaStatus.Completed);
        completed.ReconcileExecution(1, 0, true, false, false, true, Now).Should().BeFalse();
        completed.Version.Should().Be(1);
    }

    [Fact]
    public void Duplicate_and_out_of_order_task_versions_preserve_current_state()
    {
        var projection = new LifecycleTaskProjection();
        projection.Apply(5, "COMPLETED", "WORK").Should().BeTrue();
        projection.Apply(3, "IN_PROGRESS", "WORK").Should().BeFalse();
        projection.Apply(5, "COMPLETED", "WORK").Should().BeFalse();
        projection.Status.Should().Be("COMPLETED");
        projection.SourceVersion.Should().Be(5);
        var conflict = () => projection.Apply(5, "OPEN", "WORK");
        conflict.Should().Throw<FormatException>();
    }

    [Fact]
    public void Same_version_snapshot_can_enrich_an_earlier_status_event()
    {
        var projection = new LifecycleTaskProjection();
        projection.Apply(4, "IN_PROGRESS", null);
        projection.Apply(4, "IN_PROGRESS", "CORRECTIVE").Should().BeTrue();
        projection.TaskType.Should().Be("CORRECTIVE");
    }

    [Fact]
    public void Older_ready_and_equal_timestamp_ready_cannot_override_reopen()
    {
        var state = new LifecycleReadinessProjection();
        var checklist = Guid.NewGuid();
        state.Apply(checklist, Now, true).Should().BeTrue();
        state.Apply(checklist, Now.AddSeconds(-1), false).Should().BeFalse();
        state.Apply(checklist, Now, false).Should().BeFalse();
        state.Reopened.Should().BeTrue();
        state.Apply(checklist, Now.AddSeconds(1), false).Should().BeTrue();
        state.Reopened.Should().BeFalse();
    }

    [Fact]
    public void Reopen_wins_timestamp_tie_even_when_ready_arrives_first()
    {
        var state = new LifecycleReadinessProjection();
        state.Apply(Guid.NewGuid(), Now, false);
        state.Apply(Guid.NewGuid(), Now, true).Should().BeTrue();
        state.Reopened.Should().BeTrue();
    }

    private static WorkArea Area(WorkAreaStatus status = WorkAreaStatus.Planned) => WorkArea.Create(
        Guid.NewGuid(), null, null, null, null, "Area", null, null, null, status, Now);
}
