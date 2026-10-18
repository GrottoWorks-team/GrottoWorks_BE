using Identity.Api.Common;
using Identity.Api.Requests;
using Identity.Application.Accounts;
using Identity.Application.Users.GetInternalUsers;
using Identity.Application.Users.GetMyProfile;
using Identity.Application.Users.ListParishUsers;
using Identity.Application.Users.ReplaceMyAvailability;
using Identity.Application.Users.ReplaceMySkills;
using Identity.Application.Users.UpdateMyProfile;
using Identity.Domain;
using Identity.Domain.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Identity.Api.Endpoints;

/// <summary>
/// F-IDN-05 profile of the caller, F-IDN-07 volunteer skills/availability, F-IDN-10 parish users,
/// F-IDN-09 internal lookup.
/// </summary>
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

        app.MapPut("/api/v1/users/me/skills", ReplaceMySkillsAsync)
            .WithName("replaceMySkills")
            .WithTags("User")
            .RequireAuthorization(policy => policy.RequireRole(RoleCodes.Volunteer))
            .Accepts<ReplaceSkillsRequest>("application/json")
            .Produces<ApiResponse<UserProfileDto>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapPut("/api/v1/users/me/availability", ReplaceMyAvailabilityAsync)
            .WithName("replaceMyAvailability")
            .WithTags("User")
            .RequireAuthorization(policy => policy.RequireRole(RoleCodes.Volunteer))
            .Accepts<List<AvailabilitySlotRequest>>("application/json")
            .Produces<ApiResponse<UserProfileDto>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapGet("/api/v1/users", ListParishUsersAsync)
            .WithName("listParishUsers")
            .WithTags("User")
            .RequireAuthorization(policy => policy.RequireRole(
                RoleCodes.Parish,
                RoleCodes.Leader,
                RoleCodes.MaterialOfficer))
            .Produces<PagedResponse<UserSummaryDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // Service-to-service only (no gateway route, shared key) — kept out of the public OpenAPI document.
        app.MapGet("/internal/users", GetInternalUsersAsync)
            .WithName("internalGetUsers")
            .AllowAnonymous()
            .AddEndpointFilter<InternalApiKeyFilter>()
            .ExcludeFromDescription();
    }

    private static async Task<IResult> ReplaceMySkillsAsync(
        ReplaceSkillsRequest request,
        ReplaceMySkillsCommandHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var profile = await handler.HandleAsync(
            new ReplaceMySkillsCommand(request.SkillIds!),
            cancellationToken);

        return Results.Ok(ApiResults.Ok(profile, httpContext));
    }

    private static async Task<IResult> ReplaceMyAvailabilityAsync(
        List<AvailabilitySlotRequest> request,
        ReplaceMyAvailabilityCommandHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (request.Count > AppUser.MaxAvailabilitySlots)
        {
            throw new DomainException(
                "AVAILABILITY_LIMIT",
                $"At most {AppUser.MaxAvailabilitySlots} availability slots can be declared.");
        }

        // Top-level arrays are not validated by AddValidation; check required members here.
        var slots = new List<AvailabilitySlot>(request.Count);
        foreach (var slot in request)
        {
            if (slot?.AvailableFrom is not { } from || slot.AvailableTo is not { } to)
            {
                throw new DomainException(
                    "AVAILABILITY_INVALID_RANGE",
                    "Every slot needs availableFrom and availableTo.");
            }

            slots.Add(new AvailabilitySlot(from, to, CleanOptionalText(slot.Note)));
        }

        var profile = await handler.HandleAsync(new ReplaceMyAvailabilityCommand(slots), cancellationToken);

        return Results.Ok(ApiResults.Ok(profile, httpContext));
    }

    private static async Task<IResult> ListParishUsersAsync(
        ListParishUsersQueryHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var criteria = UserListQueryParser.Parse(httpContext.Request.Query, allowParishFilter: false);
        var result = await handler.HandleAsync(criteria, cancellationToken);

        return Results.Ok(ApiResults.Page(result.Items, result.TotalItems, criteria.Page, criteria.Size, httpContext));
    }

    /// <summary><c>ids</c> as a comma-separated list and/or repeated parameter: <c>?ids=a,b&amp;ids=c</c>.</summary>
    private static async Task<IResult> GetInternalUsersAsync(
        GetInternalUsersQueryHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var ids = new List<Guid>();
        foreach (var raw in httpContext.Request.Query["ids"]
                     .SelectMany(value => (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            if (!Guid.TryParse(raw, out var id))
            {
                throw new DomainException("INVALID_FILTER_VALUE", $"'{raw}' is not a valid user id.");
            }

            ids.Add(id);
        }

        var users = await handler.HandleAsync(new GetInternalUsersQuery(ids), cancellationToken);

        return Results.Ok(ApiResults.Ok(users, httpContext));
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
