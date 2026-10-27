namespace Reporting.Api.Endpoints;

/// <summary>Single entry point that maps every Reporting endpoint group.</summary>
public static class ReportingEndpoints
{
    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapInternalScopeEndpoints();
        app.MapRecognitionCatalogEndpoints();
        return app;
    }
}
