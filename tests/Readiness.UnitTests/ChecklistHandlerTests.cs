using BuildingBlocks.Security;
using Readiness.Application.Checklists;
using Readiness.Application.Scope;
using Readiness.Contracts.Events;
using Readiness.Domain;
using Readiness.Domain.Checklists;
using Readiness.Domain.Scope;

namespace Readiness.UnitTests;

public sealed class ChecklistHandlerTests
{
    private readonly Guid _parishId = Guid.NewGuid();
    private readonly Guid _leaderId = Guid.NewGuid();
    private readonly FakeScopeStore _scopes = new();
    private readonly FakeTaskProjectionStore _tasks = new();
    private readonly FakeReadinessCheckStore _store = new();
    private readonly FakeIntegrationEventPublisher _publisher = new();

    private WorkAreaAccess Access(ICurrentUser user) => new(_scopes, user);

    private FakeCurrentUser LeaderUser() => new(_leaderId, GrottoWorksRoles.Leader, _parishId);

    private InspectionCommandHandler Inspection(ICurrentUser user) =>
        new(new ChecklistLoader(_store, Access(user)), _store, _tasks, _publisher, user);

    private async Task<(WorkAreaScope Scope, ReadinessCheckDto Check)> CreateAsync(
        SeasonStatus seasonStatus = SeasonStatus.Active,
        WorkAreaStatus status = WorkAreaStatus.ReadinessReview)
    {
        var scope = _scopes.Seed(_parishId, _leaderId, seasonStatus: seasonStatus, status: status);
        var check = await new CreateReadinessCheckCommandHandler(Access(LeaderUser()), _store).HandleAsync(
            new CreateReadinessCheckCommand(scope.WorkAreaId, "Kiểm tra", [new("Điện an toàn", null, true), new("Hoa", null, false)]));
        return (scope, check);
    }

    [Fact]
    public async Task Leader_creates_one_checklist_per_work_area()
    {
        var (scope, check) = await CreateAsync();

        check.Status.Should().Be(ChecklistStatus.Planning);
        check.WorkAreaName.Should().Be(scope.Name);
        check.Items.Should().HaveCount(2);

        var again = () => new CreateReadinessCheckCommandHandler(Access(LeaderUser()), _store).HandleAsync(
            new CreateReadinessCheckCommand(scope.WorkAreaId, "Lần 2", [new("A", null, true)]));
        (await again.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("READINESS_CHECK_EXISTS");
    }

    [Fact]
    public async Task Another_leader_cannot_create_or_inspect()
    {
        var (scope, check) = await CreateAsync();
        var other = new FakeCurrentUser(Guid.NewGuid(), GrottoWorksRoles.Leader, _parishId);

        var create = () => new CreateReadinessCheckCommandHandler(Access(other), _store).HandleAsync(
            new CreateReadinessCheckCommand(scope.WorkAreaId, "X", [new("A", null, true)]));
        (await create.Should().ThrowAsync<DomainRuleException>()).Which.Kind.Should().Be(DomainErrorKind.Forbidden);

        var inspect = () => Inspection(other).RecordResultAsync(
            new ItemResultCommand(check.Id, check.Items[0].Id, null, InspectionResult.Passed, null, []));
        (await inspect.Should().ThrowAsync<DomainRuleException>()).Which.Kind.Should().Be(DomainErrorKind.Forbidden);
    }

    [Fact]
    public async Task Checklist_of_another_parish_is_not_found()
    {
        var (_, check) = await CreateAsync();
        var outsider = new FakeCurrentUser(Guid.NewGuid(), GrottoWorksRoles.Parish, Guid.NewGuid());

        var get = () => new ChecklistDefinitionHandler(new ChecklistLoader(_store, Access(outsider)), _store).GetAsync(check.Id);

        (await get.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("READINESS_CHECK_NOT_FOUND");
    }

    [Fact]
    public async Task Archived_season_is_read_only()
    {
        var (_, check) = await CreateAsync();
        _scopes.Scopes[0].ApplySeasonStatus(SeasonStatus.Archived, DateTimeOffset.UtcNow);

        var inspect = () => Inspection(LeaderUser()).RecordResultAsync(
            new ItemResultCommand(check.Id, check.Items[0].Id, null, InspectionResult.Passed, null, []));

        (await inspect.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("SEASON_READ_ONLY");
    }

    [Fact]
    public async Task Stale_version_is_rejected()
    {
        var (_, check) = await CreateAsync();

        var replace = () => new ChecklistDefinitionHandler(new ChecklistLoader(_store, Access(LeaderUser())), _store)
            .ReplaceItemsAsync(new ReplaceItemsCommand(check.Id, check.Version + 5, [new("A", null, true)]));

        (await replace.Should().ThrowAsync<DomainRuleException>()).Which.Kind.Should().Be(DomainErrorKind.PreconditionFailed);
    }

    [Fact]
    public async Task Failed_result_publishes_item_failed_and_keeps_evidence()
    {
        var (scope, check) = await CreateAsync();
        var file = Guid.NewGuid();

        var item = await Inspection(LeaderUser()).RecordResultAsync(
            new ItemResultCommand(check.Id, check.Items[0].Id, check.Version, InspectionResult.Failed, "Dây hở", [file]));

        item.Status.Should().Be(ItemStatus.Failed);
        item.EvidenceFileIds.Should().Equal(file);
        var published = _publisher.Events.Should().ContainSingle().Which;
        published.EventType.Should().Be(ReadinessItemFailed.EventType);
        published.Payload.Should().BeOfType<ReadinessItemFailed>().Which.WorkAreaId.Should().Be(scope.WorkAreaId);
    }

    [Fact]
    public async Task Corrective_task_must_be_an_open_corrective_task_of_the_area()
    {
        var (scope, check) = await CreateAsync();
        var handler = Inspection(LeaderUser());
        await handler.RecordResultAsync(new ItemResultCommand(check.Id, check.Items[0].Id, null, InspectionResult.Failed, null, []));

        var missing = () => handler.LinkCorrectiveTaskAsync(new LinkCorrectiveTaskCommand(check.Id, check.Items[0].Id, null, null));
        (await missing.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("CORRECTIVE_TASK_CREATE_UNAVAILABLE");

        var workTask = _tasks.Seed(scope.WorkAreaId, ProjectedTaskType.Work);
        var wrongType = () => handler.LinkCorrectiveTaskAsync(new LinkCorrectiveTaskCommand(check.Id, check.Items[0].Id, null, workTask.TaskId));
        (await wrongType.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("CORRECTIVE_TASK_INVALID");

        var otherArea = _tasks.Seed(Guid.NewGuid());
        var wrongArea = () => handler.LinkCorrectiveTaskAsync(new LinkCorrectiveTaskCommand(check.Id, check.Items[0].Id, null, otherArea.TaskId));
        (await wrongArea.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("CORRECTIVE_TASK_INVALID");

        var corrective = _tasks.Seed(scope.WorkAreaId);
        var linked = await handler.LinkCorrectiveTaskAsync(new LinkCorrectiveTaskCommand(check.Id, check.Items[0].Id, null, corrective.TaskId));
        linked.Status.Should().Be(ItemStatus.CorrectiveAction);
        linked.CorrectiveTaskId.Should().Be(corrective.TaskId);
    }

    [Fact]
    public async Task Completed_corrective_task_moves_the_item_to_recheck()
    {
        var (scope, check) = await CreateAsync();
        var handler = Inspection(LeaderUser());
        await handler.RecordResultAsync(new ItemResultCommand(check.Id, check.Items[0].Id, null, InspectionResult.Failed, null, []));
        var corrective = _tasks.Seed(scope.WorkAreaId);
        await handler.LinkCorrectiveTaskAsync(new LinkCorrectiveTaskCommand(check.Id, check.Items[0].Id, null, corrective.TaskId));

        var apply = new ApplyTaskSnapshotHandler(_tasks, _scopes, new CorrectiveTaskSync(_store));
        await apply.HandleAsync(new TaskSnapshot(corrective.TaskId, scope.WorkAreaId, "Sửa dây điện", ProjectedTaskType.Corrective,
            ProjectedTaskStatus.Completed, null, [], 2));

        _store.Checklists[0].Items[0].Status.Should().Be(ItemStatus.Recheck);

        var rechecked = await handler.RecheckAsync(new ItemResultCommand(check.Id, check.Items[0].Id, null, InspectionResult.Passed, null, []));
        rechecked.Status.Should().Be(ItemStatus.Passed);
    }

    [Fact]
    public async Task Volunteer_of_the_corrective_task_attaches_evidence_others_cannot()
    {
        var (scope, check) = await CreateAsync();
        var volunteerId = Guid.NewGuid();
        var handler = Inspection(LeaderUser());
        await handler.RecordResultAsync(new ItemResultCommand(check.Id, check.Items[0].Id, null, InspectionResult.Failed, null, []));
        var corrective = _tasks.Seed(scope.WorkAreaId, assignees: volunteerId);
        await handler.LinkCorrectiveTaskAsync(new LinkCorrectiveTaskCommand(check.Id, check.Items[0].Id, null, corrective.TaskId));

        AttachEvidenceCommandHandler Attach(Guid userId) =>
            new(new ChecklistLoader(_store, Access(new FakeCurrentUser(userId, GrottoWorksRoles.Volunteer, _parishId))), _store, _tasks,
                new FakeCurrentUser(userId, GrottoWorksRoles.Volunteer, _parishId));

        var item = await Attach(volunteerId).HandleAsync(new AttachEvidenceCommand(check.Id, check.Items[0].Id, [Guid.NewGuid()]));
        item.EvidenceFileIds.Should().ContainSingle();

        var stranger = () => Attach(Guid.NewGuid()).HandleAsync(new AttachEvidenceCommand(check.Id, check.Items[0].Id, [Guid.NewGuid()]));
        (await stranger.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("READINESS_EVIDENCE_DENIED");

        // Volunteers of another item's task (none here) or an item without a corrective task are denied too.
        var noTask = () => Attach(volunteerId).HandleAsync(new AttachEvidenceCommand(check.Id, check.Items[1].Id, [Guid.NewGuid()]));
        (await noTask.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("READINESS_EVIDENCE_DENIED");
    }

    [Fact]
    public async Task Confirm_publishes_area_ready_and_parish_can_reopen()
    {
        var (_, check) = await CreateAsync();
        await Inspection(LeaderUser()).RecordResultAsync(new ItemResultCommand(check.Id, check.Items[0].Id, null, InspectionResult.Passed, null, []));

        var leaderDecision = new ReadinessDecisionHandler(new ChecklistLoader(_store, Access(LeaderUser())), _store, _publisher, LeaderUser());
        var ready = await leaderDecision.ConfirmAsync(new CheckCommand(check.Id, null));
        ready.Status.Should().Be(ChecklistStatus.Ready);
        _publisher.Events.Should().ContainSingle(e => e.EventType == ReadinessAreaReady.EventType);

        var parish = new FakeCurrentUser(Guid.NewGuid(), GrottoWorksRoles.Parish, _parishId);
        var parishDecision = new ReadinessDecisionHandler(new ChecklistLoader(_store, Access(parish)), _store, _publisher, parish);
        var reopened = await parishDecision.ReopenAsync(new CheckCommand(check.Id, null, "Mưa làm hỏng đèn"));

        reopened.Status.Should().Be(ChecklistStatus.Reopened);
        reopened.ReopenReason.Should().Be("Mưa làm hỏng đèn");
        _publisher.Events.Should().ContainSingle(e => e.EventType == ReadinessAreaReopened.EventType);
    }

    [Fact]
    public async Task Confirm_is_blocked_while_a_mandatory_item_is_not_passed()
    {
        var (_, check) = await CreateAsync();
        await Inspection(LeaderUser()).RecordResultAsync(new ItemResultCommand(check.Id, check.Items[1].Id, null, InspectionResult.Passed, null, []));

        var confirm = () => new ReadinessDecisionHandler(new ChecklistLoader(_store, Access(LeaderUser())), _store, _publisher, LeaderUser())
            .ConfirmAsync(new CheckCommand(check.Id, null));

        (await confirm.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("READINESS_MANDATORY_NOT_PASSED");
        _publisher.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Season_readiness_summarises_every_work_area_of_the_parish()
    {
        var (scope, check) = await CreateAsync();
        _scopes.Seed(_parishId, Guid.NewGuid(), scope.SeasonId, "Đường kiệu");
        _scopes.Seed(Guid.NewGuid(), Guid.NewGuid(), scope.SeasonId, "Giáo xứ khác");
        await Inspection(LeaderUser()).RecordResultAsync(new ItemResultCommand(check.Id, check.Items[0].Id, null, InspectionResult.Failed, null, []));

        var mo = new FakeCurrentUser(Guid.NewGuid(), GrottoWorksRoles.MaterialOfficer, _parishId);
        var season = await new GetSeasonReadinessQueryHandler(Access(mo), _scopes, _store).HandleAsync(scope.SeasonId);

        season.WorkAreas.Should().Be(2);
        season.ReadyWorkAreas.Should().Be(0);
        var area = season.Areas.Single(row => row.WorkAreaId == scope.WorkAreaId);
        area.ChecklistStatus.Should().Be(ChecklistStatus.NeedsCorrection);
        area.FailedItems.Should().Be(1);
        area.PendingItems.Should().Be(1);
        season.Areas.Single(row => row.WorkAreaName == "Đường kiệu").ReadinessCheckId.Should().BeNull();

        var unknown = () => new GetSeasonReadinessQueryHandler(Access(mo), _scopes, _store).HandleAsync(Guid.NewGuid());
        (await unknown.Should().ThrowAsync<DomainRuleException>()).Which.Kind.Should().Be(DomainErrorKind.NotFound);
    }

    [Fact]
    public async Task Confirm_needs_the_work_area_in_readiness_review()
    {
        var (_, check) = await CreateAsync(status: WorkAreaStatus.InProgress);
        await Inspection(LeaderUser()).RecordResultAsync(new ItemResultCommand(check.Id, check.Items[0].Id, null, InspectionResult.Passed, null, []));

        var confirm = () => new ReadinessDecisionHandler(new ChecklistLoader(_store, Access(LeaderUser())), _store, _publisher, LeaderUser())
            .ConfirmAsync(new CheckCommand(check.Id, null));

        (await confirm.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("WORK_AREA_NOT_IN_READINESS_REVIEW");
        _store.Checklists[0].Status.Should().Be(ChecklistStatus.InReview);
    }
}
