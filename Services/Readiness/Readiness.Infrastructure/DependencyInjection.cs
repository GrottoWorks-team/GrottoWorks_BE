using BuildingBlocks.Messaging.MassTransit;
using Microsoft.EntityFrameworkCore;
using Readiness.Infrastructure.Outbox;
using Readiness.Application.Files;
using Readiness.Infrastructure.Files;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
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
        services.AddHttpClient<TaskCorrectiveClient>(client =>
        {
            var address = configuration["TaskExecutionClient:BaseAddress"];
            client.BaseAddress = Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
                uri.Scheme is "http" or "https" ? uri : new Uri("http://unconfigured.invalid/");
            client.Timeout = Timeout.InfiniteTimeSpan;
        }).AddStandardResilienceHandler(options=>
        {
            options.TotalRequestTimeout.Timeout=TimeSpan.FromSeconds(10);
            options.Retry.DisableForUnsafeHttpMethods();
        });
        services.AddScoped<DurableCorrectiveTaskCreator>();
        services.AddScoped<ICorrectiveTaskCreator>(provider => provider.GetRequiredService<DurableCorrectiveTaskCreator>());
        services.AddHostedService<CorrectiveTaskReconciliationWorker>();
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

        var fileAddress = configuration[NotificationAuditClientOptions.BaseAddressConfigurationKey];
        var validFiles = Uri.TryCreate(fileAddress, UriKind.Absolute, out var files) &&
            (files.Scheme == Uri.UriSchemeHttp || files.Scheme == Uri.UriSchemeHttps);
        services.AddSingleton(new NotificationAuditClientOptions(validFiles ? files! : new Uri("http://unconfigured.invalid/"),
            validFiles ? configuration["InternalApi:ApiKey"] ?? string.Empty : string.Empty));
        services.AddHttpClient<FileAttachmentClient>(client =>
        {
            client.BaseAddress = validFiles ? files : new Uri("http://unconfigured.invalid/");
            client.Timeout = Timeout.InfiniteTimeSpan;
        }).AddStandardResilienceHandler(options=>
        {
            options.TotalRequestTimeout.Timeout=TimeSpan.FromSeconds(10);
            options.Retry.DisableForUnsafeHttpMethods();
        });
        services.AddScoped<AttachmentCoordinator>();
        services.AddScoped<IFileVerifier>(provider => provider.GetRequiredService<AttachmentCoordinator>());
        services.AddScoped<IRecordFileDownload>(provider => provider.GetRequiredService<AttachmentCoordinator>());
        services.AddHostedService<AttachmentReconciliationWorker>();

        return services;
    }
}
