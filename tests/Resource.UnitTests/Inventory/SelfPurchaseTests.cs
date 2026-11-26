using BuildingBlocks.Security;
using Resource.Application.Files;
using Resource.Application.Inventory;
using Resource.Application.Scope;
using Resource.Application.Shortages;
using Resource.Domain;
using Resource.Domain.Catalog;
using Resource.Domain.Inventory;
using Resource.Domain.Purchases;
using Resource.Domain.Scope;

namespace Resource.UnitTests.Inventory;

/// <summary>
/// 26/11/2026: volunteers never take a purchase task. When a task runs out of materials mid-way they may buy
/// the goods themselves, but ONLY for less than 500,000 VND and with an invoice photo attached
/// (<c>POST /tasks/{taskId}/emergency-purchases</c>). Larger shortages go through MO -> supplementary supply.
/// </summary>
public sealed class SelfPurchaseTests
{
    private static readonly Guid Parish = Guid.NewGuid();
    private readonly Guid _officer = Guid.NewGuid();
    private readonly Guid _volunteer = Guid.NewGuid();
    private readonly FakeScopeStore _scopes = new();
    private readonly FakeCatalogStore _catalog = new();
    private readonly FakeRequirementStore _requirements = new();
    private readonly FakeInventoryStore _inventory;
    private readonly FakeTaskProjectionStore _tasks = new();
    private readonly FakeSelfPurchaseStore _receipts = new();
    private readonly FakeIntegrationEventPublisher _events = new();
    private readonly FakeFileVerifier _files = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly WorkAreaScope _area;
    private readonly Material _cable;
    private readonly TaskProjection _task;

    public SelfPurchaseTests()
    {
        _inventory = new FakeInventoryStore(_requirements);
        _area = _scopes.Seed(Parish, _officer);
        _scopes.AddSeason(SeasonScope.Create(_area.SeasonId, Parish, SeasonStatus.Active, 1, DateTimeOffset.UtcNow));
        _cable = _catalog.SeedMaterial(_catalog.SeedCategory(), "CABLE", "Dây điện", "m");
        _requirements.Add(MaterialRequirement.Create(_area.WorkAreaId, _cable.MaterialId, 50, 0, null, DateTimeOffset.UtcNow));
        _task = _tasks.Seed(_area.WorkAreaId, ProjectedTaskStatus.InProgress, _volunteer);
    }

    private RecordEmergencyPurchaseCommandHandler Handler(Guid userId) =>
        new(new WorkAreaAccess(_scopes, Current(userId)), _tasks, _catalog, _receipts, _inventory,
            new ShortageTracker(new InventoryReader(_inventory, _catalog), _events), _files, Current(userId), _unitOfWork);

    private static FakeCurrentUser Current(Guid userId) => new(userId, GrottoWorksRoles.Volunteer, Parish);

    private RecordEmergencyPurchaseCommand Bought(decimal quantity = 2, decimal unitPrice = 50_000m, int invoices = 1) =>
        new(_task.TaskId, "Trần Văn Tình nguyện", "Điện Quang", null,
            [new SelfPurchaseLineInput(_cable.MaterialId, quantity, unitPrice)],
            [.. Enumerable.Range(0, invoices).Select(_ => Guid.NewGuid())], "Thiếu dây giữa buổi");

    private static async Task<DomainRuleException> Fails(Func<Task> act) =>
        (await act.Should().ThrowAsync<DomainRuleException>()).Which;

    [Fact]
    public async Task Only_a_volunteer_assigned_to_the_task_records_the_emergency_purchase()
    {
        (await Fails(() => Handler(Guid.NewGuid()).HandleAsync(Bought()))).Code
            .Should().Be("EMERGENCY_PURCHASE_NOT_ASSIGNEE");
        (await Fails(() => Handler(_volunteer).HandleAsync(Bought() with { TaskId = Guid.NewGuid() }))).Code
            .Should().Be("TASK_NOT_FOUND");
    }

    [Fact]
    public async Task Purchase_can_only_be_recorded_while_the_task_is_in_progress()
    {
        var notStarted = _tasks.Seed(_area.WorkAreaId, ProjectedTaskStatus.NotStarted, _volunteer);

        (await Fails(() => Handler(_volunteer).HandleAsync(Bought() with { TaskId = notStarted.TaskId }))).Code
            .Should().Be("TASK_INVALID_STATE");
    }

    [Fact]
    public async Task Emergency_purchase_must_stay_under_the_500k_limit()
    {
        (await Fails(() => Handler(_volunteer).HandleAsync(Bought(10, 60_000m)))).Code
            .Should().Be("EMERGENCY_PURCHASE_LIMIT");
        (await Fails(() => Handler(_volunteer).HandleAsync(Bought(5, 100_000m)))).Code
            .Should().Be("EMERGENCY_PURCHASE_LIMIT"); // exactly 500,000 is already too much

        _receipts.Receipts.Should().BeEmpty();
        _inventory.Ledger.Should().BeEmpty();
    }

    [Fact]
    public async Task Invoice_photo_is_mandatory()
    {
        (await Fails(() => Handler(_volunteer).HandleAsync(Bought(invoices: 0)))).Code
            .Should().Be("SELF_PURCHASE_INVOICE_REQUIRED");
    }

    [Fact]
    public async Task Unknown_material_is_rejected()
    {
        var command = Bought() with { Lines = [new SelfPurchaseLineInput(Guid.NewGuid(), 1, 10_000m)] };

        (await Fails(() => Handler(_volunteer).HandleAsync(command))).Code.Should().Be("MATERIAL_UNKNOWN");
    }

    [Fact]
    public async Task Stock_enters_the_area_and_the_receipt_is_flagged_volunteer_emergency()
    {
        var dto = await Handler(_volunteer).HandleAsync(Bought());

        dto.Source.Should().Be("VOLUNTEER_EMERGENCY");
        dto.TaskId.Should().Be(_task.TaskId);
        dto.WorkAreaId.Should().Be(_area.WorkAreaId);
        dto.TotalCost.Should().Be(100_000m);
        dto.RecordedBy.Should().Be(_volunteer);

        _receipts.Receipts.Should().ContainSingle();
        _receipts.SaveChangesCount.Should().Be(1);
        _inventory.Ledger.Should().ContainSingle();
        _inventory.Ledger[0].Type.Should().Be(TransactionType.PurchaseDelivery);
        _inventory.Ledger[0].Quantity.Should().Be(2);
        _files.Calls.Should().ContainSingle().Which.Purpose.Should().Be(FilePurposes.PurchaseReceipt);
        _unitOfWork.Committed.Should().Be(1);
    }
}
