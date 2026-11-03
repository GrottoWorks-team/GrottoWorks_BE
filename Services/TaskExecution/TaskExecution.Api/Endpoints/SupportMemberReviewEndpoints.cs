using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using TaskExecution.Api.Common;
using TaskExecution.Api.Requests;
using TaskExecution.Application.Support;

namespace TaskExecution.Api.Endpoints;

public static class SupportMemberReviewEndpoints
{
    public static void MapSupportMemberReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group=app.MapGroup("/api/v1/support-response-members/{memberId:guid}").WithTags("Support")
            .RequireAuthorization(policy=>policy.RequireRole(GrottoWorksRoles.Leader));
        Configure(group.MapPost("/approve",ApproveAsync).WithName("approveSupportMember"));
        Configure(group.MapPost("/reject",RejectAsync).WithName("rejectSupportMember"));
    }
    private static void Configure(RouteHandlerBuilder route)=>route.Produces<ApiResponse<SupportMemberDto>>()
        .Produces<ProblemDetails>(400).Produces<ProblemDetails>(401).Produces<ProblemDetails>(403).Produces<ProblemDetails>(404)
        .Produces<ProblemDetails>(409).Produces<ProblemDetails>(412).Produces<ProblemDetails>(422).Produces<ProblemDetails>(503);
    private static async Task<IResult> ApproveAsync(Guid memberId,
        [FromBody(EmptyBodyBehavior=Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] VersionedCommandRequest? request,
        [FromHeader(Name="If-Match")] string? ifMatch,ReviewSupportMemberHandler handler,HttpContext context,CancellationToken ct)=>
        await ReviewAsync(memberId,true,null,request?.Version,ifMatch,handler,context,ct);
    private static async Task<IResult> RejectAsync(Guid memberId,ReasonCommandRequest request,[FromHeader(Name="If-Match")] string? ifMatch,
        ReviewSupportMemberHandler handler,HttpContext context,CancellationToken ct)=>
        await ReviewAsync(memberId,false,request.Reason,request.Version,ifMatch,handler,context,ct);
    private static async Task<IResult> ReviewAsync(Guid memberId,bool approve,string? reason,int? version,string? ifMatch,
        ReviewSupportMemberHandler handler,HttpContext context,CancellationToken ct)
    {
        var result=await handler.HandleAsync(memberId,approve,reason,TaskVersionPrecondition.Resolve(version,ifMatch,"SUPPORT_MEMBER_VERSION_MISMATCH"),ct);
        context.Response.Headers.ETag=$"\"{result.Version}\"";
        return Results.Ok(ApiResults.Ok(result,context));
    }
}
