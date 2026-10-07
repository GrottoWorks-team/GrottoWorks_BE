using BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ParishCoordination.Api.Requests;
using ParishCoordination.Application.Parishes;
using ParishCoordination.Application.Parishes.CreateParish;
using ParishCoordination.Application.Parishes.GetParishes;

namespace ParishCoordination.Api.Endpoints;

public static class ParishEndpoints
{
    public static void MapParishEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/parishes", GetAllParishesAsync)
            .WithName("listParishes")
            .WithTags("Parish")
            .Produces<ApiResponse<IReadOnlyList<ParishDto>>>(StatusCodes.Status200OK);

        app.MapPost("/api/v1/parishes", CreateParishAsync)
            .WithName("createParish")
            .WithTags("Parish")
            .Produces<ApiResponse<ParishDto>>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> GetAllParishesAsync(
        GetParishesQueryHandler queryHandler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var query = new GetParishesQuery();
        var parishes = await queryHandler.HandleAsync(query, cancellationToken);

        // Success envelope per BuildingBlocks README §3: { data, meta: { correlationId } }.
        return Results.Ok(ApiResults.Ok(parishes, httpContext));
    }

    private static async Task<IResult> CreateParishAsync(
        CreateParishRequest request,
        CreateParishCommandHandler commandHandler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var command = new CreateParishCommand(
            request.Name!.Trim(),
            CleanOptionalText(request.Address),
            CleanOptionalText(request.Description));

        var parish = await commandHandler.HandleAsync(command, cancellationToken);

        return Results.Json(
            ApiResults.Ok(parish, httpContext),
            statusCode: StatusCodes.Status201Created);
    }

    private static string? CleanOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
