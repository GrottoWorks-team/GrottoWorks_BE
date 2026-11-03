using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Tasks;
using TaskExecution.Infrastructure.Data;
using TaskExecution.Infrastructure.Scopes;
using TaskExecution.Infrastructure.Tasks;

namespace TaskExecution.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("TaskExecutionDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'TaskExecutionDatabase' is missing.");
        }

        services.AddDbContext<TaskExecutionDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IScopeProjectionStore, ScopeProjectionStore>();
        services.AddScoped<IScopeSnapshotTransaction, ScopeSnapshotTransaction>();
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<IWorkAreaTaskCountReader, WorkAreaTaskCountReader>();
        services.AddScoped<IMyTasksStore, MyTasksStore>();
        services.AddHttpClient<IMaterialCatalogLookup, TaskExecution.Infrastructure.Resource.MaterialCatalogLookup>(client =>
        {
            client.BaseAddress = new Uri(configuration["ResourceClient:BaseAddress"] ?? "http://localhost:6103/");
            client.Timeout = Timeout.InfiniteTimeSpan;
        }).AddStandardResilienceHandler(options => options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<IVolunteerScheduleReader, TaskExecution.Infrastructure.Assignments.VolunteerScheduleReader>();
        var redis = configuration.GetConnectionString("Redis");
        var cacheSeconds = configuration.GetValue("Matching:CacheSeconds", 60);
        if (cacheSeconds is < 1 or > 300)
            throw new InvalidOperationException("Matching:CacheSeconds must be between 1 and 300.");
        services.AddSingleton(new TaskExecution.Infrastructure.Identity.VolunteerDirectoryCacheOptions(cacheSeconds));
        if (string.IsNullOrWhiteSpace(redis)) services.AddDistributedMemoryCache(options => options.SizeLimit = 16 * 1024 * 1024);
        else services.AddStackExchangeRedisCache(options =>
        {
            var connection = StackExchange.Redis.ConfigurationOptions.Parse(redis);
            connection.AbortOnConnectFail = false;
            connection.ConnectTimeout = 500;
            connection.AsyncTimeout = 500;
            connection.ConnectRetry = 0;
            options.ConfigurationOptions = connection;
            options.InstanceName = "grottoworks:task:";
        });
        services.AddSingleton<TaskExecution.Infrastructure.Identity.VolunteerDirectoryCacheGate>();
        services.AddScoped<IVolunteerDirectory, TaskExecution.Infrastructure.Identity.CachedVolunteerDirectory>();
        services.AddHttpClient<TaskExecution.Infrastructure.Identity.IdentityVolunteerDirectory>(client =>
        {
            client.BaseAddress = new Uri(configuration["IdentityClient:BaseAddress"] ?? "http://localhost:6100/");
            client.Timeout = Timeout.InfiniteTimeSpan;
        }).AddStandardResilienceHandler(options => options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<TaskExecution.Application.Support.ISupportRequestRepository, TaskExecution.Infrastructure.Support.SupportRequestRepository>();
        services.AddScoped<TaskExecution.Application.Attendance.IAttendanceStore, TaskExecution.Infrastructure.Attendance.AttendanceStore>();
        services.AddScoped<TaskExecution.Application.Attendance.IAttendanceReadStore, TaskExecution.Infrastructure.Attendance.AttendanceReadStore>();
        services.AddScoped<TaskExecution.Application.Attendance.ILongOpenAttendanceStore, TaskExecution.Infrastructure.Attendance.LongOpenAttendanceStore>();
        var longOpenHours=configuration.GetValue("Attendance:LongOpenHours",12);
        if (longOpenHours is <1 or >9999) throw new InvalidOperationException("Attendance:LongOpenHours must be between 1 and 9999.");
        services.AddSingleton(new TaskExecution.Application.Attendance.LongOpenAttendanceOptions(longOpenHours*60));
        services.AddHostedService<TaskExecution.Infrastructure.Attendance.AttendanceAlertWorker>();
        services.AddScoped<TaskExecution.Application.Assignments.IAssignmentStore, TaskExecution.Infrastructure.Assignments.AssignmentStore>();
        services.AddHttpClient<TaskExecution.Application.Users.IIdentityUserLookup, TaskExecution.Infrastructure.Identity.IdentityUserLookup>(client =>
        {
            client.BaseAddress = new Uri(configuration["IdentityClient:BaseAddress"] ?? "http://localhost:6100/");
            client.Timeout = Timeout.InfiniteTimeSpan;
        }).AddStandardResilienceHandler(options => options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<TaskExecution.Application.Events.ITaskEventOutbox, TaskExecution.Infrastructure.Events.TaskEventOutbox>();
        services.AddScoped<IScopeEventInbox, ScopeEventInbox>();
        services.AddScoped<IUserStatusProjectionStore, UserStatusProjectionStore>();
        services.AddScoped<PlanningScopeIngestionHandler>();
        services.AddScoped<IdentityStatusIngestionHandler>();
        var dispatch = configuration.GetValue("Outbox:DispatchEnabled", false);
        var consume = configuration.GetValue("PlanningEvents:ConsumeEnabled", false);
        if (dispatch || consume)
        {
            BuildingBlocks.Messaging.MassTransit.Extensions.AddMessageBroker(services, configuration,
                consume ? typeof(PlanningScopeConsumer).Assembly : null);
        }
        if (dispatch)
        {
            services.AddScoped<TaskExecution.Infrastructure.Events.TaskSnapshotReconciliation>();
            services.AddHostedService<TaskExecution.Infrastructure.Events.TaskSnapshotReconciliationWorker>();
            services.AddHostedService<TaskExecution.Infrastructure.Events.TaskEventDispatcher>();
        }
        return services;
    }
}
