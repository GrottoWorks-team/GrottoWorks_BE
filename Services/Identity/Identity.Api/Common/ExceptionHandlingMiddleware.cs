using Identity.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Identity.Api.Common;

/// <summary>
/// Maps exceptions to RFC 9457 ProblemDetails with a stable <c>code</c> (API Contract 4.3).
/// 422 = invalid field values, 409 = business rule conflict, 401 = authentication failures.
/// </summary>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    private static readonly IReadOnlyDictionary<string, (int Status, string Title)> KnownErrors =
        new Dictionary<string, (int, string)>(StringComparer.Ordinal)
        {
            // 401 — authentication
            ["AUTH_INVALID_CREDENTIALS"] = (401, "Invalid credentials"),
            ["AUTH_ACCOUNT_LOCKED"] = (401, "Account locked"),
            ["AUTH_ACCOUNT_INACTIVE"] = (401, "Account inactive"),
            ["AUTH_INVALID_REFRESH_TOKEN"] = (401, "Invalid refresh token"),
            ["AUTH_REFRESH_TOKEN_EXPIRED"] = (401, "Refresh token expired"),
            ["AUTH_REFRESH_TOKEN_REUSED"] = (401, "Refresh token reused"),
            // 404 — not found
            ["USER_NOT_FOUND"] = (404, "User not found"),
            ["SKILL_NOT_FOUND"] = (404, "Skill not found"),
            // 409 — conflicts
            ["AUTH_EMAIL_ALREADY_EXISTS"] = (409, "Email already registered"),
            ["SKILL_CODE_ALREADY_EXISTS"] = (409, "Skill code already exists"),
            // 422 — field validation
            ["AUTH_EMAIL_INVALID"] = (422, "Invalid email"),
            ["AUTH_PASSWORD_POLICY_VIOLATION"] = (422, "Password does not meet the policy"),
            ["AUTH_PASSWORD_HASH_REQUIRED"] = (422, "Password required"),
            ["USER_FULL_NAME_INVALID"] = (422, "Invalid full name"),
            ["USER_PHONE_INVALID"] = (422, "Invalid phone number"),
            ["USER_PARISH_REQUIRED"] = (422, "Parish required"),
            ["PROFILE_COMMUNITY_REQUIRED"] = (422, "Community required"),
            ["PROFILE_COMMUNITY_NOT_IN_PARISH"] = (422, "Community outside your parish"),
            ["PROFILE_USER_REQUIRED"] = (422, "Invalid profile"),
            ["PROFILE_TEXT_TOO_LONG"] = (422, "Text too long"),
            ["SKILL_CODE_INVALID"] = (422, "Invalid skill code"),
            ["SKILL_NAME_INVALID"] = (422, "Invalid skill name"),
            ["ROLE_CODE_REQUIRED"] = (422, "Role code required"),
            ["ROLE_NAME_REQUIRED"] = (422, "Role name required"),
            ["AVAILABILITY_INVALID_RANGE"] = (422, "Invalid availability range"),
            ["AVAILABILITY_USER_REQUIRED"] = (422, "Invalid availability slot"),
            ["VOLUNTEER_SKILL_INVALID"] = (422, "Invalid volunteer skill"),
            ["REFRESH_TOKEN_USER_REQUIRED"] = (422, "Invalid refresh token"),
            ["REFRESH_TOKEN_HASH_REQUIRED"] = (422, "Invalid refresh token"),
            ["REFRESH_TOKEN_INVALID_TTL"] = (422, "Invalid refresh token lifetime")
        };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (DomainException exception) when (!context.Response.HasStarted)
        {
            var (status, title) = KnownErrors.TryGetValue(exception.Code, out var mapped)
                ? mapped
                : (StatusCodes.Status409Conflict, "Business rule violation");

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
        catch (Microsoft.AspNetCore.Http.BadHttpRequestException exception) when (!context.Response.HasStarted)
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
