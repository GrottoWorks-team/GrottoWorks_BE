namespace Resource.Api.Endpoints;

/// <summary>Single entry point that maps every Resource endpoint group.</summary>
public static class ResourceEndpoints
{
    public static IEndpointRouteBuilder MapResourceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapCatalogEndpoints();
        app.MapInternalScopeEndpoints();
        app.MapInternalMaterialEndpoints();
        app.MapInternalPurchaseEndpoints();
        app.MapInternalSupportReconciliationEndpoints();
        app.MapRequirementEndpoints();
        app.MapInventoryEndpoints();
        app.MapParishInventoryEndpoints();
        app.MapParishDistributionEndpoints();
        app.MapShortageEndpoints();
        app.MapPurchaseRequestEndpoints();
        app.MapPurchaseEndpoints();
        app.MapSelfPurchaseEndpoints();
        app.MapDonorEndpoints();
        app.MapDonationEndpoints();
        app.MapMaterialReportEndpoints();
        app.MapBorrowedItemEndpoints();
        app.MapRecordFileEndpoints();
        return app;
    }
}
