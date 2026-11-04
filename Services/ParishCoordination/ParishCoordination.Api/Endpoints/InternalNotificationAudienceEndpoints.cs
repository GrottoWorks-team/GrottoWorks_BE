using BuildingBlocks.Web;
using ParishCoordination.Api.Common;
using ParishCoordination.Application.WorkAreas;

namespace ParishCoordination.Api.Endpoints;

public static class InternalNotificationAudienceEndpoints
{
    public static void MapInternalNotificationAudienceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/notification-audience", async (Guid parishId, Guid seasonId,
            INotificationAudienceQuery query, HttpContext context, CancellationToken ct) =>
        {
            if (parishId == Guid.Empty || seasonId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Non-empty parishId and seasonId are required.");
            var audience = await query.GetAsync(parishId, seasonId, ct);
            return audience is null
                ? Results.NotFound()
                : Results.Ok(ApiResults.Ok(audience, context));
        }).WithName("internalGetNotificationAudience")
            .AllowAnonymous().AddEndpointFilter<InternalApiKeyFilter>().ExcludeFromDescription();
    }
}
