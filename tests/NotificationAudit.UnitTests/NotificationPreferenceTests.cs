using FluentAssertions;
using NotificationAudit.Domain.Notifications;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class NotificationPreferenceTests
{
    [Fact]
    public void Defaults_match_the_contract()
    {
        var preference = NotificationPreference.CreateDefault(Guid.NewGuid(), DateTimeOffset.UtcNow);
        preference.InAppEnabled.Should().BeTrue();
        preference.PushEnabled.Should().BeTrue();
        preference.EmailEnabled.Should().BeFalse();
    }

    [Fact]
    public void Update_replaces_all_channels()
    {
        var preference = NotificationPreference.CreateDefault(Guid.NewGuid(), DateTimeOffset.UtcNow);
        preference.Update(false, false, true, DateTimeOffset.UtcNow.AddMinutes(1));
        preference.InAppEnabled.Should().BeFalse();
        preference.PushEnabled.Should().BeFalse();
        preference.EmailEnabled.Should().BeTrue();
    }
}
