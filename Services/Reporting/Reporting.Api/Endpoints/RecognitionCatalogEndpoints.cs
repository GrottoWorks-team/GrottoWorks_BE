using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Reporting.Api.Common;
using Reporting.Api.Requests;
using Reporting.Application.Recognition;
using Reporting.Domain.Recognition;

namespace Reporting.Api.Endpoints;

/// <summary>F-RPT-01: contribution types and recognition rules (tag Reports).</summary>
public static class RecognitionCatalogEndpoints
{
    private const string VersionMismatch = "RECOGNITION_RULE_VERSION_MISMATCH";

    public static void MapRecognitionCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var types = app.MapGroup("/api/v1/contribution-types").WithTags("Reports");
        types.MapGet("", ListTypesAsync)
            .WithName("listContributionTypes")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Admin, GrottoWorksRoles.Parish))
            .Produces<ApiResponse<IReadOnlyList<ContributionTypeDto>>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);
        types.MapPost("", CreateTypeAsync)
            .WithName("createContributionType")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Admin))
            .Accepts<CreateContributionTypeRequest>("application/json")
            .Produces<ApiResponse<ContributionTypeDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
        types.MapPatch("/{typeId:guid}", UpdateTypeAsync)
            .WithName("updateContributionType")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Admin))
            .Accepts<UpdateContributionTypeRequest>("application/json")
            .Produces<ApiResponse<ContributionTypeDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        var rules = app.MapGroup("/api/v1/recognition-rules").WithTags("Reports");
        rules.MapGet("", ListRulesAsync)
            .WithName("listRecognitionRules")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Admin, GrottoWorksRoles.Parish))
            .Produces<ApiResponse<IReadOnlyList<RecognitionRuleDto>>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);
        rules.MapPost("", CreateRuleAsync)
            .WithName("createRecognitionRule")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Admin))
            .Accepts<CreateRecognitionRuleRequest>("application/json")
            .Produces<ApiResponse<RecognitionRuleDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
        rules.MapPost("/{ruleId:guid}/deactivate", DeactivateRuleAsync)
            .WithName("deactivateRecognitionRule")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Admin))
            .Accepts<ReasonCommandRequest>("application/json")
            .Produces<ApiResponse<RecognitionRuleDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> ListTypesAsync(HttpContext httpContext, ContributionTypeHandler handler, CancellationToken cancellationToken)
    {
        var status = new QueryReader(httpContext.Request.Query, "status").Enum<ContributionTypeStatus>("status");
        return Results.Ok(ApiResults.Ok(await handler.ListAsync(status, cancellationToken), httpContext));
    }

    private static async Task<IResult> CreateTypeAsync(
        CreateContributionTypeRequest request,
        HttpContext httpContext,
        ContributionTypeHandler handler,
        CancellationToken cancellationToken)
    {
        var type = await handler.CreateAsync(new CreateContributionTypeCommand(request.Code, request.Name, request.Description), cancellationToken);
        return Results.Json(ApiResults.Ok(type, httpContext), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateTypeAsync(
        Guid typeId,
        UpdateContributionTypeRequest request,
        HttpContext httpContext,
        ContributionTypeHandler handler,
        CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(
            await handler.UpdateAsync(new UpdateContributionTypeCommand(typeId, request.Name, request.Description, request.Status), cancellationToken),
            httpContext));

    private static async Task<IResult> ListRulesAsync(HttpContext httpContext, RecognitionRuleHandler handler, CancellationToken cancellationToken)
    {
        var reader = new QueryReader(httpContext.Request.Query, "status", "contributionTypeId");
        var rules = await handler.ListAsync(reader.Enum<RecognitionRuleStatus>("status"), reader.Guid("contributionTypeId"), cancellationToken);
        return Results.Ok(ApiResults.Ok(rules, httpContext));
    }

    private static async Task<IResult> CreateRuleAsync(
        CreateRecognitionRuleRequest request,
        HttpContext httpContext,
        RecognitionRuleHandler handler,
        CancellationToken cancellationToken)
    {
        var rule = await handler.CreateAsync(
            new CreateRecognitionRuleCommand(request.ContributionTypeId, request.Name, request.Points!.Value, request.Description,
                request.EffectiveFrom!.Value, request.EffectiveTo, request.PreviousRuleId),
            cancellationToken);
        return Results.Json(ApiResults.Ok(rule, httpContext), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> DeactivateRuleAsync(
        Guid ruleId,
        ReasonCommandRequest request,
        HttpContext httpContext,
        RecognitionRuleHandler handler,
        CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(
            await handler.DeactivateAsync(
                new DeactivateRecognitionRuleCommand(ruleId, VersionHeader.Resolve(request.Version, httpContext, VersionMismatch), request.Reason),
                cancellationToken),
            httpContext));
}
