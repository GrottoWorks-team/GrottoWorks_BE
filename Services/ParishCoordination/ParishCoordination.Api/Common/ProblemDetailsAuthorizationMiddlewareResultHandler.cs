using BuildingBlocks.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ParishCoordination.Api.Common;

internal sealed class ProblemDetailsAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler defaultHandler = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Succeeded)
        {
            await defaultHandler.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        var challenged = authorizeResult.Challenged;
        var statusCode = challenged ? StatusCodes.Status401Unauthorized : StatusCodes.Status403Forbidden;
        var code = challenged ? "UNAUTHORIZED" : "FORBIDDEN";
        var title = challenged ? "Unauthorized" : "Forbidden";
        var detail = challenged
            ? "Authentication is required to access this resource."
            : "You do not have permission to access this resource.";

        context.Response.StatusCode = statusCode;
        if (challenged)
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";
        }
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Type = $"https://api.grottoworks.example/problems/{code.ToLowerInvariant()}",
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };
        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] =
            CorrelationIdMiddlewareExtensions.GetCorrelationId(context);
        problem.Extensions["traceId"] = context.TraceIdentifier;

        await context.Response.WriteAsJsonAsync(problem, cancellationToken: context.RequestAborted);
    }
}