using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using ParishCoordination.Api.Requests;
using ParishCoordination.Api.Responses;
using ParishCoordination.Application.Parishes.Commands.CreateParish;
using ParishCoordination.Application.Parishes.Commands.UpdateParish;
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

        app.MapPatch("/api/v1/parishes/{parishId}", UpdateParishAsync)
            .WithName("updateParish")
            .WithTags("Parish")
            .Produces<ParishResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

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
        HttpContext httpContext,
        GetParishQueryHandler queryHandler,
        CancellationToken cancellationToken)
    {
        var query = new GetParishQuery(parishId);
        var parish = await queryHandler.HandleAsync(query, cancellationToken);

        if (parish is null)
        {
            return CreateProblem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Parish not found",
                detail: $"Parish with ID '{parishId}' was not found.",
                code: "PARISH_NOT_FOUND");
        }

        SetETag(httpContext, parish.Version);
        var response = new ParishResponse(parish);

        return Results.Ok(response);
    }

    private static async Task<IResult> UpdateParishAsync(
        Guid parishId,
        UpdateParishRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        HttpContext httpContext,
        UpdateParishCommandHandler commandHandler,
        CancellationToken cancellationToken)
    {
        if (!TryParseExpectedVersion(ifMatch, out var expectedVersion))
        {
            return CreateProblem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid If-Match header",
                detail: "If-Match must contain a positive integer version.",
                code: "INVALID_IF_MATCH");
        }

        var command = new UpdateParishCommand(
            parishId,
            request.Name?.Trim(),
            request.Address is not null,
            CleanOptionalText(request.Address),
            request.Description is not null,
            CleanOptionalText(request.Description),
            request.Status,
            expectedVersion);

        var result = await commandHandler.HandleAsync(command, cancellationToken);

        if (result.Outcome == UpdateParishOutcome.NotFound)
        {
            return CreateProblem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Parish not found",
                detail: $"Parish with ID '{parishId}' was not found.",
                code: "PARISH_NOT_FOUND");
        }

        if (result.Outcome == UpdateParishOutcome.VersionMismatch)
        {
            return CreateProblem(
                statusCode: StatusCodes.Status412PreconditionFailed,
                title: "Parish version mismatch",
                detail: "The parish was changed by another request. Load it again and retry.",
                code: "PARISH_VERSION_MISMATCH");
        }

        var parish = result.Parish!;
        SetETag(httpContext, parish.Version);

        return Results.Ok(new ParishResponse(parish));
    }

    private static async Task<IResult> CreateParishAsync(
        CreateParishRequest request,
        HttpContext httpContext,
        CreateParishCommandHandler commandHandler,
        CancellationToken cancellationToken)
    {
        var command = new CreateParishCommand(
            request.Name!.Trim(),
            CleanOptionalText(request.Address),
            CleanOptionalText(request.Description));

        var parish = await commandHandler.HandleAsync(command, cancellationToken);
        SetETag(httpContext, parish.Version);
        var response = new ParishCreatedResponse(parish);

        return Results.Json(response, statusCode: StatusCodes.Status201Created);
    }

    private static string? CleanOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool TryParseExpectedVersion(
        string? ifMatch,
        out int? expectedVersion)
    {
        expectedVersion = null;

        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            return true;
        }

        var value = ifMatch.Trim();
        var startsWithQuote = value.StartsWith('"');
        var endsWithQuote = value.EndsWith('"');

        if (startsWithQuote || endsWithQuote)
        {
            if (!startsWithQuote || !endsWithQuote || value.Length <= 2)
            {
                return false;
            }

            value = value[1..^1];
        }

        if (!int.TryParse(value, out var version) || version <= 0)
        {
            return false;
        }

        expectedVersion = version;
        return true;
    }

    private static void SetETag(HttpContext httpContext, int version)
    {
        httpContext.Response.Headers.ETag = $"\"{version}\"";
    }

    private static IResult CreateProblem(
        int statusCode,
        string title,
        string detail,
        string code)
    {
        return Results.Problem(
            statusCode: statusCode,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code
            });
    }
}
