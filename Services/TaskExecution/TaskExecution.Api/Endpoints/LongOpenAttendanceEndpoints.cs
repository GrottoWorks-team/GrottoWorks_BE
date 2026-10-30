using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using TaskExecution.Application.Attendance;

namespace TaskExecution.Api.Endpoints;

public static class LongOpenAttendanceEndpoints
{
    public static void MapLongOpenAttendanceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/attendance/open-sessions",ListAsync).WithName("listLongOpenSessions").WithTags("Attendance")
            .WithSummary("List open sessions beyond hours, defaulting to Attendance:LongOpenHours (12).")
            .RequireAuthorization(p=>p.RequireRole(GrottoWorksRoles.Leader,GrottoWorksRoles.Admin))
            .Produces<ApiResponse<IReadOnlyList<AttendanceDto>>>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(401).Produces<ProblemDetails>(403);
    }
    private static async Task<IResult> ListAsync(int? hours,LongOpenAttendanceQuery query,HttpContext context,CancellationToken ct)=>
        Results.Ok(ApiResults.Ok(await query.HandleAsync(hours,ct),context));
}
