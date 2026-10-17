using BuildingBlocks.Web;
using BuildingBlocks.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ParishCoordination.Application.WorkAreaCategories;
using ParishCoordination.Application.WorkAreaCategories.Queries.ListWorkAreaCategories;
using ParishCoordination.Application.WorkAreaCategories.Commands.CreateWorkAreaCategory;
using ParishCoordination.Application.WorkAreaCategories.Commands.UpdateWorkAreaCategory;
using ParishCoordination.Api.Requests;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Api.Endpoints;

public static class WorkAreaCategoryEndpoints
{
    public static void MapWorkAreaCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/work-area-categories", ListWorkAreaCategoriesAsync)
            .WithName("listWorkAreaCategories")
            .WithTags("Parish")
            .RequireAuthorization()
            .Produces<ApiResponse<IReadOnlyList<WorkAreaCategoryDto>>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        app.MapPost("/api/v1/work-area-categories", CreateWorkAreaCategoryAsync)
            .WithName("createWorkAreaCategory")
            .WithTags("Parish")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Admin))
            .Accepts<CreateWorkAreaCategoryRequest>("application/json")
            .Produces<ApiResponse<WorkAreaCategoryDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapPatch("/api/v1/work-area-categories/{categoryId}", UpdateWorkAreaCategoryAsync)
            .WithName("updateWorkAreaCategory")
            .WithTags("Parish")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Admin))
            .Accepts<UpdateWorkAreaCategoryRequest>("application/json")
            .Produces<ApiResponse<WorkAreaCategoryDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> ListWorkAreaCategoriesAsync(
        WorkAreaCategoryStatus? status,
        HttpContext httpContext,
        ListWorkAreaCategoriesQueryHandler queryHandler,
        CancellationToken cancellationToken)
    {
        var categories = await queryHandler.HandleAsync(
            new ListWorkAreaCategoriesQuery(status),
            cancellationToken);

        return Results.Ok(ApiResults.Ok(categories, httpContext));
    }

    private static async Task<IResult> CreateWorkAreaCategoryAsync(
        CreateWorkAreaCategoryRequest request,
        HttpContext httpContext,
        CreateWorkAreaCategoryCommandHandler commandHandler,
        CancellationToken cancellationToken)
    {
        var result = await commandHandler.HandleAsync(
            new CreateWorkAreaCategoryCommand(
                request.Code!,
                request.Name!,
                CleanOptionalText(request.Description)),
            cancellationToken);

        if (result.Outcome == CreateWorkAreaCategoryOutcome.CodeConflict)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Work area category code conflict",
                detail: "A work area category with the same code already exists.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "WORK_AREA_CATEGORY_CODE_CONFLICT",
                    ["correlationId"] = CorrelationIdMiddlewareExtensions.GetCorrelationId(httpContext),
                    ["traceId"] = httpContext.TraceIdentifier
                });
        }

        return Results.Json(
            ApiResults.Ok(result.Category!, httpContext),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateWorkAreaCategoryAsync(
        Guid categoryId,
        UpdateWorkAreaCategoryRequest request,
        HttpContext httpContext,
        UpdateWorkAreaCategoryCommandHandler commandHandler,
        CancellationToken cancellationToken)
    {
        var result = await commandHandler.HandleAsync(
            new UpdateWorkAreaCategoryCommand(
                categoryId,
                request.Name,
                request.Description is not null,
                CleanOptionalText(request.Description),
                request.Status),
            cancellationToken);

        if (result.Outcome == UpdateWorkAreaCategoryOutcome.NotFound)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Work area category not found",
                detail: $"Work area category with ID '{categoryId}' was not found.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "WORK_AREA_CATEGORY_NOT_FOUND",
                    ["correlationId"] = CorrelationIdMiddlewareExtensions.GetCorrelationId(httpContext),
                    ["traceId"] = httpContext.TraceIdentifier
                });
        }

        return Results.Ok(ApiResults.Ok(result.Category!, httpContext));
    }

    private static string? CleanOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
