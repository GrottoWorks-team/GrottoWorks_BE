using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ParishCoordination.Api.Requests;
using ParishCoordination.Api.Responses;
using ParishCoordination.Application.Parishes.Commands.CreateParish;
using ParishCoordination.Application.Parishes.Queries.GetParish;
using ParishCoordination.Application.Parishes.Queries.GetParishes;

namespace ParishCoordination.Api.Endpoints;

public static class ParishEndpoints
{
    public static void MapParishEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/parishes", GetAllParishesAsync)
            .WithName("listParishes")
            .WithTags("Parish")
            .Produces<ParishListResponse>(StatusCodes.Status200OK);

        app.MapGet("/api/v1/parishes/{parishId}", GetParishAsync)
            .WithName("getParish")
            .WithTags("Parish")
            .Produces<ParishResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/api/v1/parishes", CreateParishAsync)
            .WithName("createParish")
            .WithTags("Parish")
            .Produces<ParishCreatedResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> GetAllParishesAsync(
        GetParishesQueryHandler queryHandler,
        CancellationToken cancellationToken)
    {
        var query = new GetParishesQuery();
        var parishes = await queryHandler.HandleAsync(query, cancellationToken);
        var response = new ParishListResponse(parishes);

        return Results.Ok(response);
    }

    private static async Task<IResult> GetParishAsync(
        Guid parishId,
        GetParishQueryHandler queryHandler,
        CancellationToken cancellationToken)
    {
        var query = new GetParishQuery(parishId);
        var parish = await queryHandler.HandleAsync(query, cancellationToken);

        if (parish is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Parish not found",
                detail: $"Parish with ID '{parishId}' was not found.");
        }

        var response = new ParishResponse(parish);

        return Results.Ok(response);
    }

    private static async Task<IResult> CreateParishAsync(
        CreateParishRequest request,
        CreateParishCommandHandler commandHandler,
        CancellationToken cancellationToken)
    {
        var command = new CreateParishCommand(
            request.Name!.Trim(),
            CleanOptionalText(request.Address),
            CleanOptionalText(request.Description));

        var parish = await commandHandler.HandleAsync(command, cancellationToken);
        var response = new ParishCreatedResponse(parish);

        return Results.Json(response, statusCode: StatusCodes.Status201Created);
    }

    private static string? CleanOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
