using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using Resource.Api.Common;
using Resource.Application.Catalog;
using Resource.Contracts.Catalog;

namespace Resource.Api.Endpoints;

public static class InternalMaterialEndpoints
{
    public static void MapInternalMaterialEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/materials", GetAsync).WithName("internalGetMaterials")
            .AllowAnonymous().AddEndpointFilter<InternalApiKeyFilter>().ExcludeFromDescription()
            .Produces<ApiResponse<IReadOnlyList<InternalMaterialDto>>>()
            .Produces<ProblemDetails>(400).Produces<ProblemDetails>(401);
    }

    private static async Task<IResult> GetAsync([FromQuery] Guid[] ids, GetInternalMaterialsHandler handler,
        HttpContext context, CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(await handler.HandleAsync(ids, cancellationToken), context));
}
