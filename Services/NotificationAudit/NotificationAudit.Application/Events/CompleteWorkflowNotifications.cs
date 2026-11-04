using System.Text.Json;

namespace NotificationAudit.Application.Events;

public interface INotificationAudienceResolver
{
    Task<IReadOnlyList<Guid>?> ResolveAsync(IncomingIntegrationEvent message,CancellationToken ct);
}

public sealed partial class IntegrationEventIngestionHandler
{
    private static IReadOnlyList<NotificationCandidate> MapResolvedWorkflow(IncomingIntegrationEvent message,out bool requiresContext)
    {
        var payload=message.Payload;var ids=ReadRecipientIds(payload);
        requiresContext=ids is null;
        if(requiresContext)return [];
        var season=message.EventType.StartsWith("planning.season.",StringComparison.Ordinal);
        var support=message.EventType=="support.request.published";
        var type=season ? "SEASON" : support ? "SUPPORT_REQUEST" : "TASK";
        var reference=GetGuid(payload,season ? "seasonId" : support ? "supportRequestId" : "taskId") ?? message.AggregateId;
        var title=message.EventType switch
        {
            "planning.season.activated"=>"Season activated",
            "planning.season.completed"=>"Season completed",
            "planning.season.archived"=>"Season archived",
            "support.request.published"=>"New support request",
            "task.progress.submitted"=>"Task awaiting completion review",
            "task.completion.reviewed"=>GetString(payload,"status")=="COMPLETED" ? "Task completion approved" : "Task revision requested",
            _=>GetString(payload,"status")=="COMPLETED" ? "Task completed" : "Task revision requested"
        };
        return ids!.Select(id=>new NotificationCandidate(id,title,season ? "The season lifecycle has changed. Open the season for details."
            : support ? "A published request matches your skills or material-officer responsibility. Review it before offering support."
            : "A task review update is available. Open the task to view the decision and its history.",type,reference)).ToArray();
    }

    private static IReadOnlyList<Guid>? ReadRecipientIds(JsonElement payload)
    {
        if(!payload.TryGetProperty("recipientUserIds",out var ids))return null;
        if(ids.ValueKind!=JsonValueKind.Array || ids.GetArrayLength()>10000)
            throw new InvalidNotificationEventException("Recipient context must be a bounded array.");
        var result=new List<Guid>();
        foreach(var value in ids.EnumerateArray())
        {
            if(value.ValueKind!=JsonValueKind.String || !value.TryGetGuid(out var id) || id==Guid.Empty)
                throw new InvalidNotificationEventException("Recipient context contains an invalid identity.");
            result.Add(id);
        }
        return result.Distinct().Order().ToArray();
    }
}
