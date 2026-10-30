using BuildingBlocks.Web.Exceptions;
using FluentAssertions;
using NotificationAudit.Application.Audit;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class ActivityLogQueryTests
{
    [Fact]
    public void Date_range_uses_a_non_empty_half_open_interval()
    {
        var point = DateTimeOffset.Parse("2026-10-30T00:00:00Z");
        var action = () => ListActivityLogsHandler.ValidateRange(point, point);
        action.Should().Throw<DomainException>();
    }

    [Fact]
    public void Open_ended_ranges_are_valid()
    {
        var action = () => ListActivityLogsHandler.ValidateRange(DateTimeOffset.UtcNow, null);
        action.Should().NotThrow();
    }
}
