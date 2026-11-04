using System.ComponentModel.DataAnnotations;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using TaskExecution.Api.Common;
using TaskExecution.Api.Requests;
using TaskExecution.Application.Support;

namespace TaskExecution.Api.Endpoints;

public sealed class ApproveSupportMaterialRequest
{
    [Required] public decimal? ApprovedQuantity { get; init; }
    [Range(1,int.MaxValue)] public int? Version { get; init; }
}

public static class SupportMaterialReviewEndpoints
{
    public static void MapSupportMaterialReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group=app.MapGroup("/api/v1/support-response-materials/{itemId:guid}").WithTags("Support")
            .RequireAuthorization(policy=>policy.RequireRole(GrottoWorksRoles.Leader));
        Configure(group.MapPost("/approve",async (Guid itemId,ApproveSupportMaterialRequest request,
            [FromHeader(Name="If-Match")] string? ifMatch,ReviewSupportMaterialHandler handler,HttpContext http,CancellationToken ct)=>
            await ReviewAsync(itemId,true,request.ApprovedQuantity,null,request.Version,ifMatch,handler,http,ct)).WithName("approveSupportMaterial"));
        Configure(group.MapPost("/reject",async (Guid itemId,ReasonCommandRequest request,
            [FromHeader(Name="If-Match")] string? ifMatch,ReviewSupportMaterialHandler handler,HttpContext http,CancellationToken ct)=>
            await ReviewAsync(itemId,false,null,request.Reason,request.Version,ifMatch,handler,http,ct)).WithName("rejectSupportMaterial"));
    }
    private static void Configure(RouteHandlerBuilder route)=>route.Produces<ApiResponse<SupportResponseMaterialDto>>()
        .Produces<ProblemDetails>(400).Produces<ProblemDetails>(401).Produces<ProblemDetails>(403).Produces<ProblemDetails>(404)
        .Produces<ProblemDetails>(409).Produces<ProblemDetails>(412).Produces<ProblemDetails>(422).Produces<ProblemDetails>(503);
    private static async Task<IResult> ReviewAsync(Guid id,bool approve,decimal? quantity,string? reason,int? version,string? ifMatch,
        ReviewSupportMaterialHandler handler,HttpContext http,CancellationToken ct)
    {
        var result=await handler.HandleAsync(id,approve,quantity,reason,TaskVersionPrecondition.Resolve(version,ifMatch,"SUPPORT_MATERIAL_VERSION_MISMATCH"),ct);
        http.Response.Headers.ETag=$"\"{result.Version}\"";
        return Results.Ok(ApiResults.Ok(result,http));
    }
}
