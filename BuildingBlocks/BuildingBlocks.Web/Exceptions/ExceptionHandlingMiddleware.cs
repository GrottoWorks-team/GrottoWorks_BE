using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Web.Exceptions;

/// <summary>
/// Maps exceptions to RFC 9457 ProblemDetails with a stable <c>code</c> (API Contract 4.3):
/// 400 = malformed body/parameters, registered domain codes → their status (401/403/404/409/422...),
/// 500 = anything unexpected. Every problem carries <c>code</c>, <c>correlationId</c> and <c>traceId</c>.
/// </summary>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    GrottoWorksWebOptions options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (DomainException exception) when (!context.Response.HasStarted)
        {
            var registered = options.ErrorCodes.GetValueOrDefault(exception.Code);
            var status = exception.StatusCode
                ?? registered?.StatusCode
                ?? options.DefaultDomainExceptionStatusCode;
            var title = registered?.Title ?? DefaultTitle(status);

            if (status >= 500)
            {
                logger.LogError(exception, "Domain error {Code}", exception.Code);
            }
            else
            {
                logger.LogWarning(
                    "Request failed with {Code} for {Path}",
                    exception.Code,
                    context.Request.Path);
            }

            await WriteProblemAsync(context, status, exception.Code, title, exception.Message);
        }
        catch (BadHttpRequestException exception) when (!context.Response.HasStarted)
        {
            // API Contract 4.x: 400 = malformed request body / invalid parameter syntax.
            logger.LogWarning(
                exception,
                "Malformed request for {Path}: {Message}",
                context.Request.Path,
                exception.Message);

            await WriteProblemAsync(
                context,
                StatusCodes.Status400BadRequest,
                "INVALID_REQUEST_BODY",
                "Malformed request or invalid parameter syntax.",
                "The request body or a request parameter could not be read.");
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            logger.LogError(exception, "Unhandled exception for {Path}", context.Request.Path);

            await WriteProblemAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "INTERNAL_ERROR",
                "An unexpected error occurred.",
                "An unexpected error occurred.");
        }
    }

    private static string DefaultTitle(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Bad request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not found",
        StatusCodes.Status405MethodNotAllowed => "Method not allowed",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status412PreconditionFailed => "Precondition failed",
        StatusCodes.Status422UnprocessableEntity => "Unprocessable entity",
        _ => "Error"
    };

    private static async Task WriteProblemAsync(
        HttpContext context,
        int statusCode,
        string code,
        string title,
        string detail)
    {
        var problem = new ProblemDetails
        {
            Type = $"https://api.grottoworks.example/problems/{code.ToLowerInvariant().Replace('_', '-')}",
            Title = title,
            Status = statusCode,
            Detail = detail,
            Instance = context.Request.Path
        };

        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = CorrelationIdMiddlewareExtensions.GetCorrelationId(context);
        problem.Extensions["traceId"] = System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier;

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: context.RequestAborted);
    }
}
