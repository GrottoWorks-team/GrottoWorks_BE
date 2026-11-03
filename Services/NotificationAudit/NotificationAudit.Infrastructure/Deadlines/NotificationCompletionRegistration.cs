using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotificationAudit.Application.Events;

namespace NotificationAudit.Infrastructure;

public static class NotificationCompletionRegistration
{
    public static IServiceCollection AddNotificationCompletion(this IServiceCollection services,IConfiguration configuration)
    {
        services.AddScoped<Deadlines.NotificationSnapshotIngestion>();services.AddScoped<Deadlines.NotificationDeadlineProcessor>();
        services.AddHostedService<Deadlines.NotificationDeadlineWorker>();
        services.AddHttpClient("NotificationPlanningAudience",client=>
        {
            client.BaseAddress=new Uri(configuration["ParishClient:BaseAddress"] ?? "http://localhost:6101/");
            client.Timeout=Timeout.InfiniteTimeSpan;
        }).AddStandardResilienceHandler(options=>options.TotalRequestTimeout.Timeout=TimeSpan.FromSeconds(10));
        services.AddHttpClient<INotificationAudienceResolver,Deadlines.NotificationAudienceResolver>(client=>
        {
            client.BaseAddress=new Uri(configuration["IdentityClient:BaseAddress"] ?? "http://localhost:6100/");
            client.Timeout=Timeout.InfiniteTimeSpan;
        }).AddStandardResilienceHandler(options=>options.TotalRequestTimeout.Timeout=TimeSpan.FromSeconds(10));
        return services;
    }
}
