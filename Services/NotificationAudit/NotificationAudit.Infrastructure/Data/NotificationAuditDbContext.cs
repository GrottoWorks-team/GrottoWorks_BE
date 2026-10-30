using Microsoft.EntityFrameworkCore;
using NotificationAudit.Domain.Files;
using NotificationAudit.Domain.Notifications;
using NotificationAudit.Domain.Audit;

namespace NotificationAudit.Infrastructure.Data;

public sealed class NotificationAuditDbContext(DbContextOptions<NotificationAuditDbContext> options)
    : DbContext(options)
{
    public DbSet<FileObject> FileObjects => Set<FileObject>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<NotificationRule> NotificationRules => Set<NotificationRule>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationAuditDbContext).Assembly);
}
