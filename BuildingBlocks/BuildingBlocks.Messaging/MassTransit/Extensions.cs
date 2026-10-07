using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace BuildingBlocks.Messaging.MassTransit;

/// <summary>RabbitMQ broker registration (F-PLT-05, MVP — outbox/inbox follows in S1).</summary>
public static class Extensions
{
    /// <summary>
    /// Adds MassTransit on RabbitMQ using the <c>MessageBroker</c> config section
    /// (<c>Host</c>, <c>UserName</c>, <c>Password</c>). Pass the service assembly to register its
    /// consumers automatically.
    /// </summary>
    public static IServiceCollection AddMessageBroker(
        this IServiceCollection services,
        IConfiguration configuration,
        Assembly? assembly = null)
    {
        var host = configuration["MessageBroker:Host"]
            ?? throw new InvalidOperationException("Configuration 'MessageBroker:Host' is missing.");

        // Accept both "amqp://rabbitmq:5672" and a bare host name such as "rabbitmq".
        var hostUri = host.Contains("://", StringComparison.Ordinal)
            ? new Uri(host)
            : new Uri($"amqp://{host}");

        services.AddMassTransit(config =>
        {
            config.SetKebabCaseEndpointNameFormatter();

            if (assembly != null)
            {
                config.AddConsumers(assembly);
            }

            config.UsingRabbitMq((context, configurator) =>
            {
                configurator.Host(hostUri, hostConfigurator =>
                {
                    hostConfigurator.Username(configuration["MessageBroker:UserName"] ?? "guest");
                    hostConfigurator.Password(configuration["MessageBroker:Password"] ?? "guest");
                });
                configurator.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
