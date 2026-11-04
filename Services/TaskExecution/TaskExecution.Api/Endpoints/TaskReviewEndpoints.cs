using System.ComponentModel.DataAnnotations;
using BuildingBlocks.Pagination;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using TaskExecution.Api.Common;
using TaskExecution.Application.Tasks;

namespace TaskExecution.Api.Endpoints;

public sealed record CreateProgressRequest([property:Range(0,100)] int PercentComplete,[property:StringLength(2000)] string? Note,
    IReadOnlyList<EvidenceInput>? Evidence,int? Version);
public sealed record SubmitTaskRequest([property:StringLength(2000)] string? Note,IReadOnlyList<EvidenceInput>? Evidence,int? Version);
public sealed record ReviewTaskRequest([property:StringLength(2000)] string? Reason,int? Version);
public static class TaskReviewEndpoints
{
    public static void MapTaskReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group=app.MapGroup("/api/v1/tasks/{taskId:guid}").WithTags("Tasks").RequireAuthorization();
        group.MapGet("/progress",ListAsync).WithName("listTaskProgress").Produces<PagedResponse<TaskProgressDto>>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
        group.MapPost("/progress",AddAsync).WithName("addTaskProgress").RequireAuthorization(p=>p.RequireRole(GrottoWorksRoles.Volunteer))
            .Produces<ApiResponse<TaskProgressDto>>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404)
            .ProducesProblem(409).ProducesProblem(412).ProducesProblem(422).ProducesProblem(503);
        Configure(group.MapPost("/submit",SubmitAsync).WithName("submitTaskForReview").RequireAuthorization(p=>p.RequireRole(GrottoWorksRoles.Volunteer)));
        Configure(group.MapPost("/resubmit",ResubmitAsync).WithName("resubmitTask").RequireAuthorization(p=>p.RequireRole(GrottoWorksRoles.Volunteer)));
        group.MapGet("/review-package",PackageAsync).WithName("getTaskReviewPackage").RequireAuthorization(p=>p.RequireRole(GrottoWorksRoles.Leader))
            .Produces<ApiResponse<TaskReviewPackage>>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(503);
        Configure(group.MapPost("/approve",ApproveAsync).WithName("approveTaskCompletion").RequireAuthorization(p=>p.RequireRole(GrottoWorksRoles.Leader)));
        Configure(group.MapPost("/request-revision",RevisionAsync).WithName("requestTaskRevision").RequireAuthorization(p=>p.RequireRole(GrottoWorksRoles.Leader)));
        group.MapGet("/progress/{progressId:guid}/evidence/{fileId:guid}/download-url",DownloadAsync).WithName("getTaskEvidenceDownloadUrl")
            .Produces<ApiResponse<FileDownload>>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(503);
    }
    private static void Configure(RouteHandlerBuilder endpoint)=>endpoint.Produces<ApiResponse<TaskDto>>()
        .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
        .ProducesProblem(412).ProducesProblem(422).ProducesProblem(503);
    private static async Task<IResult> ListAsync(Guid taskId,TaskReviewService service,HttpContext context,CancellationToken ct)
    {
        var page=PaginationRequest.FromQuery(context.Request.Query,"submittedAt");
        var result=await service.GetPageAsync(taskId,page.Page,page.Size,page.Sort?.EndsWith(",asc")==true,ct);
        return Results.Ok(ApiResults.Page(result.Items,result.Total,page.Page,page.Size,context));
    }
    private static async Task<IResult> AddAsync(Guid taskId,CreateProgressRequest request,[FromHeader(Name="If-Match")] string? ifMatch,TaskReviewService service,HttpContext context,CancellationToken ct)
    {
        var result=await service.AddAsync(taskId,new(request.PercentComplete,request.Note,request.Evidence,TaskVersionPrecondition.Resolve(request.Version,ifMatch)),false,false,ct);
        return Results.Json(ApiResults.Ok(result,context),statusCode:201);
    }
    private static Task<IResult> SubmitAsync(Guid taskId,SubmitTaskRequest request,[FromHeader(Name="If-Match")] string? ifMatch,TaskReviewService service,HttpContext context,CancellationToken ct)=>SubmitCoreAsync(taskId,request,ifMatch,service,context,false,ct);
    private static Task<IResult> ResubmitAsync(Guid taskId,SubmitTaskRequest request,[FromHeader(Name="If-Match")] string? ifMatch,TaskReviewService service,HttpContext context,CancellationToken ct)=>SubmitCoreAsync(taskId,request,ifMatch,service,context,true,ct);
    private static async Task<IResult> SubmitCoreAsync(Guid taskId,SubmitTaskRequest request,string? ifMatch,TaskReviewService service,HttpContext context,bool resubmit,CancellationToken ct)
    {
        await service.AddAsync(taskId,new(100,request.Note,request.Evidence,TaskVersionPrecondition.Resolve(request.Version,ifMatch)),true,resubmit,ct);
        var task=await context.RequestServices.GetRequiredService<GetTaskQueryHandler>().HandleAsync(taskId,ct);
        context.Response.Headers.ETag=$"\"{task.Version}\"";return Results.Ok(ApiResults.Ok(task,context));
    }
    private static async Task<IResult> PackageAsync(Guid taskId,TaskReviewService service,HttpContext context,CancellationToken ct)=>Results.Ok(ApiResults.Ok(await service.PackageAsync(taskId,ct),context));
    private static Task<IResult> ApproveAsync(Guid taskId,ReviewTaskRequest request,[FromHeader(Name="If-Match")] string? ifMatch,TaskReviewService service,HttpContext context,CancellationToken ct)=>ReviewAsync(taskId,request,ifMatch,service,context,true,ct);
    private static Task<IResult> RevisionAsync(Guid taskId,ReviewTaskRequest request,[FromHeader(Name="If-Match")] string? ifMatch,TaskReviewService service,HttpContext context,CancellationToken ct)=>ReviewAsync(taskId,request,ifMatch,service,context,false,ct);
    private static async Task<IResult> ReviewAsync(Guid taskId,ReviewTaskRequest request,string? ifMatch,TaskReviewService service,HttpContext context,bool approve,CancellationToken ct)
    {
        var result=await service.ReviewAsync(taskId,approve,request.Reason,TaskVersionPrecondition.Resolve(request.Version,ifMatch),ct);
        context.Response.Headers.ETag=$"\"{result.Version}\"";return Results.Ok(ApiResults.Ok(result,context));
    }
    private static async Task<IResult> DownloadAsync(Guid taskId,Guid progressId,Guid fileId,TaskReviewService service,HttpContext context,CancellationToken ct)=>Results.Ok(ApiResults.Ok(await service.DownloadAsync(taskId,progressId,fileId,ct),context));
}
