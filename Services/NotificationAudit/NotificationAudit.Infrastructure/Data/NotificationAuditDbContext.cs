using Microsoft.EntityFrameworkCore;
using NotificationAudit.Domain.Files;
using NotificationAudit.Domain.Notifications;

namespace NotificationAudit.Infrastructure.Data;

public sealed class NotificationAuditDbContext(DbContextOptions<NotificationAuditDbContext> options)
    : DbContext(options)
{
    public DbSet<FileObject> FileObjects => Set<FileObject>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationAuditDbContext).Assembly);
}
