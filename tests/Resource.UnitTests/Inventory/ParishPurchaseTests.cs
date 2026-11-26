using BuildingBlocks.Security;
using Resource.Application.Inventory;
using Resource.Application.Scope;
using Resource.Domain;
using Resource.Domain.Inventory;
using Resource.Domain.Scope;

namespace Resource.UnitTests.Inventory;

/// <summary>
/// 26/11/2026: the parish committee buys the missing materials itself. The receipt needs an invoice photo, the total is
/// computed server-side, and every line posts a PURCHASE_DELIVERY row into the parish warehouse ledger so it can be
/// distributed down to the work areas.
/// </summary>
public sealed class ParishPurchaseTests
{
    private static readonly Guid Parish = Guid.NewGuid();
    private readonly Guid _parishUser = Guid.NewGuid();
    private readonly Guid _seasonId = Guid.NewGuid();
    private readonly FakeScopeStore _scopes = new();
    private readonly FakeCatalogStore _catalog = new();
    private readonly FakeRequirementStore _requirements = new();
    private readonly FakeParishInventoryStore _inventory = new();
    private readonly FakeParishPurchaseStore _purchases = new();
    private readonly FakeFileVerifier _files = new();
    private readonly FakeIntegrationEventPublisher _events = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly Domain.Catalog.Material _cable;

    public ParishPurchaseTests()
    {
        _scopes.AddSeason(SeasonScope.Create(_seasonId, Parish, SeasonStatus.Active, 1, DateTimeOffset.UtcNow));
        _cable = _catalog.SeedMaterial(_catalog.SeedCategory(), "CABLE", "Dây điện", "m");
    }

    private FakeCurrentUser User(string role = GrottoWorksRoles.Parish) => new(_parishUser, role, Parish);

    private RecordParishPurchaseCommandHandler Handler(string role = GrottoWorksRoles.Parish)
    {
        var user = User(role);
        return new(new ParishWarehouseAccess(_scopes, user), _catalog, _purchases, _inventory, _files, _events, user, _unitOfWork);
    }

    private RecordParishPurchaseCommand Bought(decimal quantity = 10, decimal unitPrice = 20_000m, int invoices = 1) =>
        new(_seasonId, "Ban hành giáo", "Điện Quang", null,
            [new ParishPurchaseLineInput(_cable.MaterialId, quantity, unitPrice)],
            [.. Enumerable.Range(0, invoices).Select(_ => Guid.NewGuid())], "Mua bổ sung vật tư thiếu");

    private static async Task<DomainRuleException> Fails(Func<Task> act) =>
        (await act.Should().ThrowAsync<DomainRuleException>()).Which;

    [Fact]
    public async Task Parish_records_a_purchase_and_the_stock_enters_the_warehouse()
    {
        var dto = await Handler().HandleAsync(Bought());

        dto.TotalCost.Should().Be(200_000m);
        dto.Items.Should().ContainSingle();
        dto.Items[0].MaterialCode.Should().Be("CABLE");
        _purchases.Receipts.Should().ContainSingle();
        _inventory.Ledger.Should().ContainSingle();
        _inventory.Ledger[0].Type.Should().Be(TransactionType.PurchaseDelivery);
        _inventory.Ledger[0].Quantity.Should().Be(10);
        _files.Calls.Should().ContainSingle().Which.Purpose.Should().Be("PURCHASE_RECEIPT");
        _unitOfWork.Committed.Should().Be(1);
    }

    [Fact]
    public async Task Invoice_is_mandatory_and_the_line_must_be_valid()
    {
        (await Fails(() => Handler().HandleAsync(Bought(invoices: 0)))).Code
            .Should().Be("PARISH_PURCHASE_INVOICE_REQUIRED");

        (await Fails(() => Handler().HandleAsync(Bought(quantity: 0)))).Code
            .Should().Be("PARISH_PURCHASE_QUANTITY_INVALID");

        var duplicate = Bought() with
        {
            Lines = [new ParishPurchaseLineInput(_cable.MaterialId, 1, 1), new ParishPurchaseLineInput(_cable.MaterialId, 2, 1)]
        };
        (await Fails(() => Handler().HandleAsync(duplicate))).Code.Should().Be("PARISH_PURCHASE_DUPLICATE_LINE");

        var unknown = Bought() with { Lines = [new ParishPurchaseLineInput(Guid.NewGuid(), 1, 1)] };
        (await Fails(() => Handler().HandleAsync(unknown))).Code.Should().Be("MATERIAL_UNKNOWN");
    }

    [Fact]
    public async Task Buyer_name_is_required()
    {
        (await Fails(() => Handler().HandleAsync(Bought() with { BuyerName = "  " }))).Code
            .Should().Be("PARISH_PURCHASE_BUYER_REQUIRED");
    }

    [Fact]
    public async Task Only_the_parish_of_the_season_can_record()
    {
        (await Fails(() => Handler(GrottoWorksRoles.MaterialOfficer).HandleAsync(Bought()))).Code
            .Should().BeOneOf("PARISH_ACCESS_DENIED", "PARISH_SCOPE_REQUIRED");

        (await Fails(() => Handler().HandleAsync(Bought() with { SeasonId = Guid.NewGuid() }))).Code
            .Should().BeOneOf("SEASON_NOT_FOUND", "SEASON_SCOPE_NOT_FOUND", "PARISH_SEASON_NOT_FOUND");
    }
}
