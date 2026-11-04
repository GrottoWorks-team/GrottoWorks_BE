using System.Text.Json;
using TaskExecution.Application.Events;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Users;
using TaskExecution.Contracts.Events;
using TaskExecution.Domain;
using TaskExecution.Domain.Support;

namespace TaskExecution.Application.Support;

public sealed class CloseSupportRequestHandler(ISupportRequestRepository requests, TaskScopeAccess scopes, ActiveUserAccess users,
    ITaskEventOutbox outbox, ITaskCorrelationContext correlation, TimeProvider clock)
{
    public Task<SupportRequestDto> HandleAsync(Guid requestId, SupportRequestStatus status, string? reason, int? version, CancellationToken ct = default) =>
        requests.InRequestTransactionAsync(requestId, async () =>
        {
            var request = await requests.GetAsync(requestId, true, ct)
                ?? throw DomainRuleException.NotFound("SUPPORT_REQUEST_NOT_FOUND", "The support request does not exist.");
            var (area, _) = await scopes.GetWorkAreaForLeaderWriteAsync(request.WorkAreaId, ct);
            var actor = await users.EnsureCallerAsync(area.ParishId, ct);
            var now = clock.GetUtcNow(); var previousStatus = JsonNamingPolicy.SnakeCaseUpper.ConvertName(request.Status.ToString());
            if (request.Close(status, reason, actor.UserId, version, now))
            {
                outbox.Add(new TaskIntegrationEvent(Guid.NewGuid(), "support.request.closed", 1, now, correlation.CorrelationId, actor.UserId,
                    area.ParishId, request.SeasonId, "support_request", request.Id, request.Version, JsonSerializer.SerializeToElement(new
                    {
                        supportRequestId = request.Id, workAreaId = request.WorkAreaId, previousStatus,
                        status = status.ToString().ToUpperInvariant(), reason = request.CloseReason
                    })));
                await requests.SaveAsync(ct);
            }
            return SupportRequestDto.From(request);
        }, ct);
}
