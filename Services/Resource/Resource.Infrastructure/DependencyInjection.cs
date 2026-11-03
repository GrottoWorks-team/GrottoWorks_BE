using BuildingBlocks.Messaging.MassTransit;
using Microsoft.EntityFrameworkCore;
using Resource.Infrastructure.Outbox;
using Resource.Application.Files;
using Resource.Infrastructure.Files;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Resource.Application.Abstractions;
using Resource.Application.Catalog;
using Resource.Infrastructure.Catalog;
using Resource.Application.Scope;
using Resource.Infrastructure.Scope;
using Resource.Application.Requirements;
using Resource.Infrastructure.Requirements;
using Resource.Application.Inventory;
using Resource.Infrastructure.Inventory;
using Resource.Application.Purchases;
using Resource.Infrastructure.Purchases;
using Resource.Application.Contributions;
using Resource.Application.Users;
using Resource.Infrastructure.Contributions;
using Resource.Infrastructure.Identity;
using Resource.Infrastructure.Data;
using Resource.Infrastructure.Messaging;

namespace Resource.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("ResourceDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'ResourceDatabase' is missing.");
        }

        services.AddDbContext<ResourceDbContext>(options => options.UseNpgsql(connectionString));
        // Integration events go through the transactional outbox (integration_event_outbox).
        services.AddHttpContextAccessor();
        services.AddScoped<IIntegrationEventPublisher, OutboxIntegrationEventPublisher>();
        services.AddScoped<IMaterialCatalogStore, MaterialCatalogStore>();
        services.AddScoped<IWorkAreaScopeStore, WorkAreaScopeStore>();
        services.AddScoped<IScopeReplicationInbox, ScopeReplicationInbox>();
        services.AddScoped<ITaskMaterialPlanStore, TaskMaterialPlanStore>();
        services.AddScoped<ITaskProjectionStore, TaskProjectionStore>();
        services.AddScoped<IMaterialRequirementStore, MaterialRequirementStore>();
        services.AddScoped<IInventoryStore, InventoryStore>();
        services.AddScoped<IAllocationStore, AllocationStore>();
        services.AddScoped<ITaskMaterialReportStore, TaskMaterialReportStore>();
        services.AddScoped<ISupportTransferStore, SupportTransferStore>();
        services.AddScoped<ISupportReconciliationReadStore,SupportReconciliationReadStore>();
        services.AddScoped<IWorkAreaStockLock, PostgresWorkAreaStockLock>();
        services.AddScoped<IPurchaseRequestStore, PurchaseRequestStore>();
        services.AddScoped<IPurchaseStore, PurchaseStore>();
        services.AddScoped<IDonorStore, DonorStore>();
        services.AddScoped<IDonationStore, DonationStore>();
        services.AddScoped<IBorrowedItemStore, BorrowedItemStore>();
        services.AddHostedService<BorrowedItemReminderService>();

        // F-IDN-09 lookup (link a donor to a user account).
        var identityClientOptions = IdentityClientOptions.FromConfiguration(configuration);
        services.AddSingleton(identityClientOptions);
        services.AddHttpContextAccessor();
        services.AddHttpClient<IIdentityUserLookup, IdentityUserLookupClient>(client =>
            {
                client.BaseAddress = identityClientOptions.BaseAddress;
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddStandardResilienceHandler(options => options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10));

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
