using Readiness.Domain;
using Readiness.Domain.Checklists;

namespace Readiness.UnitTests;

public sealed class ReadinessChecklistTests
{
    private static readonly Guid Leader = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static ReadinessChecklist Create(params ItemDefinition[] items) =>
        ReadinessChecklist.Create(Guid.NewGuid(), "Kiểm tra hang đá", items.Length > 0
            ? items
            : [new("Điện an toàn", null, true), new("Trang trí", "Hoa, đèn", false)]);

    [Fact]
    public void Create_starts_in_planning_with_pending_items_in_order()
    {
        var checklist = Create();

        checklist.Status.Should().Be(ChecklistStatus.Planning);
        checklist.Items.Select(item => item.Name).Should().Equal("Điện an toàn", "Trang trí");
        checklist.Items.Select(item => item.Position).Should().Equal(0, 1);
        checklist.Items.Should().OnlyContain(item => item.Status == ItemStatus.Pending);
    }

    [Fact]
    public void Create_rejects_empty_or_duplicate_items()
    {
        FluentActions.Invoking(() => ReadinessChecklist.Create(Guid.NewGuid(), "A", []))
            .Should().Throw<DomainRuleException>().Which.Code.Should().Be("READINESS_ITEMS_INVALID");
        FluentActions.Invoking(() => Create(new("Điện", null, true), new(" điện ", null, false)))
            .Should().Throw<DomainRuleException>().Which.Code.Should().Be("READINESS_DUPLICATE_ITEM");
        FluentActions.Invoking(() => ReadinessChecklist.Create(Guid.NewGuid(), " ", [new("A", null, true)]))
            .Should().Throw<DomainRuleException>().Which.Code.Should().Be("READINESS_NAME_INVALID");
    }

    [Fact]
    public void Items_can_only_be_replaced_while_planning()
    {
        var checklist = Create();
        checklist.ReplaceItems([new("Lối đi thông thoáng", null, true)]);
        checklist.Items.Should().ContainSingle();

        checklist.RecordResult(checklist.Items[0].ReadinessItemId, InspectionResult.Passed, null, Leader, Now);

        FluentActions.Invoking(() => checklist.ReplaceItems([new("X", null, true)]))
            .Should().Throw<DomainRuleException>().Which.Code.Should().Be("READINESS_INVALID_STATE");
    }

    [Fact]
    public void Failed_mandatory_item_needs_correction_and_blocks_confirm()
    {
        var checklist = Create();
        var mandatory = checklist.Items[0];

        checklist.RecordResult(mandatory.ReadinessItemId, InspectionResult.Failed, "Dây hở", Leader, Now);

        checklist.Status.Should().Be(ChecklistStatus.NeedsCorrection);
        mandatory.Status.Should().Be(ItemStatus.Failed);
        FluentActions.Invoking(() => checklist.Confirm(Leader, Now))
            .Should().Throw<DomainRuleException>().Which.Code.Should().Be("READINESS_MANDATORY_NOT_PASSED");
    }

    [Fact]
    public void Failed_optional_item_does_not_block_readiness()
    {
        var checklist = Create();
        checklist.RecordResult(checklist.Items[0].ReadinessItemId, InspectionResult.Passed, null, Leader, Now);
        checklist.RecordResult(checklist.Items[1].ReadinessItemId, InspectionResult.Failed, null, Leader, Now);

        checklist.Status.Should().Be(ChecklistStatus.InReview);
        checklist.Confirm(Leader, Now);

        checklist.Status.Should().Be(ChecklistStatus.Ready);
        checklist.CompletedByUserId.Should().Be(Leader);
    }

    [Fact]
    public void Mandatory_failed_item_is_rechecked_only_after_a_corrective_task()
    {
        var checklist = Create();
        var item = checklist.Items[0];
        checklist.RecordResult(item.ReadinessItemId, InspectionResult.Failed, null, Leader, Now);

        FluentActions.Invoking(() => checklist.Recheck(item.ReadinessItemId, InspectionResult.Passed, null, Leader, Now))
            .Should().Throw<DomainRuleException>().Which.Message.Should().Contain("BR-66");

        var taskId = Guid.NewGuid();
        checklist.LinkCorrectiveTask(item.ReadinessItemId, taskId, taskCompleted: false);
        item.Status.Should().Be(ItemStatus.CorrectiveAction);
        item.CorrectiveTaskId.Should().Be(taskId);

        checklist.CorrectiveTaskCompleted(taskId).Should().BeTrue();
        item.Status.Should().Be(ItemStatus.Recheck);
        checklist.Status.Should().Be(ChecklistStatus.NeedsCorrection);

        checklist.Recheck(item.ReadinessItemId, InspectionResult.Passed, "Đã sửa", Leader, Now);
        item.Status.Should().Be(ItemStatus.Passed);
        checklist.Status.Should().Be(ChecklistStatus.InReview);
    }

    [Fact]
    public void Linking_an_already_completed_task_goes_straight_to_recheck()
    {
        var checklist = Create();
        var item = checklist.Items[0];
        checklist.RecordResult(item.ReadinessItemId, InspectionResult.Failed, null, Leader, Now);

        checklist.LinkCorrectiveTask(item.ReadinessItemId, Guid.NewGuid(), taskCompleted: true);

        item.Status.Should().Be(ItemStatus.Recheck);
    }

    [Fact]
    public void Failed_recheck_keeps_the_corrective_task()
    {
        var checklist = Create();
        var item = checklist.Items[0];
        checklist.RecordResult(item.ReadinessItemId, InspectionResult.Failed, null, Leader, Now);
        var taskId = Guid.NewGuid();
        checklist.LinkCorrectiveTask(item.ReadinessItemId, taskId, taskCompleted: true);

        checklist.Recheck(item.ReadinessItemId, InspectionResult.Failed, "Vẫn hở", Leader, Now);

        item.Status.Should().Be(ItemStatus.Failed);
        item.CorrectiveTaskId.Should().Be(taskId);
        checklist.Status.Should().Be(ChecklistStatus.NeedsCorrection);
    }

    [Fact]
    public void Pending_item_cannot_get_a_corrective_task_and_results_are_recorded_once()
    {
        var checklist = Create();
        var item = checklist.Items[0];

        FluentActions.Invoking(() => checklist.LinkCorrectiveTask(item.ReadinessItemId, Guid.NewGuid(), false))
            .Should().Throw<DomainRuleException>().Which.Code.Should().Be("READINESS_ITEM_INVALID_STATE");

        checklist.RecordResult(item.ReadinessItemId, InspectionResult.Passed, null, Leader, Now);
        FluentActions.Invoking(() => checklist.RecordResult(item.ReadinessItemId, InspectionResult.Failed, null, Leader, Now))
            .Should().Throw<DomainRuleException>().Which.Code.Should().Be("READINESS_ITEM_INVALID_STATE");
    }

    [Fact]
    public void Ready_checklist_is_frozen_until_reopened_with_a_reason()
    {
        var checklist = Create(new ItemDefinition("Điện an toàn", null, true));
        var item = checklist.Items[0];
        checklist.RecordResult(item.ReadinessItemId, InspectionResult.Passed, null, Leader, Now);
        checklist.Confirm(Leader, Now);

        FluentActions.Invoking(() => checklist.RecordResult(item.ReadinessItemId, InspectionResult.Failed, null, Leader, Now))
            .Should().Throw<DomainRuleException>().Which.Code.Should().Be("READINESS_INVALID_STATE");
        FluentActions.Invoking(() => checklist.Reopen("  "))
            .Should().Throw<DomainRuleException>().Which.Code.Should().Be("READINESS_REOPEN_REASON_REQUIRED");

        checklist.Reopen("Mưa làm hỏng đèn");

        checklist.Status.Should().Be(ChecklistStatus.Reopened);
        checklist.ReopenReason.Should().Be("Mưa làm hỏng đèn");
        checklist.CompletedAt.Should().BeNull();

        // After a reopen a PASSED item can be inspected again.
        checklist.RecordResult(item.ReadinessItemId, InspectionResult.Failed, null, Leader, Now);
        checklist.Status.Should().Be(ChecklistStatus.NeedsCorrection);
    }

    [Fact]
    public void Reopened_checklist_stays_reopened_until_confirmed_again()
    {
        var checklist = Create(new ItemDefinition("Điện an toàn", null, true), new ItemDefinition("Hoa", null, false));
        checklist.RecordResult(checklist.Items[0].ReadinessItemId, InspectionResult.Passed, null, Leader, Now);
        checklist.Confirm(Leader, Now);
        checklist.Reopen("Kiểm tra lại");

        checklist.RecordResult(checklist.Items[1].ReadinessItemId, InspectionResult.Passed, null, Leader, Now);

        checklist.Status.Should().Be(ChecklistStatus.Reopened);
        checklist.Confirm(Leader, Now);
        checklist.Status.Should().Be(ChecklistStatus.Ready);
        checklist.ReopenReason.Should().BeNull();
    }

    [Fact]
    public void Planning_checklist_cannot_be_confirmed_or_reopened()
    {
        var checklist = Create();

        FluentActions.Invoking(() => checklist.Confirm(Leader, Now))
            .Should().Throw<DomainRuleException>().Which.Code.Should().Be("READINESS_INVALID_STATE");
        FluentActions.Invoking(() => checklist.Reopen("x"))
            .Should().Throw<DomainRuleException>().Which.Code.Should().Be("READINESS_INVALID_STATE");
    }

    [Fact]
    public void Evidence_is_merged_and_capped()
    {
        var checklist = Create();
        var item = checklist.Items[0];
        var file = Guid.NewGuid();

        item.AttachEvidence([file]);
        item.AttachEvidence([file, Guid.NewGuid()]);
        item.EvidenceFileIds.Should().HaveCount(2);

        FluentActions.Invoking(() => item.AttachEvidence(Enumerable.Range(0, 9).Select(_ => Guid.NewGuid()).ToList()))
            .Should().Throw<DomainRuleException>().Which.Code.Should().Be("READINESS_TOO_MANY_EVIDENCE");
        FluentActions.Invoking(() => item.AttachEvidence([]))
            .Should().Throw<DomainRuleException>().Which.Code.Should().Be("READINESS_EVIDENCE_REQUIRED");
    }

    [Fact]
    public void Unknown_item_is_not_found()
    {
        FluentActions.Invoking(() => Create().Item(Guid.NewGuid()))
            .Should().Throw<DomainRuleException>().Which.Kind.Should().Be(DomainErrorKind.NotFound);
    }
}
