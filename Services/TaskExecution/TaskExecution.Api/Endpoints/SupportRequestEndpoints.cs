using System.ComponentModel.DataAnnotations;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using TaskExecution.Application.Support;
using TaskExecution.Domain.Support;
using BuildingBlocks.Pagination;
using TaskExecution.Api.Common;
using System.Text.Json.Serialization;

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

public sealed class UpdateSupportRequest
{
    public SupportType? SupportType {get;init;}
    private string? description;
    public string? Description {get=>description;init {description=value;HasDescription=true;}}
    [JsonIgnore] public bool HasDescription {get;private set;}
    private DateOnly? deadline;
    public DateOnly? Deadline {get=>deadline;init {deadline=value;HasDeadline=true;}}
    [JsonIgnore] public bool HasDeadline {get;private set;}
    private IReadOnlyList<SupportPositionInput>? positions;
    public IReadOnlyList<SupportPositionInput>? Positions {get=>positions;init {positions=value;HasPositions=true;}}
    [JsonIgnore] public bool HasPositions {get;private set;}
    private IReadOnlyList<SupportMaterialInput>? materials;
    public IReadOnlyList<SupportMaterialInput>? Materials {get=>materials;init {materials=value;HasMaterials=true;}}
    [JsonIgnore] public bool HasMaterials {get;private set;}
    [Range(1,int.MaxValue)] public int? Version {get;init;}
}

public static partial class SupportRequestEndpoints
{
    public static void MapSupportRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var group=app.MapGroup("/api/v1").WithTags("Support").RequireAuthorization();
        group.MapGet("/support-requests",ListAsync).WithName("listSupportRequests")
            .Produces<PagedResponse<SupportRequestDto>>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403);
        group.MapPost("/support-requests",CreateAsync).WithName("createSupportRequest")
            .RequireAuthorization(p=>p.RequireRole(GrottoWorksRoles.Leader))
            .Produces<ApiResponse<SupportRequestDto>>(201).Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403).Produces<ProblemDetails>(404).Produces<ProblemDetails>(409)
            .Produces<ProblemDetails>(412).Produces<ProblemDetails>(422).Produces<ProblemDetails>(503);
        group.MapGet("/support-requests/{requestId:guid}",GetAsync).WithName("getSupportRequest")
            .Produces<ApiResponse<SupportRequestDto>>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403).Produces<ProblemDetails>(404);
        group.MapPatch("/support-requests/{requestId:guid}",UpdateAsync).WithName("updateSupportRequest")
            .RequireAuthorization(p=>p.RequireRole(GrottoWorksRoles.Leader))
            .Produces<ApiResponse<SupportRequestDto>>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403).Produces<ProblemDetails>(404).Produces<ProblemDetails>(409)
            .Produces<ProblemDetails>(412).Produces<ProblemDetails>(422).Produces<ProblemDetails>(503);
    }
    private static async Task<IResult> CreateAsync(CreateSupportRequest request,SupportRequestService service,HttpContext context,CancellationToken ct)
    {
        var result=await service.CreateAsync(new(request.SeasonId!.Value,request.WorkAreaId!.Value,request.SupportType!.Value,
            request.Description,request.Deadline,request.Positions,request.Materials),ct);
        context.Response.Headers.ETag=$"\"{result.Version}\"";
        return Results.Created($"/api/v1/support-requests/{result.Id}",ApiResults.Ok(result,context));
    }
    private static async Task<IResult> ListAsync(Guid? seasonId,Guid? workAreaId,string? supportType,string? status,
        SupportRequestService service,HttpContext context,CancellationToken ct)
    {
        var page=PaginationRequest.FromQuery(context.Request.Query,"createdAt");
        var result=await service.ListAsync(seasonId,workAreaId,QueryEnum.Parse<SupportType>(supportType,"supportType"),
            QueryEnum.Parse<SupportRequestStatus>(status,"status"),page.Page,page.Size,
            page.Sort is null || page.Sort.EndsWith(",desc"),ct);
        return Results.Ok(ApiResults.Page(result.Items,result.Total,page.Page,page.Size,context));
    }
    private static async Task<IResult> GetAsync(Guid requestId,SupportRequestService service,HttpContext context,CancellationToken ct)
    {
        var result=await service.GetAsync(requestId,ct);context.Response.Headers.ETag=$"\"{result.Version}\"";
        return Results.Ok(ApiResults.Ok(result,context));
    }
    private static async Task<IResult> UpdateAsync(Guid requestId,UpdateSupportRequest request,[FromHeader(Name="If-Match")] string? ifMatch,
        SupportRequestService service,HttpContext context,CancellationToken ct)
    {
        var version=TaskVersionPrecondition.Resolve(request.Version,ifMatch,"SUPPORT_REQUEST_VERSION_MISMATCH");
        var result=await service.UpdateAsync(new(requestId,request.SupportType,request.HasDescription,request.Description,
            request.HasDeadline,request.Deadline,request.HasPositions,request.Positions,request.HasMaterials,request.Materials,version),ct);
        context.Response.Headers.ETag=$"\"{result.Version}\"";
        return Results.Ok(ApiResults.Ok(result,context));
    }
}
