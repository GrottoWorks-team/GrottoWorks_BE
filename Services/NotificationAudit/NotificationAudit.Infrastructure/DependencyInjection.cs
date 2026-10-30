using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotificationAudit.Infrastructure.Data;
using NotificationAudit.Application.Files;
using NotificationAudit.Infrastructure.Files;
using NotificationAudit.Application.Notifications;
using NotificationAudit.Infrastructure.Notifications;

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
        return services;
    }
}
