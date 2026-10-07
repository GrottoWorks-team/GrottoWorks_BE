using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BuildingBlocks.Web;

/// <summary>Liveness/readiness probes (F-PLT-03): <c>/health/live</c> and <c>/health/ready</c>.</summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapGrottoWorksHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }))
            .WithName("livenessProbe")
            .WithTags("Health")
            .AllowAnonymous();

        app.MapGet("/health/ready", ReadinessAsync)
            .WithName("readinessProbe")
            .WithTags("Health")
            .AllowAnonymous();

        return app;
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
