using System.Text.Json;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Reporting.Application.Abstractions;
using Reporting.Application.Ingestion;
using Reporting.Application.ReadModels;
using Reporting.Application.Recognition;
using Reporting.Infrastructure.Messaging;
using Resource.Contracts.Events;

namespace Reporting.UnitTests;

/// <summary>Resource's outbox message reaches the ingestion handler through the broker consumer.</summary>
public sealed class UpstreamConsumerTests
{
    [Fact]
    public async Task Resource_event_from_the_broker_updates_the_read_model()
    {
        var store = new FakeContributionStore();
        var readModels = new FakeReadModelStore();
        await using var provider = new ServiceCollection()
            .AddSingleton<IInboxStore>(store)
            .AddSingleton<IContributionStore>(store)
            .AddSingleton<IRecognitionCatalogStore>(new FakeRecognitionCatalogStore())
            .AddSingleton<IIntegrationEventPublisher>(new FakeIntegrationEventPublisher())
            .AddSingleton<IReadModelStore>(readModels)
            .AddScoped<ContributionRecorder>()
            .AddScoped<ReadModelProjector>()
            .AddScoped<IntegrationEventHandler>()
            .AddMassTransitTestHarness(config => config.AddConsumer<ResourceIntegrationEventConsumer, ResourceIntegrationEventConsumerDefinition>())
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        var (area, material, parish, season) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var payload = new MaterialShortageChanged(area, season, parish, material, 0, 60, 100, 40, DateTimeOffset.UtcNow, "CAB-01", "Dây điện", "m");

        await harness.Bus.Publish(new ResourceIntegrationEvent(Guid.NewGuid(), MaterialShortageChanged.EventType, 1, DateTimeOffset.UtcNow, "c", null,
            parish, season, "work_area_material", area, JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        (await harness.GetConsumerHarness<ResourceIntegrationEventConsumer>().Consumed.Any<ResourceIntegrationEvent>()).Should().BeTrue();
        readModels.Lines.Should().ContainSingle().Which.Shortage.Should().Be(60);
        store.Processed.Should().ContainSingle();
    }
}
