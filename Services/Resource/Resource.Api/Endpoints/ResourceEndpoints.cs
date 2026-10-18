namespace Resource.Api.Endpoints;

/// <summary>Single entry point that maps every Resource endpoint group.</summary>
public static class ResourceEndpoints
{
    public static IEndpointRouteBuilder MapResourceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapCatalogEndpoints();
        app.MapInternalScopeEndpoints();
        app.MapRequirementEndpoints();
        app.MapInventoryEndpoints();
        return app;
    }
}
