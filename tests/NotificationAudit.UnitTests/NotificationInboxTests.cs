using FluentAssertions;
using NotificationAudit.Domain.Notifications;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class NotificationInboxTests
{
    [Fact]
    public void Mark_read_is_idempotent_and_keeps_the_first_timestamp()
    {
        var createdAt = DateTimeOffset.Parse("2026-10-30T00:00:00Z");
        var notification = Notification.Create(Guid.NewGuid(), "task.assigned", "Assigned", "Body", "TASK", Guid.NewGuid(), createdAt);
        var firstRead = createdAt.AddMinutes(1);

        notification.MarkRead(firstRead).Should().BeTrue();
        notification.MarkRead(firstRead.AddHours(1)).Should().BeFalse();
        notification.ReadAt.Should().Be(firstRead);
    }

    [Fact]
    public void New_notification_starts_unread()
    {
        var notification = Notification.Create(Guid.NewGuid(), "event", "Title", "Body", null, null, DateTimeOffset.UtcNow);
        notification.IsRead.Should().BeFalse();
        notification.ReadAt.Should().BeNull();
    }
}
