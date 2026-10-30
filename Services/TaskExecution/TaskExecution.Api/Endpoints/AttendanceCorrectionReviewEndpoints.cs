using System.ComponentModel.DataAnnotations;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using TaskExecution.Api.Common;
using TaskExecution.Application.Attendance;

namespace TaskExecution.Api.Endpoints;

public sealed class ReviewCorrectionRequest
{
    [Range(1,int.MaxValue)] public int? Version {get;init;}
    [StringLength(2000)] public string? ReviewNote {get;init;}
}

public static partial class AttendanceCorrectionEndpoints
{
    public static void MapAttendanceCorrectionReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group=app.MapGroup("/api/v1").WithTags("Attendance").RequireAuthorization(p=>p.RequireRole(GrottoWorksRoles.Leader));
        group.MapPost("/attendance-corrections/{correctionId:guid}/approve",ApproveAsync).WithName("approveAttendanceCorrection")
            .Produces<ApiResponse<AttendanceCorrectionDto>>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403).Produces<ProblemDetails>(404).Produces<ProblemDetails>(409)
            .Produces<ProblemDetails>(412).Produces<ProblemDetails>(422).Produces<ProblemDetails>(503);
        group.MapPost("/attendance-corrections/{correctionId:guid}/reject",RejectAsync).WithName("rejectAttendanceCorrection")
            .Produces<ApiResponse<AttendanceCorrectionDto>>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403).Produces<ProblemDetails>(404).Produces<ProblemDetails>(409)
            .Produces<ProblemDetails>(412).Produces<ProblemDetails>(422).Produces<ProblemDetails>(503);
    }
    private static Task<IResult> ApproveAsync(Guid correctionId,ReviewCorrectionRequest request,[FromHeader(Name="If-Match")] string? ifMatch,
        AttendanceService service,HttpContext context,CancellationToken ct)=>ReviewCorrectionAsync(correctionId,true,request,ifMatch,service,context,ct);
    private static Task<IResult> RejectAsync(Guid correctionId,ReviewCorrectionRequest request,[FromHeader(Name="If-Match")] string? ifMatch,
        AttendanceService service,HttpContext context,CancellationToken ct)=>ReviewCorrectionAsync(correctionId,false,request,ifMatch,service,context,ct);
    private static async Task<IResult> ReviewCorrectionAsync(Guid id,bool approve,ReviewCorrectionRequest request,string? ifMatch,
        AttendanceService service,HttpContext context,CancellationToken ct)
    {
        var result=await service.ReviewCorrectionAsync(id,approve,request.ReviewNote,
            TaskVersionPrecondition.Resolve(request.Version,ifMatch,"ATTENDANCE_VERSION_MISMATCH"),ct);
        context.Response.Headers.ETag=$"\"{result.Version}\"";
        return Results.Ok(ApiResults.Ok(result,context));
    }
}
