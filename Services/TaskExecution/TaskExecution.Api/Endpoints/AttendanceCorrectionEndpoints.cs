using System.ComponentModel.DataAnnotations;
using BuildingBlocks.Pagination;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using TaskExecution.Api.Common;
using TaskExecution.Application.Attendance;
using TaskExecution.Domain.Attendance;

namespace TaskExecution.Api.Endpoints;

public sealed class AttendanceCorrectionRequest
{
    [Required] public DateTimeOffset? CheckInAt {get;init;}
    [Required] public DateTimeOffset? CheckOutAt {get;init;}
    [Required,StringLength(2000,MinimumLength=1)] public string? Reason {get;init;}
    [Range(1,int.MaxValue)] public int? Version {get;init;}
}

public static partial class AttendanceCorrectionEndpoints
{
    public static void MapAttendanceCorrectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group=app.MapGroup("/api/v1").WithTags("Attendance").RequireAuthorization();
        group.MapPost("/attendance/{attendanceId:guid}/correction-requests",CreateAsync).WithName("requestAttendanceCorrection")
            .RequireAuthorization(p=>p.RequireRole(GrottoWorksRoles.Volunteer))
            .Produces<ApiResponse<AttendanceCorrectionDto>>(201).Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403).Produces<ProblemDetails>(404).Produces<ProblemDetails>(409)
            .Produces<ProblemDetails>(412).Produces<ProblemDetails>(422).Produces<ProblemDetails>(503);
        group.MapGet("/work-areas/{workAreaId:guid}/attendance-corrections",ListAsync).WithName("listAttendanceCorrections")
            .RequireAuthorization(p=>p.RequireRole(GrottoWorksRoles.Leader))
            .Produces<PagedResponse<AttendanceCorrectionDto>>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403).Produces<ProblemDetails>(404);
    }
    private static async Task<IResult> CreateAsync(Guid attendanceId,AttendanceCorrectionRequest request,[FromHeader(Name="If-Match")] string? ifMatch,
        AttendanceService service,HttpContext context,CancellationToken ct)
    {
        var version=TaskVersionPrecondition.Resolve(request.Version,ifMatch,"ATTENDANCE_VERSION_MISMATCH");
        var result=await service.RequestCorrectionAsync(attendanceId,request.CheckInAt!.Value,request.CheckOutAt!.Value,request.Reason!,version,ct);
        context.Response.Headers.ETag=$"\"{result.Version}\"";
        return Results.Json(ApiResults.Ok(result,context),statusCode:201);
    }
    private static async Task<IResult> ListAsync(Guid workAreaId,string? status,AttendanceService service,HttpContext context,CancellationToken ct)
    {
        var page=PaginationRequest.FromQuery(context.Request.Query,"createdAt");
        var result=await service.CorrectionsAsync(workAreaId,QueryEnum.Parse<AttendanceCorrectionStatus>(status,"status")??AttendanceCorrectionStatus.Pending,
            page.Page,page.Size,page.Sort?.EndsWith(",desc")==true,ct);
        return Results.Ok(ApiResults.Page(result.Items,result.Total,page.Page,page.Size,context));
    }
}
