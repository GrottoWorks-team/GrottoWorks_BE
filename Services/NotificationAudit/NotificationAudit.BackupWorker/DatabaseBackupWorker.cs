using NotificationAudit.Domain.SystemJobs;
using NotificationAudit.Infrastructure.SystemJobs;

namespace NotificationAudit.BackupWorker;

public sealed class DatabaseBackupWorker(IServiceScopeFactory scopes, ILogger<DatabaseBackupWorker> logger)
    : DurableSystemJobWorker(scopes, logger, SystemJobKind.Backup);
