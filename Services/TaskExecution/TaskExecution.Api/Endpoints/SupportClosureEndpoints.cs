using System.ComponentModel.DataAnnotations;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using TaskExecution.Api.Common;
using TaskExecution.Application.Support;
using TaskExecution.Domain.Support;

namespace TaskExecution.Api.Endpoints;

public sealed class CloseSupportRequest
{
    [Required] public SupportRequestStatus? Status { get; init; }
    [StringLength(500)] public string? Reason { get; init; }
    [Range(1,int.MaxValue)] public int? Version { get; init; }
}
public static class SupportClosureEndpoints
{
    public static void MapSupportClosureEndpoints(this IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/support-requests/{requestId:guid}/close", async (Guid requestId, CloseSupportRequest request,
            [FromHeader(Name="If-Match")] string? ifMatch, CloseSupportRequestHandler handler, HttpContext http, CancellationToken ct) =>
        {
            var result=await handler.HandleAsync(requestId,request.Status!.Value,request.Reason,
                TaskVersionPrecondition.Resolve(request.Version,ifMatch,"SUPPORT_REQUEST_VERSION_MISMATCH"),ct);
            http.Response.Headers.ETag=$"\"{result.Version}\"";
            return Results.Ok(ApiResults.Ok(result,http));
        }).WithName("closeSupportRequest").WithTags("Support").RequireAuthorization(policy=>policy.RequireRole(GrottoWorksRoles.Leader))
            .Produces<ApiResponse<SupportRequestDto>>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403).Produces<ProblemDetails>(404).Produces<ProblemDetails>(409).Produces<ProblemDetails>(412)
            .Produces<ProblemDetails>(422).Produces<ProblemDetails>(503);
}
