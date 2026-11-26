using System.Text.Json;
using Reporting.Application.Ingestion;
using Reporting.Application.ReadModels;
using Reporting.Application.Recognition;
using Resource.Contracts.Events;

namespace Reporting.UnitTests;

/// <summary>Parish central warehouse (26/11/2026): <c>parish.material.stock.changed</c> → <c>rm_parish_material_line</c>.</summary>
public sealed class ParishWarehouseReportingTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Guid _parishId = Guid.NewGuid();
    private readonly Guid _seasonId = Guid.NewGuid();
    private readonly Guid _materialId = Guid.NewGuid();
    private readonly FakeReadModelStore _readModels = new();
    private readonly FakeContributionStore _store = new();
    private readonly FakeRecognitionCatalogStore _catalog = new();

    private IntegrationEventHandler Handler =>
        new(_store, new ContributionRecorder(_store, _catalog, new FakeIntegrationEventPublisher()), new ReadModelProjector(_readModels), _readModels);

    private IncomingEvent Stock(decimal available, DateTimeOffset? at = null) =>
        new(Guid.NewGuid(), ParishMaterialStockChanged.EventType, 1, at ?? DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(new ParishMaterialStockChanged(_parishId, _seasonId, _materialId, available,
                at ?? DateTimeOffset.UtcNow, "VAT-001", "Đèn", "cái"), Json));

    [Fact]
    public async Task Parish_stock_event_keeps_the_warehouse_balance()
    {
        var result = await Handler.HandleAsync(Stock(18m));

        result.Outcome.Should().Be(IngestionOutcome.Applied);
        var line = _readModels.ParishLines.Should().ContainSingle().Subject;
        (line.ParishId, line.SeasonId, line.MaterialId).Should().Be((_parishId, _seasonId, _materialId));
        (line.MaterialCode, line.MaterialName, line.Unit).Should().Be(("VAT-001", "Đèn", "cái"));
        line.ConfirmedAvailable.Should().Be(18m);
    }

    [Fact]
    public async Task The_newest_balance_wins()
    {
        await Handler.HandleAsync(Stock(18m));
        await Handler.HandleAsync(Stock(12m));

        _readModels.ParishLines.Should().ContainSingle().Which.ConfirmedAvailable.Should().Be(12m);
    }
}
