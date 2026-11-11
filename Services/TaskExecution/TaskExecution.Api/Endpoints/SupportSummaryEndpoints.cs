using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using TaskExecution.Api.Common;
using TaskExecution.Application.Support;

namespace TaskExecution.Api.Endpoints;

public static class SupportSummaryEndpoints
{
    public static void MapSupportSummaryEndpoints(this IEndpointRouteBuilder app)=>
        app.MapGet("/api/v1/seasons/{seasonId:guid}/support-summary",async (Guid seasonId,GetSupportSummaryHandler handler,HttpContext http,CancellationToken ct)=>
            Results.Ok(ApiResults.Ok(await handler.HandleAsync(seasonId,ct),http)))
        .WithName("getCrossCommunitySupportSummary").WithTags("Support").RequireAuthorization(policy=>policy.RequireRole(GrottoWorksRoles.Parish, GrottoWorksRoles.MaterialOfficer))
        .Produces<ApiResponse<SupportSummaryDto>>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
        .Produces<ProblemDetails>(403).Produces<ProblemDetails>(404).Produces<ProblemDetails>(503);
}
