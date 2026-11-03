using Microsoft.Extensions.Configuration;

namespace ParishCoordination.Infrastructure.Events;

/// <summary>Rollback overrides broker-primary without deleting either durable outbox.</summary>
public sealed record ScopeDeliveryOptions(bool BrokerPrimaryEnabled, bool HttpRollbackEnabled)
{
    public bool UseBroker => BrokerPrimaryEnabled && !HttpRollbackEnabled;
    public static ScopeDeliveryOptions FromConfiguration(IConfiguration configuration)
    {
        var options = new ScopeDeliveryOptions(configuration.GetValue("ScopeDelivery:BrokerPrimaryEnabled", false),
            configuration.GetValue("ScopeDelivery:HttpRollbackEnabled", false));
        if (options.UseBroker && !configuration.GetValue("PlanningEvents:DispatchEnabled", false))
            throw new InvalidOperationException("Broker scope delivery requires PlanningEvents:DispatchEnabled.");
        if (options.HttpRollbackEnabled && !configuration.GetValue("TaskScopeSync:Enabled", false))
            throw new InvalidOperationException("HTTP scope rollback requires TaskScopeSync:Enabled.");
        return options;
    }
}
