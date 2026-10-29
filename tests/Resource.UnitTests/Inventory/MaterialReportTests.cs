using BuildingBlocks.Security;
using Resource.Application.Inventory;
using Resource.Application.Scope;
using Resource.Application.Shortages;
using Resource.Contracts.Events;
using Resource.Domain;
using Resource.Domain.Catalog;
using Resource.Domain.Inventory;
using Resource.Domain.Scope;

namespace Resource.UnitTests.Inventory;

/// <summary>F-RES-17: task material reports — BR-44..51.</summary>
public sealed class MaterialReportTests
{
    private static readonly Guid Parish = Guid.NewGuid();
    private readonly Guid _officer = Guid.NewGuid();
    private readonly Guid _volunteer = Guid.NewGuid();
    private readonly FakeScopeStore _scopes = new();
    private readonly FakeCatalogStore _catalog = new();
    private readonly FakeRequirementStore _requirements = new();
    private readonly FakeInventoryStore _inventory;
    private readonly FakeTaskProjectionStore _tasks = new();
    private readonly FakeTaskMaterialReportStore _reports = new();
    private readonly FakeIntegrationEventPublisher _events = new();
    private readonly WorkAreaScope _area;
    private readonly Material _cable;
    private readonly Material _bulb;
    private readonly TaskProjection _task;
    private readonly MaterialAllocation _allocation;

    public MaterialReportTests()
    {
        _inventory = new FakeInventoryStore(_requirements);
        _area = _scopes.Seed(Parish, _officer);
        var category = _catalog.SeedCategory();
        _cable = _catalog.SeedMaterial(category, "CABLE", "Dây điện", "m");
        _bulb = _catalog.SeedMaterial(category, "BULB", "Bóng đèn", "cái");
        _requirements.Add(MaterialRequirement.Create(_area.WorkAreaId, _cable.MaterialId, 100, 0, null, DateTimeOffset.UtcNow));
        _inventory.Append(MaterialTransaction.Post(_area.WorkAreaId, _cable.MaterialId, TransactionType.OpeningStock, 100, DateTimeOffset.UtcNow, _officer, null));
        _task = _tasks.Seed(_area.WorkAreaId, ProjectedTaskStatus.Completed, _volunteer);
        _allocation = MaterialAllocation.Create(_area.WorkAreaId, _task.TaskId,
            [new AllocationLineInput(_cable.MaterialId, 30), new AllocationLineInput(_bulb.MaterialId, 10)], DateTimeOffset.UtcNow, null, _officer, DateTimeOffset.UtcNow);
        _inventory.Allocations.Add(_allocation);
    }

    private FakeCurrentUser Volunteer(Guid? id = null) => new(id ?? _volunteer, GrottoWorksRoles.Volunteer, Parish);

    private FakeCurrentUser Officer(Guid? id = null) => new(id ?? _officer, GrottoWorksRoles.MaterialOfficer, Parish);

    private MaterialReportContext Context(FakeCurrentUser user) =>
        new(_tasks, new FakeAllocationStore(_inventory), _catalog, new WorkAreaAccess(_scopes, user));

    private SubmitMaterialReportCommandHandler Submit(FakeCurrentUser? user = null)
    {
        var current = user ?? Volunteer();
        return new(Context(current), _reports, _events, current);
    }

    private VerifyMaterialReportCommandHandler Verify(FakeCurrentUser? user = null)
    {
        var current = user ?? Officer();
        var reader = new InventoryReader(_inventory, _catalog);
        return new(Context(current), _reports, _inventory, new ShortageTracker(reader, _events), new WorkAreaAccess(_scopes, current), _events, current);
    }

    private RequestReportRevisionCommandHandler Revise(FakeCurrentUser? user = null)
    {
        var current = user ?? Officer();
        return new(Context(current), _reports, new WorkAreaAccess(_scopes, current));
    }

    private Guid ItemOf(Material material) => _allocation.Items.Single(item => item.MaterialId == material.MaterialId).MaterialAllocationItemId;

    private SubmitMaterialReportCommand Report(decimal cableRemaining = 8, decimal cableDamaged = 2, decimal cableLost = 0, decimal bulbRemaining = 0) =>
        new(_task.TaskId,
            [new ReportedLineInput(ItemOf(_cable), cableRemaining, cableDamaged, cableLost, null), new ReportedLineInput(ItemOf(_bulb), bulbRemaining, 1, 0, "1 bóng vỡ")],
            "Xong việc");

    private VerifyMaterialReportCommand Verified(TaskMaterialReportDto report, decimal cableRemaining = 7, decimal cableDamaged = 2, decimal cableLost = 1) =>
        new(report.Id, report.Version,
            [new VerifiedLineInput(report.Items.Single(i => i.MaterialId == _cable.MaterialId).Id, cableRemaining, cableDamaged, cableLost),
             new VerifiedLineInput(report.Items.Single(i => i.MaterialId == _bulb.MaterialId).Id, 0, 1, 0)]);

    private static async Task<DomainRuleException> Fails(Func<Task> act) =>
        (await act.Should().ThrowAsync<DomainRuleException>()).Which;

    [Fact]
    public async Task Volunteer_reports_every_allocated_item_after_completion()
    {
        var dto = await Submit().HandleAsync(Report());

        dto.Status.Should().Be(MaterialReportStatus.Submitted);
        dto.Items.Should().HaveCount(2);
        var cable = dto.Items.Single(i => i.MaterialId == _cable.MaterialId);
        cable.AllocatedQuantity.Should().Be(30);
        cable.ReportedRemainingQuantity.Should().Be(8);
        cable.VerifiedRemainingQuantity.Should().BeNull();
        cable.UsedQuantity.Should().BeNull();
        _inventory.Ledger.Should().ContainSingle(); // BR-46: reporting changes nothing
        var payload = _events.Events.Single().Payload.Should().BeOfType<MaterialReportSubmitted>().Which;
        payload.MaterialOfficerUserId.Should().Be(_officer);
        payload.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Report_waits_for_completion_and_comes_from_an_assignee()
    {
        var running = _tasks.Seed(_area.WorkAreaId, ProjectedTaskStatus.InProgress, _volunteer);
        (await Fails(() => Submit().HandleAsync(Report() with { TaskId = running.TaskId }))).Code.Should().Be("TASK_NOT_COMPLETED");
        (await Fails(() => Submit(Volunteer(Guid.NewGuid())).HandleAsync(Report()))).Code.Should().Be("MATERIAL_REPORT_NOT_ASSIGNEE");
        (await Fails(() => Submit().HandleAsync(Report() with { TaskId = Guid.NewGuid() }))).Code.Should().Be("TASK_NOT_FOUND");

        var nothingAllocated = _tasks.Seed(_area.WorkAreaId, ProjectedTaskStatus.Completed, _volunteer);
        (await Fails(() => Submit().HandleAsync(new SubmitMaterialReportCommand(nothingAllocated.TaskId, [], null)))).Code.Should().Be("TASK_HAS_NO_ALLOCATION");
    }

    [Fact]
    public async Task Lines_cover_exactly_the_allocation_items_and_never_exceed_allocated()
    {
        (await Fails(() => Submit().HandleAsync(new SubmitMaterialReportCommand(_task.TaskId, [new ReportedLineInput(ItemOf(_cable), 1, 0, 0, null)], null))))
            .Code.Should().Be("MATERIAL_REPORT_ITEMS_MISMATCH");
        (await Fails(() => Submit().HandleAsync(new SubmitMaterialReportCommand(_task.TaskId,
            [new ReportedLineInput(ItemOf(_cable), 1, 0, 0, null), new ReportedLineInput(Guid.NewGuid(), 1, 0, 0, null)], null))))
            .Code.Should().Be("MATERIAL_REPORT_ITEMS_MISMATCH");
        (await Fails(() => Submit().HandleAsync(Report(cableRemaining: 20, cableDamaged: 10, cableLost: 1)))).Code.Should().Be("MATERIAL_REPORT_QUANTITY_INVALID");
        (await Fails(() => Submit().HandleAsync(Report(cableRemaining: -1)))).Code.Should().Be("MATERIAL_REPORT_QUANTITY_INVALID");
    }

    [Fact]
    public async Task One_report_per_task_until_sent_back()
    {
        await Submit().HandleAsync(Report());
        (await Fails(() => Submit().HandleAsync(Report()))).Code.Should().Be("MATERIAL_REPORT_ALREADY_SUBMITTED");
    }

    [Fact]
    public async Task Verification_returns_only_verified_remaining_to_the_same_work_area()
    {
        var submitted = await Submit().HandleAsync(Report());

        var verified = await Verify().HandleAsync(Verified(submitted));

        verified.Status.Should().Be(MaterialReportStatus.Verified);
        verified.VerifiedBy.Should().Be(_officer);
        var cable = verified.Items.Single(i => i.MaterialId == _cable.MaterialId);
        cable.VerifiedRemainingQuantity.Should().Be(7);
        cable.UsedQuantity.Should().Be(20); // 30 − 7 − 2 − 1
        cable.AllocatedQuantity.Should().Be(30); // BR-51

        var returned = _inventory.Ledger.Single(t => t.Type == TransactionType.TaskReturn);
        returned.Quantity.Should().Be(7);
        returned.WorkAreaId.Should().Be(_area.WorkAreaId);
        returned.ReferenceType.Should().Be(TransactionReferenceType.TaskMaterialReportItem);
        _inventory.Ledger.Should().HaveCount(2); // nothing for damaged/lost (BR-50) nor for the bulbs (0 remaining)
        var row = (await new InventoryReader(_inventory, _catalog).ReadAsync([_area], _cable.MaterialId)).Single();
        row.Unallocated.Should().Be(77); // 100 − 30 allocated + 7 returned
        _events.Events.Should().Contain(e => e.EventType == MaterialReportVerified.EventType);
    }

    [Fact]
    public async Task Verified_values_are_checked_and_only_the_officer_of_the_area_verifies()
    {
        var submitted = await Submit().HandleAsync(Report());

        (await Fails(() => Verify().HandleAsync(Verified(submitted, cableRemaining: 30)))).Code.Should().Be("MATERIAL_REPORT_VERIFIED_QUANTITY_INVALID");
        (await Fails(() => Verify().HandleAsync(Verified(submitted) with { Items = [Verified(submitted).Items[0]] }))).Code.Should().Be("MATERIAL_REPORT_ITEMS_MISMATCH");
        (await Fails(() => Verify(Officer(Guid.NewGuid())).HandleAsync(Verified(submitted)))).Code.Should().Be("WORK_AREA_SCOPE_DENIED");
        _inventory.Ledger.Should().ContainSingle();

        await Verify().HandleAsync(Verified(submitted));
        (await Fails(() => Verify().HandleAsync(Verified(submitted)))).Code.Should().Be("MATERIAL_REPORT_INVALID_STATE");
    }

    [Fact]
    public async Task Revision_sends_it_back_with_a_reason_and_the_volunteer_resubmits()
    {
        var submitted = await Submit().HandleAsync(Report());

        (await Fails(() => Revise().HandleAsync(new RequestReportRevisionCommand(submitted.Id, null, "")))).Code.Should().Be("MATERIAL_REPORT_REASON_REQUIRED");
        var revised = await Revise().HandleAsync(new RequestReportRevisionCommand(submitted.Id, null, "Đếm lại số dây còn"));
        revised.Status.Should().Be(MaterialReportStatus.RevisionRequired);
        revised.RevisionReason.Should().Be("Đếm lại số dây còn");

        var resubmitted = await Submit().HandleAsync(Report(cableRemaining: 9, cableDamaged: 1));
        resubmitted.Id.Should().Be(submitted.Id);
        resubmitted.Status.Should().Be(MaterialReportStatus.Submitted);
        resubmitted.Items.Single(i => i.MaterialId == _cable.MaterialId).ReportedRemainingQuantity.Should().Be(9);
    }

    [Fact]
    public async Task List_shows_the_report_to_officers_and_assignees_only()
    {
        await Submit().HandleAsync(Report());

        (await new ListTaskMaterialReportsQueryHandler(Context(Officer()), _reports, Officer()).HandleAsync(_task.TaskId)).Should().ContainSingle();
        (await new ListTaskMaterialReportsQueryHandler(Context(Volunteer()), _reports, Volunteer()).HandleAsync(_task.TaskId)).Should().ContainSingle();
        var stranger = Volunteer(Guid.NewGuid());
        (await Fails(() => new ListTaskMaterialReportsQueryHandler(Context(stranger), _reports, stranger).HandleAsync(_task.TaskId)))
            .Code.Should().Be("TASK_NOT_FOUND");
    }
}
