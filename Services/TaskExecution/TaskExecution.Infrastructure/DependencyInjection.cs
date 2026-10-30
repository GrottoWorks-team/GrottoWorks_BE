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
        return services;
    }
}
