namespace NotificationAudit.Application.Events;

public sealed partial class IntegrationEventIngestionHandler
{
    private static IReadOnlyList<NotificationCandidate> MapTaskWorkflow(IncomingIntegrationEvent message, out bool requiresContext)
    {
        requiresContext = false;
        var payload = message.Payload;
        var taskId = GetGuid(payload, "taskId");
        if (taskId is null && message.AggregateType.Equals("task", StringComparison.OrdinalIgnoreCase))
            taskId = message.AggregateId;

        var volunteerId = GetGuid(payload, "volunteerUserId");
        var leaderId = GetGuid(payload, "leaderUserId");
        var assignmentType = GetString(payload, "assignmentType");
        var decision = GetString(payload, "decision");
        Guid? recipient = null;
        string? title = null;
        string? body = null;

        switch (message.EventType)
        {
            case "task.assignment.created" when assignmentType == "SUPPORT":
                // The same transaction emits support.member.reviewed, which owns the Volunteer notification.
                return [];
            case "task.assignment.created" when assignmentType == "DIRECT":
                recipient = volunteerId;
                title = "New task assignment";
                body = "You have received a new task assignment.";
                break;
            case "task.assignment.created" when assignmentType == "APPLICATION":
                recipient = leaderId;
                title = "Task application awaiting review";
                body = "A volunteer has applied for a task in your work area.";
                break;
            case "task.assignment.responded" when assignmentType == "APPLICATION" && decision is "ACCEPTED" or "REJECTED":
                recipient = volunteerId;
                title = decision == "ACCEPTED" ? "Task application approved" : "Task application rejected";
                body = decision == "ACCEPTED" ? "Your task application was approved." : "Your task application was rejected.";
                break;
            case "task.assignment.responded" when assignmentType == "DIRECT" && decision == "DECLINED":
                recipient = leaderId;
                title = "Task assignment declined";
                body = "A volunteer has declined a task assignment.";
                break;
            case "task.assignment.responded" when assignmentType == "DIRECT" && decision == "ACCEPTED":
                // Acceptance remains an audit fact; BA only requires a Leader alert on decline.
                return [];
            case "attendance.request.submitted":
                recipient = leaderId;
                title = "Check-in awaiting review";
                body = "A volunteer has requested check-in for a task in your work area.";
                break;
            case "attendance.request.reviewed" when decision == "CANCELLED":
                // The volunteer cancelled their own request; no decision notification is due.
                return [];
            case "attendance.request.reviewed" when decision is "APPROVED" or "REJECTED":
                var reviewType = GetString(payload, "reviewType");
                if (reviewType is not (null or "CHECK_IN" or "CORRECTION")) break;
                // Legacy check-in reviews lack reviewType. Existing correction events carry correctionId.
                var correction = reviewType == "CORRECTION" || GetGuid(payload, "correctionId").HasValue;
                var approved = decision == "APPROVED";
                recipient = volunteerId;
                title = correction
                    ? approved ? "Attendance correction approved" : "Attendance correction rejected"
                    : approved ? "Check-in approved" : "Check-in rejected";
                body = correction
                    ? approved ? "Your attendance correction was approved." : "Your attendance correction was rejected."
                    : approved ? "Your attendance request was approved." : "Your attendance request was rejected.";
                break;
        }

        if (!taskId.HasValue || !recipient.HasValue || title is null || body is null)
        {
            requiresContext = true;
            return [];
        }
        return [new NotificationCandidate(recipient.Value, title, body, "TASK", taskId.Value)];
    }
}
