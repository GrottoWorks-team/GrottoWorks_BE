using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Resource.Api.Common;
using Resource.Api.Requests;
using Resource.Application.Catalog;
using Resource.Domain.Catalog;

namespace Resource.Api.Endpoints;

/// <summary>F-RES-02: material categories and the material catalog (tag Materials).</summary>
public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/material-categories", ListCategoriesAsync)
            .WithName("listMaterialCategories")
            .WithTags("Materials")
            .RequireAuthorization()
            .Produces<ApiResponse<IReadOnlyList<MaterialCategoryDto>>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/v1/material-categories", CreateCategoryAsync)
            .WithName("createMaterialCategory")
            .WithTags("Materials")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer))
            .Accepts<CreateMaterialCategoryRequest>("application/json")
            .Produces<ApiResponse<MaterialCategoryDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapPatch("/api/v1/material-categories/{categoryId:guid}", UpdateCategoryAsync)
            .WithName("updateMaterialCategory")
            .WithTags("Materials")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer))
            .Accepts<UpdateMaterialCategoryRequest>("application/json")
            .Produces<ApiResponse<MaterialCategoryDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapGet("/api/v1/materials", ListMaterialsAsync)
            .WithName("listMaterials")
            .WithTags("Materials")
            .RequireAuthorization()
            .Produces<PagedResponse<MaterialDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/v1/materials", CreateMaterialAsync)
            .WithName("createMaterial")
            .WithTags("Materials")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer))
            .Accepts<CreateMaterialRequest>("application/json")
            .Produces<ApiResponse<MaterialDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapPatch("/api/v1/materials/{materialId:guid}", UpdateMaterialAsync)
            .WithName("updateMaterial")
            .WithTags("Materials")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer))
            .Accepts<UpdateMaterialRequest>("application/json")
            .Produces<ApiResponse<MaterialDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> ListCategoriesAsync(
        HttpContext httpContext,
        ListMaterialCategoriesQueryHandler handler,
        CancellationToken cancellationToken)
    {
        var status = new QueryReader(httpContext.Request.Query, "status").Enum<CatalogStatus>("status");
        var categories = await handler.HandleAsync(status, cancellationToken);
        return Results.Ok(ApiResults.Ok(categories, httpContext));
    }

    private static async Task<IResult> CreateCategoryAsync(
        CreateMaterialCategoryRequest request,
        HttpContext httpContext,
        CreateMaterialCategoryCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var category = await handler.HandleAsync(
            new CreateMaterialCategoryCommand(request.Code!, request.Name!, Text.Clean(request.Description)),
            cancellationToken);
        return Results.Json(ApiResults.Ok(category, httpContext), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateCategoryAsync(
        Guid categoryId,
        UpdateMaterialCategoryRequest request,
        HttpContext httpContext,
        UpdateMaterialCategoryCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var category = await handler.HandleAsync(
            new UpdateMaterialCategoryCommand(categoryId, Text.Clean(request.Name), request.Description, request.Status),
            cancellationToken);
        return Results.Ok(ApiResults.Ok(category, httpContext));
    }

    private static async Task<IResult> ListMaterialsAsync(
        HttpContext httpContext,
        ListMaterialsQueryHandler handler,
        CancellationToken cancellationToken)
    {
        var reader = new QueryReader(httpContext.Request.Query, "page", "size", "sort", "categoryId", "status", "q");
        var (page, size) = reader.Paging();
        var (field, descending) = reader.Sort("name", "name", "code");

        var criteria = new MaterialSearchCriteria(
            page,
            size,
            reader.Guid("categoryId"),
            reader.Enum<CatalogStatus>("status"),
            reader.String("q"),
            field == "code" ? MaterialSortField.Code : MaterialSortField.Name,
            descending);

        var result = await handler.HandleAsync(criteria, cancellationToken);
        return Results.Ok(ApiResults.Page(result.Items, result.TotalItems, page, size, httpContext));
    }

    private static async Task<IResult> CreateMaterialAsync(
        CreateMaterialRequest request,
        HttpContext httpContext,
        CreateMaterialCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var material = await handler.HandleAsync(
            new CreateMaterialCommand(
                request.CategoryId!.Value,
                request.Code!,
                request.Name!,
                request.Unit!,
                Text.Clean(request.Description)),
            cancellationToken);
        return Results.Json(ApiResults.Ok(material, httpContext), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateMaterialAsync(
        Guid materialId,
        UpdateMaterialRequest request,
        HttpContext httpContext,
        UpdateMaterialCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var material = await handler.HandleAsync(
            new UpdateMaterialCommand(
                materialId,
                request.CategoryId,
                Text.Clean(request.Name),
                Text.Clean(request.Unit),
                request.Description,
                request.Status),
            cancellationToken);
        return Results.Ok(ApiResults.Ok(material, httpContext));
    }
}
