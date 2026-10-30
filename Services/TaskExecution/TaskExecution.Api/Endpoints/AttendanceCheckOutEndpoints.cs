using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using TaskExecution.Api.Common;
using TaskExecution.Api.Requests;
using TaskExecution.Application.Attendance;

namespace TaskExecution.Api.Endpoints;

public static partial class AttendanceEndpoints
{
    public static void MapCheckOutEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/task-assignments/{assignmentId:guid}/attendance/check-out",CheckOutAsync)
            .WithName("recordCheckOut").WithTags("Attendance").RequireAuthorization(p=>p.RequireRole(GrottoWorksRoles.Volunteer))
            .Produces<ApiResponse<AttendanceDto>>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403).Produces<ProblemDetails>(404).Produces<ProblemDetails>(409)
            .Produces<ProblemDetails>(412).Produces<ProblemDetails>(422).Produces<ProblemDetails>(503);
    }

    private static async Task<IResult> CheckOutAsync(Guid assignmentId,VersionedCommandRequest request,
        [FromHeader(Name="Idempotency-Key")] string? idempotencyKey,[FromHeader(Name="If-Match")] string? ifMatch,
        AttendanceService service,HttpContext context,CancellationToken ct)
    {
        var version=TaskVersionPrecondition.Resolve(request.Version,ifMatch,"ATTENDANCE_VERSION_MISMATCH");
        var result=await service.CheckOutAsync(assignmentId,version,ParseKey(idempotencyKey),ct);
        context.Response.Headers.ETag=$"\"{result.Version}\"";
        return Results.Ok(ApiResults.Ok(result,context));
    }
}
