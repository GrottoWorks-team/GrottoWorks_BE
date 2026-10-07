using Identity.Application.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Identity.Api.Endpoints;

/// <summary>
/// Liveness/readiness probes (skill §35). Interim implementation — replaced by
/// <c>BuildingBlocks.Web</c> health checks (F-PLT-03) when they land.
/// </summary>
public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }))
            .WithName("livenessProbe")
            .WithTags("Health")
            .AllowAnonymous();

        app.MapGet("/health/ready", ReadinessAsync)
            .WithName("readinessProbe")
            .WithTags("Health")
            .AllowAnonymous();
    }

    private static async Task<IResult> ReadinessAsync(
        IReadinessProbe readinessProbe,
        CancellationToken cancellationToken)
    {
        var ready = await readinessProbe.IsReadyAsync(cancellationToken);

        return ready
            ? Results.Ok(new { status = "Healthy" })
            : Results.Json(
                new { status = "Unhealthy" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
