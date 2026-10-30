using System.ComponentModel.DataAnnotations;
using BuildingBlocks.Pagination;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using TaskExecution.Api.Common;
using TaskExecution.Application.Attendance;
using TaskExecution.Domain.Attendance;

namespace TaskExecution.Api.Endpoints;

public sealed class ReviewAttendanceRequest
{
    [Range(1,int.MaxValue)] public int? Version {get;init;}
    [StringLength(2000)] public string? ReviewNote {get;init;}
}

public static class AttendanceReviewEndpoints
{
    public static void MapAttendanceReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group=app.MapGroup("/api/v1").WithTags("Attendance").RequireAuthorization(p=>p.RequireRole(GrottoWorksRoles.Leader));
        group.MapGet("/work-areas/{workAreaId:guid}/attendance-requests",ListAsync).WithName("listAttendanceRequests")
            .Produces<PagedResponse<AttendanceRequestDto>>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403).Produces<ProblemDetails>(404);
        group.MapPost("/attendance-requests/{requestId:guid}/approve",ApproveAsync).WithName("approveAttendanceRequest")
            .Produces<ApiResponse<AttendanceRequestDto>>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403).Produces<ProblemDetails>(404).Produces<ProblemDetails>(409)
            .Produces<ProblemDetails>(412).Produces<ProblemDetails>(422).Produces<ProblemDetails>(503);
        group.MapPost("/attendance-requests/{requestId:guid}/reject",RejectAsync).WithName("rejectAttendanceRequest")
            .Produces<ApiResponse<AttendanceRequestDto>>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403).Produces<ProblemDetails>(404).Produces<ProblemDetails>(409)
            .Produces<ProblemDetails>(412).Produces<ProblemDetails>(422).Produces<ProblemDetails>(503);
    }
    private static async Task<IResult> ListAsync(Guid workAreaId,string? status,AttendanceService service,HttpContext context,CancellationToken ct)
    {
        var page=PaginationRequest.FromQuery(context.Request.Query,"requestedAt");
        var result=await service.RequestsAsync(workAreaId,QueryEnum.Parse<AttendanceRequestStatus>(status,"status")??AttendanceRequestStatus.Pending,
            page.Page,page.Size,page.Sort?.EndsWith(",desc")==true,ct);
        return Results.Ok(ApiResults.Page(result.Items,result.Total,page.Page,page.Size,context));
    }
    private static Task<IResult> ApproveAsync(Guid requestId,ReviewAttendanceRequest request,[FromHeader(Name="If-Match")] string? ifMatch,
        AttendanceService service,HttpContext context,CancellationToken ct)=>ReviewAsync(requestId,true,request,ifMatch,service,context,ct);
    private static Task<IResult> RejectAsync(Guid requestId,ReviewAttendanceRequest request,[FromHeader(Name="If-Match")] string? ifMatch,
        AttendanceService service,HttpContext context,CancellationToken ct)=>ReviewAsync(requestId,false,request,ifMatch,service,context,ct);
    private static async Task<IResult> ReviewAsync(Guid id,bool approve,ReviewAttendanceRequest request,string? header,
        AttendanceService service,HttpContext context,CancellationToken ct)
    {
        var result=await service.ReviewRequestAsync(id,approve,request.ReviewNote,
            TaskVersionPrecondition.Resolve(request.Version,header,"ATTENDANCE_VERSION_MISMATCH"),ct);
        context.Response.Headers.ETag=$"\"{result.Version}\"";
        return Results.Ok(ApiResults.Ok(result,context));
    }
}
