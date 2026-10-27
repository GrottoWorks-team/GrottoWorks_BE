using BuildingBlocks.Web;
using Reporting.Api.Common;
using Reporting.Api.Requests;
using Reporting.Application.Ingestion;

namespace Reporting.Api.Endpoints;

/// <summary>
/// Interim event feed (F-RPT-02) until the outbox (F-PLT-05) and the MassTransit consumers exist: producers post the
/// envelope here; service-to-service only (<c>X-Internal-Api-Key</c>), no gateway route, hidden from OpenAPI.
/// </summary>
public static class InternalEventEndpoints
{
    public static void MapInternalEventEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/internal/events", HandleAsync)
            .AllowAnonymous()
            .AddEndpointFilter<InternalApiKeyFilter>()
            .ExcludeFromDescription()
            .WithName("internalPostEvent");
    }

    private static async Task<IResult> HandleAsync(
        IntegrationEventRequest request,
        HttpContext httpContext,
        IntegrationEventHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new IncomingEvent(request.EventId!.Value, request.EventType!.Trim(), request.Version, request.OccurredAt, request.Payload!.Value),
            cancellationToken);
        return Results.Ok(ApiResults.Ok(result, httpContext));
    }
}
