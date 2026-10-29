using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Resource.Api.Common;
using Resource.Api.Requests;
using Resource.Application.Inventory;
using Resource.Domain.Inventory;

namespace Resource.Api.Endpoints;

/// <summary>F-RES-17: task material reports (tag Materials).</summary>
public static class MaterialReportEndpoints
{
    private const string VersionMismatch = "MATERIAL_REPORT_VERSION_MISMATCH";

    public static void MapMaterialReportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/tasks/{taskId:guid}/material-reports", SubmitAsync)
            .WithName("submitTaskMaterialReport")
            .WithTags("Materials")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Volunteer))
            .Accepts<TaskMaterialReportRequest>("application/json")
            .Produces<ApiResponse<TaskMaterialReportDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapGet("/api/v1/tasks/{taskId:guid}/material-reports", ListAsync)
            .WithName("listTaskMaterialReports")
            .WithTags("Materials")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Leader, GrottoWorksRoles.MaterialOfficer, GrottoWorksRoles.Volunteer))
            .Produces<ApiResponse<IReadOnlyList<TaskMaterialReportDto>>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/api/v1/task-material-reports/{reportId:guid}/verify", VerifyAsync)
            .WithName("verifyTaskMaterialReport")
            .WithTags("Materials")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer))
            .Accepts<VerifyMaterialReportRequest>("application/json")
            .Produces<ApiResponse<TaskMaterialReportDto>>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapPost("/api/v1/task-material-reports/{reportId:guid}/request-revision", RequestRevisionAsync)
            .WithName("requestMaterialReportRevision")
            .WithTags("Materials")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer))
            .Accepts<ReasonCommandRequest>("application/json")
            .Produces<ApiResponse<TaskMaterialReportDto>>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> SubmitAsync(
        Guid taskId,
        TaskMaterialReportRequest request,
        HttpContext httpContext,
        SubmitMaterialReportCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var report = await handler.HandleAsync(
            new SubmitMaterialReportCommand(
                taskId,
                request.Items!.Select(item => new ReportedLineInput(item.MaterialAllocationItemId!.Value, item.RemainingQuantity ?? 0,
                    item.DamagedQuantity ?? 0, item.LostQuantity ?? 0, item.Note)).ToList(),
                request.Note),
            cancellationToken);
        return Results.Json(ApiResults.Ok(report, httpContext), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> ListAsync(
        Guid taskId,
        HttpContext httpContext,
        ListTaskMaterialReportsQueryHandler handler,
        CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(await handler.HandleAsync(taskId, cancellationToken), httpContext));

    private static async Task<IResult> VerifyAsync(
        Guid reportId,
        VerifyMaterialReportRequest request,
        HttpContext httpContext,
        VerifyMaterialReportCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var report = await handler.HandleAsync(
            new VerifyMaterialReportCommand(
                reportId,
                VersionHeader.Resolve(request.Version, httpContext, VersionMismatch),
                request.Items!.Select(item => new VerifiedLineInput(item.ReportItemId!.Value, item.RemainingQuantity!.Value,
                    item.DamagedQuantity!.Value, item.LostQuantity!.Value)).ToList()),
            cancellationToken);
        return Results.Ok(ApiResults.Ok(report, httpContext));
    }

    private static async Task<IResult> RequestRevisionAsync(
        Guid reportId,
        ReasonCommandRequest request,
        HttpContext httpContext,
        RequestReportRevisionCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var report = await handler.HandleAsync(
            new RequestReportRevisionCommand(reportId, VersionHeader.Resolve(request.Version, httpContext, VersionMismatch), request.Reason),
            cancellationToken);
        return Results.Ok(ApiResults.Ok(report, httpContext));
    }
}
