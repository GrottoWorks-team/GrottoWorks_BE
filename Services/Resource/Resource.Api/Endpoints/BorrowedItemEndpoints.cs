using System.Globalization;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Resource.Api.Common;
using Resource.Api.Requests;
using Resource.Application.Contributions;
using Resource.Domain;
using Resource.Domain.Contributions;

namespace Resource.Api.Endpoints;

/// <summary>F-RES-18: loan offers and borrowed items (tag Contributions).</summary>
public static class BorrowedItemEndpoints
{
    private const string VersionMismatch = "BORROWED_ITEM_VERSION_MISMATCH";

    public static void MapBorrowedItemEndpoints(this IEndpointRouteBuilder app)
    {
        var offers = app.MapGroup("/api/v1/loan-offers").WithTags("Contributions");
        offers.MapPost("", OfferAsync)
            .WithName("offerItemForLoan")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Volunteer))
            .Accepts<LoanOfferRequest>("application/json")
            .Produces<ApiResponse<BorrowedItemDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
        offers.MapGet("", ListOffersAsync)
            .WithName("listLoanOffers")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer))
            .Produces<PagedResponse<BorrowedItemDto>>();
        offers.MapPost("/{offerId:guid}/accept", AcceptAsync)
            .WithName("acceptLoanOffer")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer))
            .Accepts<AcceptLoanOfferRequest>("application/json")
            .Produces<ApiResponse<BorrowedItemDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);
        offers.MapPost("/{offerId:guid}/reject", RejectAsync)
            .WithName("rejectLoanOffer")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer))
            .Accepts<ReasonCommandRequest>("application/json")
            .Produces<ApiResponse<BorrowedItemDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        var items = app.MapGroup("/api/v1/borrowed-items").WithTags("Contributions");
        items.MapGet("", ListAsync)
            .WithName("listBorrowedItems")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Parish, GrottoWorksRoles.Leader, GrottoWorksRoles.MaterialOfficer))
            .Produces<PagedResponse<BorrowedItemDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);
        items.MapGet("/overdue", ListOverdueAsync)
            .WithName("listOverdueBorrowedItems")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer))
            .Produces<ApiResponse<IReadOnlyList<BorrowedItemDto>>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);
        items.MapPost("/{itemId:guid}/issue", IssueAsync)
            .WithName("issueBorrowedItem")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer))
            .Accepts<IssueBorrowedItemRequest>("application/json")
            .Produces<ApiResponse<BorrowedItemDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        items.MapPut("/{itemId:guid}/assignment", AssignAsync)
            .WithName("assignBorrowedItem")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer))
            .Accepts<BorrowedItemAssignmentRequest>("application/json")
            .Produces<ApiResponse<BorrowedItemDto>>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
        items.MapPost("/{itemId:guid}/return", ReturnAsync)
            .WithName("returnBorrowedItem")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer))
            .Accepts<ReturnBorrowedItemRequest>("application/json")
            .Produces<ApiResponse<BorrowedItemDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
        items.MapPost("/{itemId:guid}/incidents", IncidentAsync)
            .WithName("recordBorrowedItemIncident")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer))
            .Accepts<BorrowedItemIncidentRequest>("application/json")
            .Produces<ApiResponse<BorrowTransactionDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> OfferAsync(LoanOfferRequest request, HttpContext httpContext, OfferItemForLoanCommandHandler handler, CancellationToken cancellationToken)
    {
        var item = await handler.HandleAsync(
            new OfferItemForLoanCommand(new LoanOfferInput(request.SeasonId!.Value, request.WorkAreaId, request.OwnerName, request.OwnerPhone,
                request.OwnerAddress, request.OwnerNote, request.ItemName, request.Quantity!.Value, request.Description, request.InitialCondition,
                request.ExpectedReturnDate!.Value)),
            cancellationToken);
        return Results.Json(ApiResults.Ok(item, httpContext), statusCode: StatusCodes.Status201Created);
    }

    /// <summary><c>StatusFilter</c>: status (OFFERED default, ACCEPTED, REJECTED); paged.</summary>
    private static async Task<IResult> ListOffersAsync(HttpContext httpContext, ListLoanOffersQueryHandler handler, CancellationToken cancellationToken)
    {
        var reader = new QueryReader(httpContext.Request.Query, "page", "size", "status");
        var (page, size) = reader.Paging();
        var result = await handler.HandleAsync(page, size, reader.Enum<BorrowedItemStatus>("status"), cancellationToken);
        return Results.Ok(ApiResults.Page(result.Items, result.TotalItems, page, size, httpContext));
    }

    private static async Task<IResult> AcceptAsync(Guid offerId, AcceptLoanOfferRequest request, HttpContext httpContext, BorrowedItemCommandHandler handler, CancellationToken cancellationToken)
    {
        var item = await handler.AcceptAsync(
            new AcceptLoanOfferCommand(offerId, VersionHeader.Resolve(request.Version, httpContext, VersionMismatch), request.WorkAreaId, request.ExpectedReturnDate),
            cancellationToken);
        return Results.Json(ApiResults.Ok(item, httpContext), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> RejectAsync(Guid offerId, ReasonCommandRequest request, HttpContext httpContext, BorrowedItemCommandHandler handler, CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(
            await handler.RejectAsync(new RejectLoanOfferCommand(offerId, VersionHeader.Resolve(request.Version, httpContext, VersionMismatch), request.Reason), cancellationToken),
            httpContext));

    /// <summary><c>BorrowedItemFilter</c>: seasonId, workAreaId, status, overdue; earliest due first; paged.</summary>
    private static async Task<IResult> ListAsync(HttpContext httpContext, ListBorrowedItemsQueryHandler handler, CancellationToken cancellationToken)
    {
        var reader = new QueryReader(httpContext.Request.Query, "page", "size", "seasonId", "workAreaId", "status", "overdue");
        var (page, size) = reader.Paging();
        var overdue = reader.String("overdue") switch
        {
            null => (bool?)null,
            var value when bool.TryParse(value, out var parsed) => parsed,
            _ => throw new DomainRuleException("INVALID_FILTER_VALUE", "'overdue' must be true or false.", DomainErrorKind.BadRequest)
        };
        var result = await handler.HandleAsync(
            new BorrowedItemFilter(page, size, reader.Guid("seasonId"), reader.Guid("workAreaId"), reader.Enum<BorrowedItemStatus>("status"), overdue),
            cancellationToken);
        return Results.Ok(ApiResults.Page(result.Items, result.TotalItems, page, size, httpContext));
    }

    /// <summary><c>DateQuery</c>: date (YYYY-MM-DD, default today) and withinDays (0–30, default 2).</summary>
    private static async Task<IResult> ListOverdueAsync(HttpContext httpContext, ListOverdueBorrowedItemsQueryHandler handler, CancellationToken cancellationToken)
    {
        var reader = new QueryReader(httpContext.Request.Query, "date", "withinDays");
        DateOnly? date = reader.String("date") switch
        {
            null => null,
            var value when DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) => parsed,
            _ => throw new DomainRuleException("INVALID_FILTER_VALUE", "'date' must be YYYY-MM-DD.", DomainErrorKind.BadRequest)
        };
        var within = reader.String("withinDays") switch
        {
            null => 2,
            var value when int.TryParse(value, out var parsed) && parsed is >= 0 and <= 30 => parsed,
            _ => throw new DomainRuleException("INVALID_FILTER_VALUE", "'withinDays' must be between 0 and 30.", DomainErrorKind.BadRequest)
        };
        return Results.Ok(ApiResults.Ok(await handler.HandleAsync(date, within, cancellationToken), httpContext));
    }

    private static async Task<IResult> IssueAsync(Guid itemId, IssueBorrowedItemRequest request, HttpContext httpContext, BorrowedItemCommandHandler handler, CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(
            await handler.IssueAsync(new IssueBorrowedItemCommand(itemId, VersionHeader.Resolve(request.Version, httpContext, VersionMismatch), request.BorrowDate, request.Note), cancellationToken),
            httpContext));

    private static async Task<IResult> AssignAsync(Guid itemId, BorrowedItemAssignmentRequest request, HttpContext httpContext, BorrowedItemCommandHandler handler, CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(
            await handler.AssignAsync(new AssignBorrowedItemCommand(itemId, VersionHeader.Resolve(request.Version, httpContext, VersionMismatch), request.WorkAreaId!.Value, request.TaskId), cancellationToken),
            httpContext));

    private static async Task<IResult> ReturnAsync(Guid itemId, ReturnBorrowedItemRequest request, HttpContext httpContext, BorrowedItemCommandHandler handler, CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(
            await handler.ReturnAsync(new ReturnBorrowedItemCommand(itemId, VersionHeader.Resolve(request.Version, httpContext, VersionMismatch), request.ReturnDate,
                request.Condition!.Value, request.ReturnedQuantity, request.Note), cancellationToken),
            httpContext));

    private static async Task<IResult> IncidentAsync(Guid itemId, BorrowedItemIncidentRequest request, HttpContext httpContext, BorrowedItemCommandHandler handler, CancellationToken cancellationToken)
    {
        var incident = await handler.RecordIncidentAsync(
            new BorrowedItemIncidentCommand(itemId, request.Condition!.Value, request.Quantity, request.Date, request.Description), cancellationToken);
        return Results.Json(ApiResults.Ok(incident, httpContext), statusCode: StatusCodes.Status201Created);
    }
}
