using Microsoft.Extensions.DependencyInjection;
using NotificationAudit.Application.Files;

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
        return services;
    }
}
