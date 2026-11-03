using System.Text.Json;
using Identity.Application.Abstractions;
using Identity.Contracts.Events;
using Identity.Infrastructure.Data;
using Microsoft.AspNetCore.Http;

namespace Identity.Infrastructure.Events;

public sealed class IdentityEventPublisher(IdentityDbContext db, ICurrentUser caller, IHttpContextAccessor http)
    : IIntegrationEventPublisher
{
    public Task PublishAsync<TPayload>(string eventType, int version, TPayload payload,
        string aggregateType, Guid aggregateId, CancellationToken cancellationToken = default) where TPayload : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = db.Users.Local.SingleOrDefault(user => user.UserId == aggregateId)
            ?? throw new InvalidOperationException("Identity events must be staged with the tracked account before SaveChanges.");
        var correlation = http.HttpContext?.Request.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? Guid.NewGuid().ToString();
        var message = new IdentityIntegrationEvent(Guid.NewGuid(), eventType, version, DateTimeOffset.UtcNow,
            correlation, caller.IsAuthenticated ? caller.UserId : null, user.ParishId, null,
            aggregateType, aggregateId, user.StatusVersion,
            JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        db.EventOutbox.Add(IdentityEventOutboxMessage.Create(message));
        return Task.CompletedTask;
    }
}
