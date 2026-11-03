namespace NotificationAudit.BackupWorker;

public sealed class DatabaseBackupOptions
{
    public string PgDumpPath { get; set; } = "pg_dump";
    public int DumpTimeoutMinutes { get; set; } = 30;
    public string EncryptionKeyEnvironmentVariable { get; set; } = "GROTTOWORKS_BACKUP_KEY";
    public string EncryptionKeyId { get; set; } = "";
    // Bind these values from worker-only environment/secrets. The business API must never receive this section.
    public Dictionary<string, string> Databases { get; set; } = new(StringComparer.Ordinal);
}
