using FluentAssertions;
using Microsoft.Extensions.Configuration;
using ParishCoordination.Infrastructure.Events;

namespace ParishCoordination.UnitTests;

public sealed class ScopeDeliveryCutoverTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    public void Http_rollback_overrides_broker_primary_and_keeps_one_scope_transport(bool primary, bool rollback, bool broker)
    {
        var options = ScopeDeliveryOptions.FromConfiguration(Config(primary, rollback, true, true));
        options.UseBroker.Should().Be(broker);
    }

    [Fact]
    public void Broker_primary_requires_dispatch_and_rollback_requires_http_adapter()
    {
        var missingBroker = () => ScopeDeliveryOptions.FromConfiguration(Config(true, false, false, true));
        missingBroker.Should().Throw<InvalidOperationException>();
        var missingHttp = () => ScopeDeliveryOptions.FromConfiguration(Config(true, true, true, false));
        missingHttp.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Default_does_not_cut_over_before_runtime_acceptance()
    {
        var options = ScopeDeliveryOptions.FromConfiguration(new ConfigurationBuilder().Build());
        options.UseBroker.Should().BeFalse();
        options.HttpRollbackEnabled.Should().BeFalse();
    }

    private static IConfiguration Config(bool primary, bool rollback, bool dispatch, bool http) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> {
            ["ScopeDelivery:BrokerPrimaryEnabled"] = primary.ToString(), ["ScopeDelivery:HttpRollbackEnabled"] = rollback.ToString(),
            ["PlanningEvents:DispatchEnabled"] = dispatch.ToString(), ["TaskScopeSync:Enabled"] = http.ToString()
        }).Build();
}
