using BuildingBlocks.Web;
using TaskExecution.Api.Common;
using TaskExecution.Application.Tasks;
using TaskExecution.Domain;

namespace TaskExecution.Api.Endpoints;

public static class InternalTaskCountEndpoints
{
    public static void MapInternalTaskCountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/work-area-task-counts", GetAsync)
            .WithName("internalGetWorkAreaTaskCounts")
            .AllowAnonymous()
            .AddEndpointFilter<InternalApiKeyFilter>()
            .ExcludeFromDescription();
    }

    private static async Task<IResult> GetAsync(
        HttpContext httpContext,
        GetWorkAreaTaskCountsQueryHandler handler,
        CancellationToken cancellationToken)
    {
        var workAreaIds = InternalWorkAreaTaskCountQuery.Parse(httpContext.Request.Query);
        var result = await handler.HandleAsync(workAreaIds, cancellationToken);
        return Results.Ok(ApiResults.Ok(result, httpContext));
    }
}

public static class InternalWorkAreaTaskCountQuery
{
    public const int MaximumWorkAreaIds = 100;

    public static IReadOnlyList<Guid> Parse(IQueryCollection query)
    {
        if (query.Keys.Any(key => !key.Equals("workAreaIds", StringComparison.OrdinalIgnoreCase)))
        {
            throw BadRequest("Only the 'workAreaIds' query parameter is supported.");
        }

        var ids = new List<Guid>();
        var distinctIds = new HashSet<Guid>();
        foreach (var rawValue in query["workAreaIds"])
        {
            if (!Guid.TryParse(rawValue, out var workAreaId) || workAreaId == Guid.Empty)
            {
                throw BadRequest("Every workAreaIds value must be a non-empty UUID.");
            }

            if (distinctIds.Add(workAreaId))
            {
                ids.Add(workAreaId);
            }
        }

        if (ids.Count == 0)
        {
            throw BadRequest("At least one workAreaIds value is required.");
        }

        if (ids.Count > MaximumWorkAreaIds)
        {
            throw BadRequest($"At most {MaximumWorkAreaIds} distinct workAreaIds values are allowed.");
        }

        return ids;
    }

    private static DomainRuleException BadRequest(string message) =>
        new("INVALID_WORK_AREA_IDS", message, DomainErrorKind.BadRequest);
}
