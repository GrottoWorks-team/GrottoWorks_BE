using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using TaskExecution.Application.Support;
using TaskExecution.Domain.Support;

namespace TaskExecution.Api.Endpoints;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class StaffingSupportResponseRequest
{
    public Guid? RespondingCommunityId {get;init;}
    [Required,MinLength(1)] public IReadOnlyList<SupportMemberInput>? Members {get;init;}
    [StringLength(2000)] public string? Note {get;init;}
}

public static class SupportResponseEndpoints
{
    public static void MapSupportResponseEndpoints(this IEndpointRouteBuilder app)
    {
        var group=app.MapGroup("/api/v1/support-requests/{requestId:guid}/responses").WithTags("Support").RequireAuthorization();
        group.MapPost("",CreateAsync).WithName("respondToSupportRequest")
            .RequireAuthorization(policy=>policy.RequireRole(GrottoWorksRoles.Volunteer,GrottoWorksRoles.MaterialOfficer))
            .Produces<ApiResponse<SupportResponseDto>>(201).Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403).Produces<ProblemDetails>(404).Produces<ProblemDetails>(409)
            .Produces<ProblemDetails>(412).Produces<ProblemDetails>(422).Produces<ProblemDetails>(503);
        group.MapGet("",ListAsync).WithName("listSupportResponses")
            .RequireAuthorization(policy=>policy.RequireRole(GrottoWorksRoles.Leader))
            .Produces<ApiResponse<IReadOnlyList<SupportResponseDto>>>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403).Produces<ProblemDetails>(404);
    }
    private static async Task<IResult> CreateAsync(Guid requestId,StaffingSupportResponseRequest request,
        StaffingSupportResponseHandler handler,HttpContext context,CancellationToken ct)
    {
        var result=await handler.HandleAsync(requestId,request.RespondingCommunityId,request.Members!,request.Note,ct);
        context.Response.Headers.ETag=$"\"{result.Version}\"";
        return Results.Created($"/api/v1/support-requests/{requestId}/responses",ApiResults.Ok(result,context));
    }
    private static async Task<IResult> ListAsync(Guid requestId,ListSupportResponsesHandler handler,HttpContext context,CancellationToken ct)=>
        Results.Ok(ApiResults.Ok(await handler.HandleAsync(requestId,ct),context));
}
