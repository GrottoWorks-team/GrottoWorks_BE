using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Resource.Api.Common;
using Resource.Api.Requests;
using Resource.Application.Purchases;
using Resource.Domain.Purchases;

namespace Resource.Api.Endpoints;

/// <summary>F-RES-06: purchase requests (tag Purchases).</summary>
public static class PurchaseRequestEndpoints
{
    private const string VersionMismatch = "PURCHASE_REQUEST_VERSION_MISMATCH";

    private static readonly string[] Readers = [GrottoWorksRoles.Parish, GrottoWorksRoles.Leader, GrottoWorksRoles.MaterialOfficer];

    public static void MapPurchaseRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/purchase-requests").WithTags("Purchases");

        group.MapGet("", ListAsync)
            .WithName("listPurchaseRequests")
            .RequireAuthorization(policy => policy.RequireRole(Readers))
            .Produces<PagedResponse<PurchaseRequestDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("", CreateAsync)
            .WithName("createPurchaseRequest")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer))
            .Accepts<CreatePurchaseRequestRequest>("application/json")
            .Produces<ApiResponse<PurchaseRequestDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{requestId:guid}", GetAsync)
            .WithName("getPurchaseRequest")
            .RequireAuthorization(policy => policy.RequireRole([.. Readers, GrottoWorksRoles.Volunteer]))
            .Produces<ApiResponse<PurchaseRequestDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/{requestId:guid}", UpdateAsync)
            .WithName("updatePurchaseRequest")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer))
            .Accepts<UpdatePurchaseRequestRequest>("application/json")
            .Produces<ApiResponse<PurchaseRequestDto>>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{requestId:guid}/submit", SubmitAsync)
            .WithName("submitPurchaseRequest")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer))
            .Accepts<VersionedCommandRequest>("application/json")
            .Produces<ApiResponse<PurchaseRequestDto>>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{requestId:guid}/history", HistoryAsync)
            .WithName("getPurchaseRequestHistory")
            .RequireAuthorization(policy => policy.RequireRole(Readers))
            .Produces<ApiResponse<IReadOnlyList<StateChangeDto>>>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    /// <summary><c>PurchaseRequestFilter</c>: seasonId, workAreaId, status; sort <c>createdAt</c> (default desc); paged.</summary>
    private static async Task<IResult> ListAsync(
        HttpContext httpContext,
        ListPurchaseRequestsQueryHandler handler,
        CancellationToken cancellationToken)
    {
        var reader = new QueryReader(httpContext.Request.Query, "page", "size", "sort", "seasonId", "workAreaId", "status");
        var (page, size) = reader.Paging();
        var newestFirst = httpContext.Request.Query.ContainsKey("sort") ? reader.Sort("createdAt", "createdAt").Descending : true;

        var result = await handler.HandleAsync(
            new PurchaseRequestFilter(page, size, reader.Guid("seasonId"), reader.Guid("workAreaId"),
                reader.Enum<PurchaseRequestStatus>("status"), newestFirst),
            cancellationToken);
        return Results.Ok(ApiResults.Page(result.Items, result.TotalItems, page, size, httpContext));
    }

    private static async Task<IResult> CreateAsync(
        CreatePurchaseRequestRequest request,
        HttpContext httpContext,
        CreatePurchaseRequestCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var created = await handler.HandleAsync(
            new CreatePurchaseRequestCommand(request.WorkAreaId!.Value, Text.Clean(request.Reason), Items(request.Items) ?? []),
            cancellationToken);
        return Results.Json(ApiResults.Ok(created, httpContext), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> GetAsync(
        Guid requestId,
        HttpContext httpContext,
        GetPurchaseRequestQueryHandler handler,
        CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(await handler.HandleAsync(requestId, cancellationToken), httpContext));

    private static async Task<IResult> UpdateAsync(
        Guid requestId,
        UpdatePurchaseRequestRequest request,
        HttpContext httpContext,
        UpdatePurchaseRequestCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var updated = await handler.HandleAsync(
            new UpdatePurchaseRequestCommand(
                requestId,
                VersionHeader.Resolve(request.Version, httpContext, VersionMismatch),
                request.Reason,
                Items(request.Items)),
            cancellationToken);
        return Results.Ok(ApiResults.Ok(updated, httpContext));
    }

    private static async Task<IResult> SubmitAsync(
        Guid requestId,
        VersionedCommandRequest request,
        HttpContext httpContext,
        SubmitPurchaseRequestCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var submitted = await handler.HandleAsync(
            new PurchaseRequestCommand(requestId, VersionHeader.Resolve(request.Version, httpContext, VersionMismatch)),
            cancellationToken);
        return Results.Ok(ApiResults.Ok(submitted, httpContext));
    }

    private static async Task<IResult> HistoryAsync(
        Guid requestId,
        HttpContext httpContext,
        GetPurchaseRequestHistoryQueryHandler handler,
        CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(await handler.HandleAsync(requestId, cancellationToken), httpContext));

    private static List<PurchaseRequestItemInput>? Items(List<PurchaseRequestItemRequest>? items) =>
        items?.Select(item => new PurchaseRequestItemInput(
                item.MaterialId!.Value,
                item.RequestedQuantity!.Value,
                item.EstimatedCost,
                item.Note,
                item.ShortageOverrideReason))
            .ToList();
}
