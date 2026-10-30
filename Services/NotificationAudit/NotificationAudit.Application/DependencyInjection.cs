using Microsoft.Extensions.DependencyInjection;
using NotificationAudit.Application.Files;
using NotificationAudit.Application.Notifications;

namespace NotificationAudit.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<CreateUploadRequestHandler>();
        services.AddScoped<CompleteUploadHandler>();
        services.AddScoped<GetFileDownloadHandler>();
        services.AddScoped<DeleteUnattachedFileHandler>();
        services.AddScoped<GetInternalFileHandler>();
        services.AddScoped<ListNotificationsHandler>();
        services.AddScoped<MarkNotificationReadHandler>();
        services.AddScoped<MarkAllNotificationsReadHandler>();
        services.AddScoped<GetNotificationPreferenceHandler>();
        services.AddScoped<UpdateNotificationPreferenceHandler>();
        services.AddScoped<UpdateNotificationRuleHandler>();
        return services;
    }
}
