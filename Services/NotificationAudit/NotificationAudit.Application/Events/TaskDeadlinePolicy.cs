namespace NotificationAudit.Application.Events;

public static class TaskDeadlinePolicy
{
    public static string? Due(DateTimeOffset? deadline,string status,DateTimeOffset now)
    {
        if(deadline is null || status is "COMPLETED" or "CANCELLED")return null;
        if(now>=deadline)return "OVERDUE";
        return now>=deadline.Value.AddHours(-24) ? "DUE_SOON" : null;
    }
}
