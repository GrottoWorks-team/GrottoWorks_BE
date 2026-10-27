using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Resource.Api.Common;
using Resource.Api.Requests;
using Resource.Application.Contributions;
using Resource.Domain.Contributions;

namespace Resource.Api.Endpoints;

/// <summary>F-RES-13: direct donations and the volunteer's own contributions (tag Contributions).</summary>
public static class DonationEndpoints
{
    public static void MapDonationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/donations").WithTags("Contributions");

        group.MapGet("", ListAsync)
            .WithName("listDonations")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Parish, GrottoWorksRoles.Leader, GrottoWorksRoles.MaterialOfficer))
            .Produces<PagedResponse<DonationDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("", RecordAsync)
            .WithName("recordDirectDonation")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer))
            .Accepts<CreateDonationRequest>("application/json")
            .Produces<ApiResponse<DonationDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{donationId:guid}", GetAsync)
            .WithName("getDonation")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.MaterialOfficer, GrottoWorksRoles.Volunteer))
            .Produces<ApiResponse<DonationDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/api/v1/users/me/contributions", ListMineAsync)
            .WithName("listMyContributions")
            .WithTags("Contributions")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Volunteer))
            .Produces<PagedResponse<DonationDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    /// <summary><c>DonationFilter</c>: seasonId, workAreaId, donorId, type, status; newest first; paged.</summary>
    private static async Task<IResult> ListAsync(HttpContext httpContext, ListDonationsQueryHandler handler, CancellationToken cancellationToken)
    {
        var reader = new QueryReader(httpContext.Request.Query, "page", "size", "seasonId", "workAreaId", "donorId", "type", "status");
        var (page, size) = reader.Paging();
        var result = await handler.HandleAsync(
            new DonationFilter(page, size, reader.Guid("seasonId"), reader.Guid("workAreaId"), reader.Guid("donorId"),
                reader.Enum<DonationType>("type"), reader.Enum<DonationStatus>("status")),
            cancellationToken);
        return Results.Ok(ApiResults.Page(result.Items, result.TotalItems, page, size, httpContext));
    }

    private static async Task<IResult> RecordAsync(
        CreateDonationRequest request,
        HttpContext httpContext,
        RecordDirectDonationCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var donation = await handler.HandleAsync(
            new RecordDirectDonationCommand(
                request.DonorId!.Value,
                request.Type!.Value,
                request.SeasonId,
                request.WorkAreaId,
                request.Amount,
                (request.Items ?? [])
                    .Select(item => new ReceivedMaterialInput(item.MaterialId!.Value, item.ReceivedQuantity!.Value, item.UnusableQuantity ?? 0, item.Note))
                    .ToList(),
                request.ReceivedAt,
                request.Note),
            cancellationToken);
        return Results.Json(ApiResults.Ok(donation, httpContext), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> GetAsync(
        Guid donationId,
        HttpContext httpContext,
        GetDonationQueryHandler handler,
        CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(await handler.HandleAsync(donationId, cancellationToken), httpContext));

    /// <summary><c>ContributionFilter</c>: seasonId; newest first; paged.</summary>
    private static async Task<IResult> ListMineAsync(HttpContext httpContext, ListMyContributionsQueryHandler handler, CancellationToken cancellationToken)
    {
        var reader = new QueryReader(httpContext.Request.Query, "page", "size", "seasonId");
        var (page, size) = reader.Paging();
        var result = await handler.HandleAsync(page, size, reader.Guid("seasonId"), cancellationToken);
        return Results.Ok(ApiResults.Page(result.Items, result.TotalItems, page, size, httpContext));
    }
}
