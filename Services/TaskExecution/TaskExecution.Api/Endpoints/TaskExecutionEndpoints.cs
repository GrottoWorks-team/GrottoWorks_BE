namespace TaskExecution.Api.Endpoints;

public static class TaskExecutionEndpoints
{
    public static void MapTaskExecutionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapInternalScopeEndpoints();
        app.MapTaskEndpoints();
        app.MapAssignmentEndpoints();
        app.MapApplicationEndpoints();
    }
}
