using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NotificationAudit.Infrastructure.Data;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class ScaffoldTests
{
    [Fact]
    public void DbContext_is_dedicated_to_notification_audit() =>
        typeof(NotificationAuditDbContext).BaseType.Should().Be(typeof(DbContext));
}
