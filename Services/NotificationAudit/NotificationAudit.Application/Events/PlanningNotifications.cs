using System.Text.Json;

namespace NotificationAudit.Application.Events;

public sealed partial class IntegrationEventIngestionHandler
{
    private static IReadOnlyList<NotificationCandidate> MapWorkAreaAssignment(IncomingIntegrationEvent message, out bool requiresContext)
    {
        requiresContext = false;
        var payload = message.Payload;
        var workAreaId = GetGuid(payload, "workAreaId");
        var hasLeaderFlag = payload.TryGetProperty("leaderChanged", out var leaderFlag) &&
            leaderFlag.ValueKind is JsonValueKind.True or JsonValueKind.False;
        var hasOfficerFlag = payload.TryGetProperty("materialOfficerChanged", out var officerFlag) &&
            officerFlag.ValueKind is JsonValueKind.True or JsonValueKind.False;

        if (!workAreaId.HasValue || !hasLeaderFlag || !hasOfficerFlag)
        {
            requiresContext = true;
            return [];
        }

        var leader = GetGuid(payload, "leaderUserId");
        var officer = GetGuid(payload, "materialOfficerUserId");
        if ((leaderFlag.GetBoolean() && !leader.HasValue) || (officerFlag.GetBoolean() && !officer.HasValue))
        {
            requiresContext = true;
            return [];
        }

        var recipients = new HashSet<Guid>();
        if (leaderFlag.GetBoolean()) recipients.Add(leader!.Value);
        if (officerFlag.GetBoolean()) recipients.Add(officer!.Value);

        return recipients.Select(recipient => new NotificationCandidate(recipient,
            "Work area responsibility assigned", "You have been assigned responsibility in a work area.",
            "WORK_AREA", workAreaId.Value)).ToArray();
    }
}
