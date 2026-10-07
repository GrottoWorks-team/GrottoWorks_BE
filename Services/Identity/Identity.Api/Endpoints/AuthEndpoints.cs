using Identity.Api.Common;
using Identity.Api.Requests;
using Identity.Application.Accounts;
using Identity.Application.Auth;
using Identity.Application.Auth.Login;
using Identity.Application.Auth.Logout;
using Identity.Application.Auth.TokenRefresh;
using Identity.Application.Auth.RegisterUser;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Identity.Api.Endpoints;

/// <summary>F-IDN-02/F-IDN-03: registration, login, token rotation and logout.</summary>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/auth/register", RegisterAsync)
            .WithName("registerUser")
            .WithTags("User")
            .AllowAnonymous()
            .Produces<ApiResponse<UserDto>>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapPost("/api/v1/auth/login", LoginAsync)
            .WithName("login")
            .WithTags("User")
            .AllowAnonymous()
            .Produces<ApiResponse<TokenPairDto>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        app.MapPost("/api/v1/auth/refresh", RefreshAsync)
            .WithName("refreshToken")
            .WithTags("User")
            .AllowAnonymous()
            .Produces<ApiResponse<TokenPairDto>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        app.MapPost("/api/v1/auth/logout", LogoutAsync)
            .WithName("logout")
            .WithTags("User")
            .RequireAuthorization()
            .Accepts<LogoutRequest>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        RegisterUserCommandHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var command = new RegisterUserCommand(
            request.DisplayName!.Trim(),
            request.Email!.Trim(),
            request.Password!,
            CleanOptionalText(request.Phone),
            request.ParishId!.Value);

        var user = await handler.HandleAsync(command, cancellationToken);

        return Results.Json(
            ApiResults.Ok(user, httpContext),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        LoginCommandHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var command = new LoginCommand(request.Email!.Trim(), request.Password!);
        var tokenPair = await handler.HandleAsync(command, cancellationToken);

        return Results.Ok(ApiResults.Ok(tokenPair, httpContext));
    }

    private static async Task<IResult> RefreshAsync(
        RefreshTokenRequest request,
        RefreshTokenCommandHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var command = new RefreshTokenCommand(request.RefreshToken!);
        var tokenPair = await handler.HandleAsync(command, cancellationToken);

        return Results.Ok(ApiResults.Ok(tokenPair, httpContext));
    }

    private static async Task<IResult> LogoutAsync(
        LogoutRequest request,
        LogoutCommandHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(new LogoutCommand(request.RefreshToken!), cancellationToken);

        return Results.NoContent();
    }

    private static string? CleanOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
