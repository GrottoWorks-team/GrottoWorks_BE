using BuildingBlocks.Messaging.MassTransit;
using Microsoft.EntityFrameworkCore;
using Readiness.Application.Files;
using Readiness.Infrastructure.Files;
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
