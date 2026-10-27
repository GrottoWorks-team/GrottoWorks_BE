using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Resource.Api.Common;
using Resource.Api.Requests;
using Resource.Application.Contributions;
using Resource.Domain;

namespace Resource.Api.Endpoints;

/// <summary>F-RES-12: donors (tag Contributions).</summary>
public static class DonorEndpoints
{
    private static readonly string[] Roles = [GrottoWorksRoles.MaterialOfficer, GrottoWorksRoles.Admin];

    public static void MapDonorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/donors").WithTags("Contributions").RequireAuthorization(policy => policy.RequireRole(Roles));

        group.MapGet("", ListAsync)
            .WithName("listDonors")
            .Produces<PagedResponse<DonorDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("", CreateAsync)
            .WithName("createDonor")
            .Accepts<CreateDonorRequest>("application/json")
            .Produces<ApiResponse<DonorDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPatch("/{donorId:guid}", UpdateAsync)
            .WithName("updateDonor")
            .Accepts<UpdateDonorRequest>("application/json")
            .Produces<ApiResponse<DonorDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
    }

    /// <summary><c>DonorFilter</c>: q (name, phone or email), linked, parishId (ADMIN); sort <c>name|createdAt</c>; paged.</summary>
    private static async Task<IResult> ListAsync(HttpContext httpContext, ListDonorsQueryHandler handler, CancellationToken cancellationToken)
    {
        var reader = new QueryReader(httpContext.Request.Query, "page", "size", "sort", "q", "linked", "parishId");
        var (page, size) = reader.Paging();
        var (field, descending) = reader.Sort("name", "name", "createdAt");
        var linked = reader.String("linked") switch
        {
            null => (bool?)null,
            var value when bool.TryParse(value, out var parsed) => parsed,
            _ => throw new DomainRuleException("INVALID_FILTER_VALUE", "'linked' must be true or false.", DomainErrorKind.BadRequest)
        };

        var result = await handler.HandleAsync(
            new DonorFilter(page, size, reader.Guid("parishId"), reader.String("q"), linked,
                field == "createdAt" ? DonorSortField.CreatedAt : DonorSortField.Name, descending),
            cancellationToken);
        return Results.Ok(ApiResults.Page(result.Items, result.TotalItems, page, size, httpContext));
    }

    private static async Task<IResult> CreateAsync(
        CreateDonorRequest request,
        HttpContext httpContext,
        CreateDonorCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var donor = await handler.HandleAsync(
            new CreateDonorCommand(request.ParishId, request.UserId, request.DonorName, request.Phone, request.Email, request.Address, request.Note),
            cancellationToken);
        return Results.Json(ApiResults.Ok(donor, httpContext), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateAsync(
        Guid donorId,
        UpdateDonorRequest request,
        HttpContext httpContext,
        UpdateDonorCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var donor = await handler.HandleAsync(
            new UpdateDonorCommand(
                donorId,
                VersionHeader.Resolve(request.Version, httpContext, "DONOR_VERSION_MISMATCH"),
                request.DonorName,
                request.Phone,
                request.Email,
                request.Address,
                request.Note),
            cancellationToken);
        return Results.Ok(ApiResults.Ok(donor, httpContext));
    }
}
