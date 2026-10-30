using Microsoft.Extensions.DependencyInjection;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Tasks;

namespace TaskExecution.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ApplySeasonScopeSnapshotHandler>();
        services.AddScoped<ApplyWorkAreaScopeSnapshotHandler>();
        services.AddScoped<TaskScopeAccess>();
        services.AddScoped<TaskExecution.Application.Users.ActiveUserAccess>();
        services.AddScoped<TaskExecution.Application.Assignments.AssignmentService>();
        services.AddScoped<TaskExecution.Application.Events.TaskEventRecorder>();
        services.AddScoped<GetTasksQueryHandler>();
        services.AddScoped<MyTasksQueryHandler>();
        services.AddScoped<TaskExecution.Application.Attendance.AttendanceService>();
        services.AddScoped<TaskExecution.Application.Attendance.AttendanceQueries>();
        services.AddScoped<TaskExecution.Application.Attendance.LongOpenAttendanceQuery>();
        services.AddScoped<TaskExecution.Application.Attendance.AttendanceAlertProcessor>();
        services.AddScoped<GetTaskQueryHandler>();
        services.AddScoped<CreateTaskCommandHandler>();
        services.AddScoped<UpdateTaskCommandHandler>();
        services.AddScoped<ReplaceCompletionCriteriaCommandHandler>();
        services.AddScoped<ReplaceTaskSkillsCommandHandler>();
        services.AddScoped<ReadyTaskCommandHandler>();
        return services;
    }
}
