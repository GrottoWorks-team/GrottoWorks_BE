using Identity.Api.Common;
using Identity.Api.Requests;
using Identity.Application.Admin;
using Identity.Application.Admin.AssignUserRole;
using Identity.Application.Admin.CreateUser;
using Identity.Application.Admin.ListAssignableRoles;
using Identity.Application.Admin.ListUsers;
using Identity.Application.Admin.LockUser;
using Identity.Application.Admin.UnlockUser;
using Identity.Application.Admin.UpdateUser;
using Identity.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Identity.Api.Endpoints;

/// <summary>F-IDN-08: account administration (ADMIN only).</summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin/users")
            .WithTags("User")
            .RequireAuthorization(policy => policy.RequireRole(RoleCodes.Admin));

        admin.MapGet("", ListUsersAsync)
            .WithName("listUsers")
            .Produces<PagedResponse<AdminUserDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        admin.MapPost("", CreateUserAsync)
            .WithName("createUser")
            .Accepts<CreateUserRequest>("application/json")
            .Produces<ApiResponse<AdminUserDto>>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status409Conflict);

        admin.MapPatch("/{userId:guid}", UpdateUserAsync)
            .WithName("updateUser")
            .Accepts<UpdateUserRequest>("application/json")
            .Produces<ApiResponse<AdminUserDto>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        admin.MapPost("/{userId:guid}/lock", LockUserAsync)
            .WithName("lockUser")
            .Accepts<ReasonCommandRequest>("application/json")
            .Produces<ApiResponse<AdminUserDto>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        admin.MapPost("/{userId:guid}/unlock", UnlockUserAsync)
            .WithName("unlockUser")
            .Accepts<ReasonCommandRequest>("application/json")
            .Produces<ApiResponse<AdminUserDto>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        admin.MapPut("/{userId:guid}/role", AssignUserRoleAsync)
            .WithName("assignUserRole")
            .Accepts<RoleAssignmentRequest>("application/json")
            .Produces<ApiResponse<AdminUserDto>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        app.MapGet("/api/v1/roles", ListAssignableRolesAsync)
            .WithName("listAssignableRoles")
            .WithTags("User")
            .RequireAuthorization(policy => policy.RequireRole(RoleCodes.Admin))
            .Produces<ApiResponse<IReadOnlyList<RoleDto>>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> ListUsersAsync(
        ListUsersQueryHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var criteria = UserListQueryParser.Parse(httpContext.Request.Query, allowParishFilter: true);
        var result = await handler.HandleAsync(criteria, cancellationToken);

        return Results.Ok(ApiResults.Page(result.Items, result.TotalItems, criteria.Page, criteria.Size, httpContext));
    }

    private static async Task<IResult> CreateUserAsync(
        CreateUserRequest request,
        CreateUserCommandHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var user = await handler.HandleAsync(
            new CreateUserCommand(
                request.Email!.Trim(),
                request.DisplayName!.Trim(),
                CleanOptionalText(request.Phone),
                request.Role!.Trim(),
                request.ParishId,
                string.IsNullOrEmpty(request.Password) ? null : request.Password),
            cancellationToken);

        return Results.Json(ApiResults.Ok(user, httpContext), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateUserAsync(
        Guid userId,
        UpdateUserRequest request,
        UpdateUserCommandHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var user = await handler.HandleAsync(
            new UpdateUserCommand(
                userId,
                ResolveVersion(request.Version, httpContext),
                CleanOptionalText(request.Email),
                CleanOptionalText(request.DisplayName),
                CleanOptionalText(request.Phone),
                request.ParishId,
                request.Status),
            cancellationToken);

        return Results.Ok(ApiResults.Ok(user, httpContext));
    }

    private static async Task<IResult> LockUserAsync(
        Guid userId,
        ReasonCommandRequest request,
        LockUserCommandHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var user = await handler.HandleAsync(
            new LockUserCommand(userId, ResolveVersion(request.Version, httpContext), request.Reason!),
            cancellationToken);

        return Results.Ok(ApiResults.Ok(user, httpContext));
    }

    private static async Task<IResult> UnlockUserAsync(
        Guid userId,
        ReasonCommandRequest request,
        UnlockUserCommandHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var user = await handler.HandleAsync(
            new UnlockUserCommand(userId, ResolveVersion(request.Version, httpContext), request.Reason!),
            cancellationToken);

        return Results.Ok(ApiResults.Ok(user, httpContext));
    }

    private static async Task<IResult> AssignUserRoleAsync(
        Guid userId,
        RoleAssignmentRequest request,
        AssignUserRoleCommandHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var user = await handler.HandleAsync(
            new AssignUserRoleCommand(userId, ResolveVersion(request.Version, httpContext), request.Role!.Trim(), request.ParishId),
            cancellationToken);

        return Results.Ok(ApiResults.Ok(user, httpContext));
    }

    private static async Task<IResult> ListAssignableRolesAsync(
        ListAssignableRolesQueryHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var roles = await handler.HandleAsync(cancellationToken);

        return Results.Ok(ApiResults.Ok(roles, httpContext));
    }

    /// <summary>
    /// API Contract 4.1: <c>If-Match</c> carries the version (ETag, quotes optional); the body
    /// <c>version</c> is accepted too. Both present and different → 412.
    /// </summary>
    private static long? ResolveVersion(long? bodyVersion, HttpContext httpContext)
    {
        var ifMatch = httpContext.Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(ifMatch) || ifMatch.Trim() == "*")
        {
            return bodyVersion;
        }

        var raw = ifMatch.Trim();
        if (raw.StartsWith("W/", StringComparison.Ordinal))
        {
            raw = raw[2..];
        }

        if (!long.TryParse(raw.Trim('"'), out var headerVersion))
        {
            throw new DomainException("INVALID_IF_MATCH", "If-Match must contain the numeric resource version.");
        }

        if (bodyVersion is { } body && body != headerVersion)
        {
            throw new DomainException(
                "USER_VERSION_MISMATCH",
                "If-Match and body version disagree. Reload the account and try again.");
        }

        return headerVersion;
    }

    private static string? CleanOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
