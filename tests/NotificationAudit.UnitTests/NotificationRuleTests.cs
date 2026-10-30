using FluentAssertions;
using NotificationAudit.Domain.Notifications;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class NotificationRuleTests
{
    [Fact]
    public void Toggling_a_rule_increments_version()
    {
        var rule = NotificationRule.Create(Guid.NewGuid(), "task.assignment.created", DateTimeOffset.UtcNow);
        rule.Update(false, DateTimeOffset.UtcNow.AddMinutes(1)).Should().BeTrue();
        rule.Version.Should().Be(2);
        rule.InAppEnabled.Should().BeFalse();
    }

    [Fact]
    public void Setting_the_current_value_is_a_no_op()
    {
        var rule = NotificationRule.Create(Guid.NewGuid(), "event", DateTimeOffset.UtcNow);
        rule.Update(true, DateTimeOffset.UtcNow.AddMinutes(1)).Should().BeFalse();
        rule.Version.Should().Be(1);
    }
}
