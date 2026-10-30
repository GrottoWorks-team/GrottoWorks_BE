using BuildingBlocks.Pagination;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using TaskExecution.Api.Common;
using TaskExecution.Application.Attendance;
using TaskExecution.Domain.Attendance;

namespace TaskExecution.Api.Endpoints;

public static class AttendanceQueryEndpoints
{
    public static void MapAttendanceQueryEndpoints(this IEndpointRouteBuilder app)
    {
        var group=app.MapGroup("/api/v1").WithTags("Attendance").RequireAuthorization();
        group.MapGet("/users/me/attendance",PersonalAsync).WithName("listMyAttendance")
            .RequireAuthorization(p=>p.RequireRole(GrottoWorksRoles.Volunteer))
            .Produces<PagedResponse<AttendanceDto>>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(401).Produces<ProblemDetails>(403);
        group.MapGet("/work-areas/{workAreaId:guid}/working-hours",HoursAsync).WithName("getWorkingHours")
            .RequireAuthorization(p=>p.RequireRole(GrottoWorksRoles.Parish,GrottoWorksRoles.Leader))
            .Produces<ApiResponse<IReadOnlyList<WorkingHourSummary>>>().Produces<ProblemDetails>(400)
            .Produces<ProblemDetails>(401).Produces<ProblemDetails>(403).Produces<ProblemDetails>(404);
    }

    private static async Task<IResult> PersonalAsync(Guid? seasonId,Guid? workAreaId,string? status,DateTimeOffset? from,DateTimeOffset? to,
        AttendanceQueries queries,HttpContext context,CancellationToken ct)
    {
        var page=PaginationRequest.FromQuery(context.Request.Query,"checkInAt");
        var result=await queries.PersonalAsync(new(seasonId,workAreaId,QueryEnum.Parse<AttendanceStatus>(status,"status"),from,to,
            page.Page,page.Size,page.Sort is null || page.Sort.EndsWith(",desc")),ct);
        return Results.Ok(ApiResults.Page(result.Items,result.Total,page.Page,page.Size,context));
    }
    private static async Task<IResult> HoursAsync(Guid workAreaId,DateTimeOffset? from,DateTimeOffset? to,AttendanceQueries queries,HttpContext context,CancellationToken ct) =>
        Results.Ok(ApiResults.Ok(await queries.HoursAsync(workAreaId,from,to,ct),context));
}
