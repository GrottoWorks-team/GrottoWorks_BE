using BuildingBlocks.Messaging.MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Readiness.Application.Abstractions;
using Readiness.Application.Checklists;
using Readiness.Application.Scope;
using Readiness.Infrastructure.Checklists;
using Readiness.Infrastructure.Data;
using Readiness.Infrastructure.Observability;
using Readiness.Infrastructure.Scope;
using Readiness.Infrastructure.Messaging;

namespace Readiness.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("ReadinessDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'ReadinessDatabase' is missing.");
        }

        services.AddDbContext<ReadinessDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IIntegrationEventPublisher, LoggingIntegrationEventPublisher>();
        services.AddScoped<IWorkAreaScopeStore, WorkAreaScopeStore>();
        services.AddScoped<ITaskProjectionStore, TaskProjectionStore>();
        services.AddScoped<IReadinessCheckStore, ReadinessCheckStore>();
        // TaskExecution outbox events over RabbitMQ (POST /internal/task-events works without the broker).
        if (configuration.GetValue("TaskEvents:Enabled", false))
        {
            services.AddMessageBroker(configuration, typeof(TaskIntegrationEventConsumer).Assembly);
        }

        return services;
    }
}
