using Identity.Api.Common;
using Identity.Api.Requests;
using Identity.Application.Skills;
using Identity.Application.Skills.CreateSkill;
using Identity.Application.Skills.ListSkills;
using Identity.Application.Skills.UpdateSkill;
using Identity.Domain;
using Identity.Domain.Enums;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Identity.Api.Endpoints;

/// <summary>F-IDN-06: skill catalog — read for any authenticated user, write for ADMIN.</summary>
public static class SkillEndpoints
{
    public static void MapSkillEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/skills", ListSkillsAsync)
            .WithName("listSkills")
            .WithTags("User")
            .RequireAuthorization()
            .Produces<ApiResponse<IReadOnlyList<SkillDto>>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        app.MapPost("/api/v1/skills", CreateSkillAsync)
            .WithName("createSkill")
            .WithTags("User")
            .RequireAuthorization(policy => policy.RequireRole(RoleCodes.Admin))
            .Accepts<CreateSkillRequest>("application/json")
            .Produces<ApiResponse<SkillDto>>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapPatch("/api/v1/skills/{skillId:guid}", UpdateSkillAsync)
            .WithName("updateSkill")
            .WithTags("User")
            .RequireAuthorization(policy => policy.RequireRole(RoleCodes.Admin))
            .Accepts<UpdateSkillRequest>("application/json")
            .Produces<ApiResponse<SkillDto>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListSkillsAsync(
        SkillStatus? status,
        ListSkillsQueryHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var skills = await handler.HandleAsync(new ListSkillsQuery(status), cancellationToken);

        return Results.Ok(ApiResults.Ok(skills, httpContext));
    }

    private static async Task<IResult> CreateSkillAsync(
        CreateSkillRequest request,
        CreateSkillCommandHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var command = new CreateSkillCommand(
            request.Code!.Trim(),
            request.Name!.Trim(),
            CleanOptionalText(request.Description));

        var skill = await handler.HandleAsync(command, cancellationToken);

        return Results.Json(
            ApiResults.Ok(skill, httpContext),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateSkillAsync(
        Guid skillId,
        UpdateSkillRequest request,
        UpdateSkillCommandHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var command = new UpdateSkillCommand(
            skillId,
            CleanOptionalText(request.Name),
            CleanOptionalText(request.Description),
            request.Status);

        var skill = await handler.HandleAsync(command, cancellationToken);

        return Results.Ok(ApiResults.Ok(skill, httpContext));
    }

    private static string? CleanOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
