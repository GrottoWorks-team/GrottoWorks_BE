using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotificationAudit.Infrastructure.Data;
using NotificationAudit.Application.Files;
using NotificationAudit.Infrastructure.Files;
using NotificationAudit.Application.Notifications;
using NotificationAudit.Infrastructure.Notifications;
using NotificationAudit.Application.Audit;
using NotificationAudit.Infrastructure.Audit;
using BuildingBlocks.Messaging.MassTransit;
using NotificationAudit.Application.Events;
using NotificationAudit.Infrastructure.Events;

namespace NotificationAudit.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("NotificationAuditDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'NotificationAuditDatabase' is missing.");
        }

        services.AddDbContext<NotificationAuditDbContext>(options => options.UseNpgsql(connectionString));
        services.Configure<ObjectStorageOptions>(configuration.GetSection(ObjectStorageOptions.SectionName));
        services.Configure<FileUploadOptions>(configuration.GetSection(FileUploadOptions.SectionName));
        services.AddScoped<IFileObjectRepository, FileObjectRepository>();
        services.AddScoped<IFileObjectStorage, MinioFileObjectStorage>();
        services.AddHostedService<MinioBucketInitializer>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<INotificationPreferenceRepository, NotificationPreferenceRepository>();
        services.AddScoped<INotificationRuleRepository, NotificationRuleRepository>();
        services.AddScoped<INotificationRuleSeeder, NotificationRuleSeeder>();
        services.AddScoped<IActivityLogRepository, ActivityLogRepository>();
        services.AddScoped<INotificationEventStore, NotificationEventStore>();
        services.AddScoped<IUnresolvedNotificationEventStore, UnresolvedNotificationEventStore>();
        services.AddOptions<NotificationContextReplayOptions>()
            .Bind(configuration.GetSection(NotificationContextReplayOptions.SectionName))
            .Validate(value => value.PollSeconds is >= 5 and <= 300 && value.BatchSize is >= 1 and <= 1000,
                "Notification replay requires a poll interval of 5-300 seconds and batch size of 1-1000.")
            .ValidateOnStart();
        if (configuration.GetValue("NotificationContextReplay:Enabled", true))
            services.AddHostedService<NotificationContextReplayWorker>();
        if (configuration.GetValue("NotificationEvents:Enabled", false))
        {
            services.AddMessageBroker(configuration, typeof(TaskIntegrationEventConsumer).Assembly);
        }
        services.AddNotificationCompletion(configuration);
        SystemJobs.SystemJobRegistration.AddSystemJobs(services, configuration);
        return services;
    }
}
