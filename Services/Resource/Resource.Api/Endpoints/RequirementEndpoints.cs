using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Resource.Api.Common;
using Resource.Api.Requests;
using Resource.Application.Requirements;
using Resource.Domain.Inventory;

namespace Resource.Api.Endpoints;

/// <summary>F-RES-03: material requirements of a work area (tag Materials).</summary>
public static class RequirementEndpoints
{
    public static void MapRequirementEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/work-areas/{workAreaId:guid}/material-requirements", ListAsync)
            .WithName("listMaterialRequirements")
            .WithTags("Materials")
            .RequireAuthorization()
            .Produces<ApiResponse<IReadOnlyList<MaterialRequirementDto>>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/api/v1/work-areas/{workAreaId:guid}/material-requirements", CreateAsync)
            .WithName("createMaterialRequirement")
            .WithTags("Materials")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer))
            .Accepts<CreateMaterialRequirementRequest>("application/json")
            .Produces<ApiResponse<MaterialRequirementDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapPatch("/api/v1/material-requirements/{requirementId:guid}", UpdateAsync)
            .WithName("updateMaterialRequirement")
            .WithTags("Materials")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer))
            .Accepts<UpdateMaterialRequirementRequest>("application/json")
            .Produces<ApiResponse<MaterialRequirementDto>>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> ListAsync(
        Guid workAreaId,
        HttpContext httpContext,
        ListMaterialRequirementsQueryHandler handler,
        CancellationToken cancellationToken)
    {
        var status = new QueryReader(httpContext.Request.Query, "status").Enum<RequirementStatus>("status");
        var requirements = await handler.HandleAsync(workAreaId, status, cancellationToken);
        return Results.Ok(ApiResults.Ok(requirements, httpContext));
    }

    private static async Task<IResult> CreateAsync(
        Guid workAreaId,
        CreateMaterialRequirementRequest request,
        HttpContext httpContext,
        CreateMaterialRequirementCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var requirement = await handler.HandleAsync(
            new CreateMaterialRequirementCommand(
                workAreaId,
                request.MaterialId!.Value,
                request.RequiredQuantity!.Value,
                request.ExistingQuantity,
                Text.Clean(request.Note)),
            cancellationToken);
        return Results.Json(ApiResults.Ok(requirement, httpContext), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateAsync(
        Guid requirementId,
        UpdateMaterialRequirementRequest request,
        HttpContext httpContext,
        UpdateMaterialRequirementCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var requirement = await handler.HandleAsync(
            new UpdateMaterialRequirementCommand(
                requirementId,
                VersionHeader.Resolve(request.Version, httpContext, "REQUIREMENT_VERSION_MISMATCH"),
                request.RequiredQuantity,
                request.ExistingQuantity,
                request.Note,
                request.Status),
            cancellationToken);
        return Results.Ok(ApiResults.Ok(requirement, httpContext));
    }
}
