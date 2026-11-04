using System.Text.Json;

namespace NotificationAudit.Application.Events;

public sealed partial class IntegrationEventIngestionHandler
{
    private static IReadOnlyList<NotificationCandidate> MapUpstreamWorkflow(IncomingIntegrationEvent message, out bool requiresContext)
    {
        requiresContext = false;
        var payload = message.Payload;
        Guid? recipient = null;
        Guid? reference = null;
        string? referenceType = null;
        string? title = null;
        string? body = null;

        switch (message.EventType)
        {
            case "purchase.request.submitted":
                recipient = GetGuid(payload, "leaderUserId");
                reference = GetGuid(payload, "purchaseRequestId");
                referenceType = "PURCHASE_REQUEST";
                title = "Purchase request awaiting review";
                body = "A purchase request in your work area is awaiting your decision.";
                break;
            case "purchase.request.decided":
                var decision = GetString(payload, "decision");
                if (decision is not ("APPROVED" or "REJECTED")) break;
                recipient = GetGuid(payload, "requestedByUserId");
                reference = GetGuid(payload, "purchaseRequestId");
                referenceType = "PURCHASE_REQUEST";
                title = decision == "APPROVED" ? "Purchase request approved" : "Purchase request rejected";
                body = decision == "APPROVED" ? "Your purchase request was approved." : "Your purchase request was rejected.";
                break;
            case "readiness.item.failed":
                if (!payload.TryGetProperty("mandatory", out var mandatory) ||
                    mandatory.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) break;
                if (!mandatory.GetBoolean()) return [];
                recipient = GetGuid(payload, "leaderUserId");
                reference = GetGuid(payload, "workAreaId");
                referenceType = "WORK_AREA";
                title = "Mandatory readiness item failed";
                body = "A mandatory readiness item failed in your work area.";
                break;
            case "recognition.entry.created":
                recipient = GetGuid(payload, "awardedToUserId");
                reference = GetGuid(payload, "recognitionPointId");
                referenceType = "RECOGNITION_POINT";
                title = "Contribution recognized";
                body = "A recognition entry has been recorded for your contribution.";
                break;
        }

        if (!recipient.HasValue || !reference.HasValue || referenceType is null || title is null || body is null)
        {
            requiresContext = true;
            return [];
        }
        return [new NotificationCandidate(recipient.Value, title, body, referenceType, reference.Value)];
    }
}
