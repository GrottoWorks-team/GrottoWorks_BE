using NotificationAudit.Application.Events;
using NotificationAudit.Infrastructure.Data;
using NotificationAudit.Infrastructure.Deadlines;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class DeadlineReminderTests
{
    [Fact]
    public void Delivery_identity_prevents_duplicates_per_recipient_stage_and_deadline_generation()
    {
        using var db=new NotificationAuditDbContext(new DbContextOptionsBuilder<NotificationAuditDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only").Options);
        var entity=db.Model.FindEntityType(typeof(NotificationDeadlineDelivery))!;
        Assert.Equal(new[] {"TaskId","DeadlineGeneration","RecipientId","Kind"},
            entity.FindPrimaryKey()!.Properties.Select(property=>property.Name));
        Assert.Equal("notification_deadline_delivery",entity.GetTableName());
        Assert.Contains(db.Model.FindEntityType(typeof(NotificationTaskDeadline))!.GetIndexes(),
            index=>index.Properties.Single().Name=="Deadline");
    }
    [Theory]
    [InlineData(-25,null)]
    [InlineData(-24,"DUE_SOON")]
    [InlineData(-1,"DUE_SOON")]
    [InlineData(0,"OVERDUE")]
    [InlineData(2,"OVERDUE")]
    public void Reminder_windows_use_utc_and_do_not_send_both_stages_after_the_deadline(int offsetHours,string? expected)
    {
        var deadline=new DateTimeOffset(2026,12,20,0,0,0,TimeSpan.Zero);
        Assert.Equal(expected,TaskDeadlinePolicy.Due(deadline,"IN_PROGRESS",deadline.AddHours(offsetHours)));
    }
    [Theory]
    [InlineData("COMPLETED")][InlineData("CANCELLED")]
    public void Terminal_tasks_have_no_reminder(string status)
    {Assert.Null(TaskDeadlinePolicy.Due(DateTimeOffset.UtcNow.AddDays(-1),status,DateTimeOffset.UtcNow));}
    [Fact]
    public void Unknown_deadline_is_not_assumed_overdue()
    {Assert.Null(TaskDeadlinePolicy.Due(null,"OPEN",DateTimeOffset.UtcNow));}
}
