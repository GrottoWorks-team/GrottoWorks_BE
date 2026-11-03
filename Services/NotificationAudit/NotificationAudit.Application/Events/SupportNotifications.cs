using System.Text.Json;

namespace NotificationAudit.Application.Events;

public sealed partial class IntegrationEventIngestionHandler
{
    private static IReadOnlyList<NotificationCandidate> MapSupportDecision(IncomingIntegrationEvent message, out bool requiresContext)
    {
        var requestId=GetGuid(message.Payload,"supportRequestId");
        var volunteerId=GetGuid(message.Payload,"volunteerUserId");
        var decision=GetString(message.Payload,"decision");
        requiresContext=!requestId.HasValue || !volunteerId.HasValue || decision is not ("APPROVED" or "REJECTED");
        if(requiresContext) return [];
        var approved=decision=="APPROVED";
        if(approved && message.Payload.TryGetProperty("requiresConfirmation",out var confirmation))
        {
            if(confirmation.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidNotificationEventException("Support confirmation flag must be a boolean.");
            if(confirmation.GetBoolean())
                return [new NotificationCandidate(volunteerId!.Value,"Support nomination awaiting your response",
                    "A community nominated you for a support task. Please accept or decline the assignment.","SUPPORT_REQUEST",requestId)];
        }
        return [new NotificationCandidate(volunteerId!.Value,approved ? "Support participation approved" : "Support participation rejected",
            approved ? "Your offer to participate in a support task was approved." : "Your offer to participate in a support task was rejected.",
            "SUPPORT_REQUEST",requestId)];
    }

    private static void AddSafeSupportRequirements(JsonElement payload,Dictionary<string,JsonElement> safeValues)
    {
        var type=GetString(payload,"supportType");
        if(type is not ("STAFFING" or "MATERIAL" or "MIXED") ||
            !payload.TryGetProperty("positions",out var positionJson) || positionJson.ValueKind!=JsonValueKind.Array ||
            !payload.TryGetProperty("materials",out var materialJson) || materialJson.ValueKind!=JsonValueKind.Array)
            throw new InvalidNotificationEventException("Published Support requirements have an invalid schema.");
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        IReadOnlyList<MatchingPosition>? positions;
        IReadOnlyList<MatchingMaterial>? materials;
        try
        {
            positions=positionJson.Deserialize<IReadOnlyList<MatchingPosition>>(options);
            materials=materialJson.Deserialize<IReadOnlyList<MatchingMaterial>>(options);
        }
        catch(JsonException) {throw new InvalidNotificationEventException("Published Support requirements have invalid field types.");}
        if(positions is null || materials is null || positions.Any(position=>position is null || position.PositionId==Guid.Empty ||
            position.TaskId==Guid.Empty || position.RequiredVolunteerCount<=0 || position.Skills is null ||
            position.Skills.Any(skill=>skill is null || skill.SkillId==Guid.Empty ||
                skill.RequiredSkillLevel is not (null or "BASIC" or "INTERMEDIATE" or "ADVANCED" or "EXPERT"))) ||
            materials.Any(material=>material is null || material.MaterialId==Guid.Empty || material.RequiredQuantity<=0 ||
                material.RequiredQuantity>9999999999.99m || decimal.Round(material.RequiredQuantity,2)!=material.RequiredQuantity))
            throw new InvalidNotificationEventException("Published Support requirements have invalid identities, levels or quantities.");
        if(((type is "STAFFING" or "MIXED") && positions.Count==0) || ((type is "MATERIAL" or "MIXED") && materials.Count==0))
            throw new InvalidNotificationEventException("Published Support requirements do not match the declared type.");
        // Typed projection intentionally discards names, notes and arbitrary extra payload fields.
        safeValues["supportType"]=JsonSerializer.SerializeToElement(type);
        safeValues["positions"]=JsonSerializer.SerializeToElement(positions,options);
        safeValues["materials"]=JsonSerializer.SerializeToElement(materials,options);
    }

    private sealed record MatchingSkill(Guid SkillId,string? RequiredSkillLevel);
    private sealed record MatchingPosition(Guid PositionId,Guid TaskId,int RequiredVolunteerCount,IReadOnlyList<MatchingSkill> Skills);
    private sealed record MatchingMaterial(Guid MaterialId,decimal RequiredQuantity);
}
