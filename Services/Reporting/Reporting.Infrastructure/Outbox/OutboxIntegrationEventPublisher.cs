using System.Text.Json;
using BuildingBlocks.Security;
using Microsoft.AspNetCore.Http;
using Reporting.Application.Abstractions;
using Reporting.Contracts.Events;
using Reporting.Infrastructure.Data;

namespace Reporting.Infrastructure.Outbox;

/// <summary>
/// Writes integration events to <c>integration_event_outbox</c> (F-PLT-05 pattern, per service); <see cref="OutboxDispatcher"/>
/// publishes them. The row is saved through the handler's DbContext: inside a command that already runs in a database
/// transaction (stock lock) it commits with the business change, otherwise right after it.
/// </summary>
public sealed class OutboxIntegrationEventPublisher(ReportingDbContext dbContext, ICurrentUser currentUser, IHttpContextAccessor httpContextAccessor)
    : IIntegrationEventPublisher
{
    public async Task PublishAsync<TPayload>(
        string eventType,
        int version,
        TPayload payload,
        string aggregateType,
        Guid aggregateId,
        CancellationToken cancellationToken = default)
        where TPayload : class
    {
        var body = JsonSerializer.SerializeToElement(payload, OutboxMessage.JsonOptions);
        var http = httpContextAccessor.HttpContext;
        var correlationId = http?.Request.Headers["X-Correlation-Id"].FirstOrDefault() ?? http?.TraceIdentifier ?? Guid.NewGuid().ToString();
        var message = new ReportingIntegrationEvent(
            Guid.NewGuid(),
            eventType,
            version,
            DateTimeOffset.UtcNow,
            correlationId,
            currentUser.IsAuthenticated ? currentUser.UserId : null,
            ReadGuid(body, "parishId"),
            ReadGuid(body, "seasonId"),
            aggregateType,
            aggregateId,
            body);
        dbContext.Add(OutboxMessage.Create(message));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static Guid? ReadGuid(JsonElement body, string property) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(property, out var value) && value.TryGetGuid(out var id) ? id : null;
}
