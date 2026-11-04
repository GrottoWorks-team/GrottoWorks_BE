using Microsoft.EntityFrameworkCore;
using NotificationAudit.Application.SystemJobs;
using NotificationAudit.Infrastructure.Data;
using NotificationAudit.Infrastructure.Files;
using NotificationAudit.Infrastructure.SystemJobs;
using NotificationAudit.BackupWorker;

var builder = Host.CreateApplicationBuilder(args);
// Deliberately do not call AddInfrastructure: no API, broker consumers, migrations or bucket initializer here.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<NotificationAuditDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("NotificationAuditDatabase")
        ?? throw new InvalidOperationException("The job database connection is required.")));
builder.Services.Configure<ObjectStorageOptions>(builder.Configuration.GetSection("ObjectStorage"));
builder.Services.AddSystemJobExecution(builder.Configuration);
builder.Services.Configure<DatabaseBackupOptions>(builder.Configuration.GetSection("DatabaseBackup"));
builder.Services.AddScoped<ISystemArtifactBuilder, DatabaseBackupArtifactBuilder>();
builder.Services.AddHostedService<DatabaseBackupWorker>();
await builder.Build().RunAsync();
