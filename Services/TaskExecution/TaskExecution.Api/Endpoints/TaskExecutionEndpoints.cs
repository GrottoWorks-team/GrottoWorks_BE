namespace TaskExecution.Api.Endpoints;

public static class TaskExecutionEndpoints
{
    public static void MapTaskExecutionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapInternalScopeEndpoints();
        app.MapTaskEndpoints();
        app.MapAssignmentEndpoints();
        app.MapApplicationEndpoints();
        app.MapMyTasksEndpoints();
        app.MapTaskLifecycleEndpoints();
        app.MapAttendanceEndpoints();
        app.MapAttendanceReviewEndpoints();
        app.MapCheckOutEndpoints();
        app.MapAttendanceQueryEndpoints();
        app.MapAttendanceCorrectionEndpoints();
        app.MapAttendanceCorrectionReviewEndpoints();
        app.MapLongOpenAttendanceEndpoints();
        app.MapSupportRequestEndpoints();
    }
}
