using System.ComponentModel.DataAnnotations;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Exceptions;
using Microsoft.AspNetCore.Mvc;
using TaskExecution.Api.Common;
using TaskExecution.Api.Requests;
using TaskExecution.Application.Attendance;

namespace TaskExecution.Api.Endpoints;

public sealed record CheckInRequest([property:StringLength(2000)] string? Note);

public static partial class AttendanceEndpoints
{
    public static void MapAttendanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group=app.MapGroup("/api/v1").WithTags("Attendance").RequireAuthorization();
        group.MapPost("/task-assignments/{assignmentId:guid}/attendance/check-in-requests",CheckInAsync).WithName("requestCheckIn")
            .RequireAuthorization(p=>p.RequireRole(GrottoWorksRoles.Volunteer))
            .Produces<ApiResponse<AttendanceRequestDto>>(201).Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403).Produces<ProblemDetails>(404).Produces<ProblemDetails>(409)
            .Produces<ProblemDetails>(412).Produces<ProblemDetails>(422).Produces<ProblemDetails>(503);
        group.MapPost("/attendance-requests/{requestId:guid}/cancel",CancelAsync).WithName("cancelCheckInRequest")
            .RequireAuthorization(p=>p.RequireRole(GrottoWorksRoles.Volunteer))
            .Produces<ApiResponse<AttendanceRequestDto>>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403).Produces<ProblemDetails>(404).Produces<ProblemDetails>(409)
            .Produces<ProblemDetails>(412).Produces<ProblemDetails>(422).Produces<ProblemDetails>(503);
    }

    private static async Task<IResult> CheckInAsync(Guid assignmentId,CheckInRequest request,
        [FromHeader(Name="Idempotency-Key")] string? idempotencyKey,AttendanceService service,HttpContext context,CancellationToken ct)
    {
        var result=await service.CheckInAsync(assignmentId,request.Note,ParseKey(idempotencyKey),ct);
        context.Response.Headers.ETag=$"\"{result.Version}\"";
        return Results.Json(ApiResults.Ok(result,context),statusCode:201);
    }

    private static async Task<IResult> CancelAsync(Guid requestId,VersionedCommandRequest request,
        [FromHeader(Name="If-Match")] string? ifMatch,AttendanceService service,HttpContext context,CancellationToken ct)
    {
        var result=await service.CancelRequestAsync(requestId,TaskVersionPrecondition.Resolve(request.Version,ifMatch,"ATTENDANCE_VERSION_MISMATCH"),ct);
        context.Response.Headers.ETag=$"\"{result.Version}\"";
        return Results.Ok(ApiResults.Ok(result,context));
    }

    private static Guid ParseKey(string? value)
    {
        if (Guid.TryParse(value,out var key) && key!=Guid.Empty) return key;
        throw new DomainException("INVALID_IDEMPOTENCY_KEY","A UUID Idempotency-Key header is required.",400);
    }
}
