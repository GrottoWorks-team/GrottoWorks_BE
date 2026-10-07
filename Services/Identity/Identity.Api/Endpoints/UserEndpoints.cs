using Identity.Api.Common;
using Identity.Api.Requests;
using Identity.Application.Accounts;
using Identity.Application.Users.GetMyProfile;
using Identity.Application.Users.UpdateMyProfile;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Identity.Api.Endpoints;

/// <summary>F-IDN-05: profile of the authenticated caller.</summary>
public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/users/me", GetMyProfileAsync)
            .WithName("getMyProfile")
            .WithTags("User")
            .RequireAuthorization()
            .Produces<ApiResponse<UserProfileDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPatch("/api/v1/users/me", UpdateMyProfileAsync)
            .WithName("updateMyProfile")
            .WithTags("User")
            .RequireAuthorization()
            .Accepts<UpdateProfileRequest>("application/json")
            .Produces<ApiResponse<UserProfileDto>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> GetMyProfileAsync(
        GetMyProfileQueryHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var profile = await handler.HandleAsync(new GetMyProfileQuery(), cancellationToken);

        return Results.Ok(ApiResults.Ok(profile, httpContext));
    }

    private static async Task<IResult> UpdateMyProfileAsync(
        UpdateProfileRequest request,
        UpdateMyProfileCommandHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var command = new UpdateMyProfileCommand(
            CleanOptionalText(request.DisplayName),
            CleanOptionalText(request.Phone),
            CleanOptionalText(request.Introduction),
            request.CommunityId);

        var profile = await handler.HandleAsync(command, cancellationToken);

        return Results.Ok(ApiResults.Ok(profile, httpContext));
    }

    private static string? CleanOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
