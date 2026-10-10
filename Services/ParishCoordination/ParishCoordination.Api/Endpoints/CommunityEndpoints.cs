using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ParishCoordination.Api.Requests;
using ParishCoordination.Application.Communities;
using ParishCoordination.Application.Communities.Commands.CreateCommunity;
using ParishCoordination.Application.Communities.Queries.GetCommunities;

namespace ParishCoordination.Api.Endpoints;

public static class CommunityEndpoints
{
    public static void MapCommunityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/parishes/{parishId}/communities", GetCommunitiesAsync)
            .WithName("listCommunities")
            .WithTags("Parish")
            .RequireAuthorization()
            .Produces<ApiResponse<IReadOnlyList<CommunityDto>>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/api/v1/parishes/{parishId}/communities", CreateCommunityAsync)
            .WithName("createCommunity")
            .WithTags("Parish")
            .RequireAuthorization(policy => policy.RequireRole(
                GrottoWorksRoles.Admin,
                GrottoWorksRoles.Parish))
            .Produces<ApiResponse<CommunityDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> GetCommunitiesAsync(
        Guid parishId,
        HttpContext httpContext,
        GetCommunitiesQueryHandler queryHandler,
        CancellationToken cancellationToken)
    {
        var query = new GetCommunitiesQuery(parishId);
        var communities = await queryHandler.HandleAsync(query, cancellationToken);

        if (communities is null)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status404NotFound,
                "Parish not found",
                $"Parish with ID '{parishId}' was not found.",
                "PARISH_NOT_FOUND");
        }

        return Results.Ok(ApiResults.Ok(communities, httpContext));
    }

    private static async Task<IResult> CreateCommunityAsync(
        Guid parishId,
        CreateCommunityRequest request,
        HttpContext httpContext,
        CreateCommunityCommandHandler commandHandler,
        CancellationToken cancellationToken)
    {
        var command = new CreateCommunityCommand(
            parishId,
            request.Name!.Trim(),
            CleanOptionalText(request.Description));

        var result = await commandHandler.HandleAsync(command, cancellationToken);

        if (result.Outcome == CreateCommunityOutcome.ParishNotFound)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status404NotFound,
                "Parish not found",
                $"Parish with ID '{parishId}' was not found.",
                "PARISH_NOT_FOUND");
        }

        if (result.Outcome == CreateCommunityOutcome.NameConflict)
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status409Conflict,
                "Community name conflict",
                "A community with the same name already exists in this parish.",
                "COMMUNITY_NAME_CONFLICT");
        }

        return Results.Json(
            ApiResults.Ok(result.Community!, httpContext),
            statusCode: StatusCodes.Status201Created);
    }

    private static string? CleanOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static IResult CreateProblem(
        HttpContext httpContext,
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
                ["code"] = code,
                ["correlationId"] = CorrelationIdMiddlewareExtensions.GetCorrelationId(httpContext),
                ["traceId"] = httpContext.TraceIdentifier
            });
    }
}
