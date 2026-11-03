using BuildingBlocks.Messaging.MassTransit;
using Microsoft.EntityFrameworkCore;
using Readiness.Infrastructure.Outbox;
using Readiness.Application.Files;
using Readiness.Infrastructure.Files;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Readiness.Application.Abstractions;
using Readiness.Application.Checklists;
using Readiness.Application.Scope;
using Readiness.Infrastructure.Checklists;
using Readiness.Infrastructure.Data;
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
        // Integration events go through the transactional outbox (integration_event_outbox).
        services.AddHttpContextAccessor();
        services.AddScoped<IIntegrationEventPublisher, OutboxIntegrationEventPublisher>();
        services.AddScoped<IWorkAreaScopeStore, WorkAreaScopeStore>();
        services.AddScoped<IScopeReplicationInbox, ScopeReplicationInbox>();
        services.AddScoped<ITaskProjectionStore, TaskProjectionStore>();
        services.AddScoped<IReadinessCheckStore, ReadinessCheckStore>();
        // RabbitMQ: consumers of TaskExecution events (TaskEvents:Enabled) and the outbox dispatcher (Outbox:DispatchEnabled).
        // Without the broker, events stay in the outbox and the /internal endpoints still accept events.
        var consumeTaskEvents = configuration.GetValue("TaskEvents:Enabled", false);
        var dispatchOutbox = configuration.GetValue("Outbox:DispatchEnabled", false);
        if (consumeTaskEvents || dispatchOutbox)
        {
            services.AddMessageBroker(configuration, consumeTaskEvents ? typeof(TaskIntegrationEventConsumer).Assembly : null);
        }

        if (dispatchOutbox)
        {
            services.AddHostedService<OutboxDispatcher>();
        }

        // F-NTF-01 file checks (evidence, receipts); skipped when NotificationAudit is not configured.
        if (Uri.TryCreate(configuration[NotificationAuditClientOptions.BaseAddressConfigurationKey], UriKind.Absolute, out var files) &&
            (files.Scheme == Uri.UriSchemeHttp || files.Scheme == Uri.UriSchemeHttps))
        {
            services.AddSingleton(new NotificationAuditClientOptions(files, configuration["InternalApi:ApiKey"] ?? string.Empty));
            services.AddHttpContextAccessor();
            services.AddHttpClient<IFileVerifier, NotificationAuditFileVerifier>(client =>
                {
                    client.BaseAddress = files;
                    client.Timeout = Timeout.InfiniteTimeSpan;
                })
                .AddStandardResilienceHandler(options => options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10));
        }
        else
        {
            services.AddSingleton<IFileVerifier, NoFileVerifier>();
        }

        return services;
    }
}
