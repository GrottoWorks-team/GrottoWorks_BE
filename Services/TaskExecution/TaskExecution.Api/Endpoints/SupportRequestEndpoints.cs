using System.ComponentModel.DataAnnotations;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using TaskExecution.Application.Support;
using TaskExecution.Domain.Support;

namespace TaskExecution.Api.Endpoints;

public sealed class CreateSupportRequest
{
    [Required] public Guid? SeasonId {get;init;}
    [Required] public Guid? WorkAreaId {get;init;}
    [Required,EnumDataType(typeof(SupportType))] public SupportType? SupportType {get;init;}
    [StringLength(2000)] public string? Description {get;init;}
    public DateOnly? Deadline {get;init;}
    public IReadOnlyList<SupportPositionInput> Positions {get;init;}=[];
    public IReadOnlyList<SupportMaterialInput> Materials {get;init;}=[];
}

public static partial class SupportRequestEndpoints
{
    public static void MapSupportRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var group=app.MapGroup("/api/v1").WithTags("Support").RequireAuthorization();
        group.MapPost("/support-requests",CreateAsync).WithName("createSupportRequest")
            .RequireAuthorization(p=>p.RequireRole(GrottoWorksRoles.Leader))
            .Produces<ApiResponse<SupportRequestDto>>(201).Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403).Produces<ProblemDetails>(404).Produces<ProblemDetails>(409)
            .Produces<ProblemDetails>(412).Produces<ProblemDetails>(422).Produces<ProblemDetails>(503);
        group.MapGet("/support-requests/{requestId:guid}",GetAsync).WithName("getSupportRequest")
            .Produces<ApiResponse<SupportRequestDto>>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403).Produces<ProblemDetails>(404);
    }
    private static async Task<IResult> CreateAsync(CreateSupportRequest request,SupportRequestService service,HttpContext context,CancellationToken ct)
    {
        var result=await service.CreateAsync(new(request.SeasonId!.Value,request.WorkAreaId!.Value,request.SupportType!.Value,
            request.Description,request.Deadline,request.Positions,request.Materials),ct);
        context.Response.Headers.ETag=$"\"{result.Version}\"";
        return Results.Created($"/api/v1/support-requests/{result.Id}",ApiResults.Ok(result,context));
    }
    private static async Task<IResult> GetAsync(Guid requestId,SupportRequestService service,HttpContext context,CancellationToken ct)
    {
        var result=await service.GetAsync(requestId,ct);context.Response.Headers.ETag=$"\"{result.Version}\"";
        return Results.Ok(ApiResults.Ok(result,context));
    }
}
