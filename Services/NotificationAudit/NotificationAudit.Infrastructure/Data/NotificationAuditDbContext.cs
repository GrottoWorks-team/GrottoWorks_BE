using Microsoft.EntityFrameworkCore;

namespace NotificationAudit.Infrastructure.Data;

public sealed class NotificationAuditDbContext(DbContextOptions<NotificationAuditDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationAuditDbContext).Assembly);
}
