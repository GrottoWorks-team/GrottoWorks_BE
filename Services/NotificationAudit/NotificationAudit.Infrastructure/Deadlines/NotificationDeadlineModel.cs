using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NotificationAudit.Infrastructure.Deadlines;

public sealed class NotificationTaskDeadline
{
    public Guid TaskId {get;set;}
    public Guid ParishId {get;set;}
    public Guid SeasonId {get;set;}
    public long SourceVersion {get;set;}
    public DateTimeOffset? Deadline {get;set;}
    public long DeadlineGeneration {get;set;}=1;
    public string Status {get;set;}="OPEN";
    public Guid[] Assignees {get;set;}=[];
}
public sealed class NotificationDeadlineDelivery
{
    public Guid TaskId {get;set;}
    public long DeadlineGeneration {get;set;}
    public Guid RecipientId {get;set;}
    public string Kind {get;set;}="";
    public Guid NotificationId {get;set;}
    public DateTimeOffset CreatedAt {get;set;}
}
public sealed class NotificationSnapshotReceipt
{
    public Guid EventId {get;set;}
    public string SourceKey {get;set;}="";
    public string Fingerprint {get;set;}="";
    public DateTimeOffset ReceivedAt {get;set;}
}
public sealed class NotificationTaskDeadlineConfiguration : IEntityTypeConfiguration<NotificationTaskDeadline>
{
    public void Configure(EntityTypeBuilder<NotificationTaskDeadline> b)
    {
        b.ToTable("notification_task_deadline");b.HasKey(row=>row.TaskId);
        b.Property(row=>row.Status).HasMaxLength(30);b.Property(row=>row.Assignees).HasColumnType("uuid[]");
        b.HasIndex(row=>row.Deadline);
    }
}
public sealed class NotificationDeadlineDeliveryConfiguration : IEntityTypeConfiguration<NotificationDeadlineDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDeadlineDelivery> b)
    {
        b.ToTable("notification_deadline_delivery");b.HasKey(row=>new {row.TaskId,row.DeadlineGeneration,row.RecipientId,row.Kind});
        b.Property(row=>row.Kind).HasMaxLength(20);
    }
}
public sealed class NotificationSnapshotReceiptConfiguration : IEntityTypeConfiguration<NotificationSnapshotReceipt>
{
    public void Configure(EntityTypeBuilder<NotificationSnapshotReceipt> b)
    {b.ToTable("notification_snapshot_receipt");b.HasKey(row=>row.EventId);b.Property(row=>row.SourceKey).HasMaxLength(150);b.Property(row=>row.Fingerprint).HasMaxLength(64);}
}
public static class NotificationDeadlineModel
{
    public static void Configure(ModelBuilder model)
    {
        model.ApplyConfiguration(new NotificationTaskDeadlineConfiguration());
        model.ApplyConfiguration(new NotificationDeadlineDeliveryConfiguration());model.ApplyConfiguration(new NotificationSnapshotReceiptConfiguration());
    }
}
