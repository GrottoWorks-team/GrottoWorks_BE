using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ParishCoordination.Infrastructure;
using ParishCoordination.Infrastructure.Events;

namespace ParishCoordination.UnitTests;

public sealed class BrokerScopeDeliveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Planning_dispatch_always_registers_broker_workers_despite_obsolete_http_rollback_flags(bool rollback)
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(Config(rollback));

        services.Should().Contain(service => service.ServiceType == typeof(IHostedService) &&
            service.ImplementationType == typeof(PlanningEventDispatcher));
        services.Should().Contain(service => service.ServiceType == typeof(IHostedService) &&
            service.ImplementationType == typeof(PlanningSnapshotReconciliationWorker));
        typeof(PlanningEventDispatcher).Assembly.GetTypes().Should().NotContain(type =>
            type.Namespace == "ParishCoordination.Infrastructure.TaskScopeSync" || type.Name == "ScopeDeliveryOptions");
    }

    [Fact]
    public void Planning_dispatch_requires_broker_configuration_without_http_fallback()
    {
        var config = Config(true);
        config["MessageBroker:Host"] = null;
        var register = () => new ServiceCollection().AddInfrastructure(config);

        register.Should().Throw<InvalidOperationException>().WithMessage("*MessageBroker:Host*");
    }

    private static IConfigurationRoot Config(bool rollback) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ParishCoordinationDatabase"] = "Host=localhost;Database=metadata_only",
            ["IdentityClient:BaseAddress"] = "http://localhost:6100/",
            ["TaskExecutionClient:BaseAddress"] = "http://localhost:6104/",
            ["MessageBroker:Host"] = "localhost",
            ["PlanningEvents:DispatchEnabled"] = "true",
            ["ScopeDelivery:BrokerPrimaryEnabled"] = "false",
            ["ScopeDelivery:HttpRollbackEnabled"] = rollback.ToString(),
            ["TaskScopeSync:Enabled"] = "true",
            ["TaskScopeSync:BaseAddress"] = "obsolete-invalid-address"
        }).Build();
}
