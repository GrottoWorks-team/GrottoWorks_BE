using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using NotificationAudit.Infrastructure.Files;
using NotificationAudit.Infrastructure.Notifications;
using NotificationAudit.Infrastructure.Audit;

#nullable disable

namespace NotificationAudit.Infrastructure.Data.Migrations;

[DbContext(typeof(NotificationAuditDbContext))]
public partial class NotificationAuditDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.4")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);
        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);
        new FileObjectConfiguration().Configure(modelBuilder.Entity<NotificationAudit.Domain.Files.FileObject>());
        new NotificationConfiguration().Configure(modelBuilder.Entity<NotificationAudit.Domain.Notifications.Notification>());
        new NotificationPreferenceConfiguration().Configure(modelBuilder.Entity<NotificationAudit.Domain.Notifications.NotificationPreference>());
        new NotificationRuleConfiguration().Configure(modelBuilder.Entity<NotificationAudit.Domain.Notifications.NotificationRule>());
        new ActivityLogConfiguration().Configure(modelBuilder.Entity<NotificationAudit.Domain.Audit.ActivityLog>());
    }
}
