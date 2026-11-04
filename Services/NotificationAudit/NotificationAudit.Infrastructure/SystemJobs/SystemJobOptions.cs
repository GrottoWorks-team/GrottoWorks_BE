namespace NotificationAudit.Infrastructure.SystemJobs;

public sealed class SystemJobOptions
{
    public int MaximumAttempts { get; set; } = 3;
    public int RetentionDays { get; set; } = 7;
    public long MaximumSourceBytes { get; set; } = 2L * 1024 * 1024 * 1024;
    public int SourceTimeoutMinutes { get; set; } = 15;
    public Dictionary<string, SystemExportSourceOptions> Sources { get; set; } = new(StringComparer.Ordinal);
}

public sealed class SystemExportSourceOptions
{
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
}
