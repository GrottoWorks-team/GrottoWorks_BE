using System.Net.Http.Json;
using System.Text.Json;

namespace ParishCoordination.Infrastructure.TaskScopeSync;

public sealed class HttpTaskScopeDelivery(HttpClient client, TaskScopeSyncOptions options) : ITaskScopeDelivery
{
    public async Task DeliverAsync(TaskScopeOutbox message, CancellationToken cancellationToken)
    {
        if (message.ScopeType == "season")
        {
            var snapshot = JsonSerializer.Deserialize<SeasonSnapshot>(message.Payload, ScopeJson.Options)
                ?? throw new InvalidOperationException("Invalid season scope snapshot.");
            await PutAsync($"internal/season-scopes/{message.AggregateId:D}", snapshot, message.Id, cancellationToken);
            return;
        }

        if (message.ScopeType != "work-area")
        {
            throw new InvalidOperationException("Unsupported task scope type.");
        }

        var delivery = JsonSerializer.Deserialize<WorkAreaDelivery>(message.Payload, ScopeJson.Options)
            ?? throw new InvalidOperationException("Invalid work area scope snapshot.");
        await PutAsync($"internal/season-scopes/{delivery.WorkArea.SeasonId:D}",
            delivery.Season, message.Id, cancellationToken);
        await PutAsync($"internal/work-area-scopes/{message.AggregateId:D}",
            delivery.WorkArea, message.Id, cancellationToken);
    }

    private async Task PutAsync<T>(string path, T payload, Guid correlationId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
        request.Headers.Add("X-Internal-Api-Key", options.ApiKey);
        request.Headers.Add("X-Correlation-Id", correlationId.ToString());
        request.Content = JsonContent.Create(payload, options: ScopeJson.Options);
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Do not persist response bodies, credentials or transport exception messages.
            throw new HttpRequestException($"Scope delivery returned HTTP {(int)response.StatusCode}.",
                null, response.StatusCode);
        }
    }
}
