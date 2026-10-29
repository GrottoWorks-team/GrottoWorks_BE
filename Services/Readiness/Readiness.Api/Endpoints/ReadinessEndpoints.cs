namespace Readiness.Api.Endpoints;

/// <summary>Single entry point that maps every Readiness endpoint group.</summary>
public static class ReadinessEndpoints
{
    public static IEndpointRouteBuilder MapReadinessEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapInternalScopeEndpoints();
        app.MapChecklistEndpoints();
        return app;
    }
}
