using BuildingBlocks.Web.Exceptions;
using FluentAssertions;
using NotificationAudit.Application.Audit;
using NotificationAudit.Application.Notifications;
using NotificationAudit.Domain.Audit;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class ContractAlignmentTests
{
    [Fact]
    public void Rule_catalog_keeps_ids_when_correcting_event_names()
    {
        NotificationRuleCatalog.All[Guid.Parse("71000000-0000-0000-0000-000000000011")].Should().Be("purchase.request.decided");
        NotificationRuleCatalog.All[Guid.Parse("71000000-0000-0000-0000-000000000012")].Should().Be("readiness.area.ready");
        NotificationRuleCatalog.All.Values.Should().OnlyHaveUniqueItems();
        NotificationRuleCatalog.All.Values.Should().NotContain("resource.purchase-request.decided");
        NotificationRuleCatalog.All.Values.Should().Contain("recognition.entry.created");
    }

    [Fact]
    public async Task Resource_history_forwards_normalized_half_open_range()
    {
        var store = new Logs();
        var from = new DateTimeOffset(2026, 11, 3, 7, 0, 0, TimeSpan.FromHours(7));
        await new GetResourceHistoryHandler(store).HandleAsync(" task ", Guid.NewGuid(), from, from.AddDays(1), default);
        store.ResourceType.Should().Be("task");
        store.From.Should().Be(from.ToUniversalTime());
        store.From!.Value.Offset.Should().Be(TimeSpan.Zero);
        store.To.Should().Be(from.AddDays(1).ToUniversalTime());
    }

    [Fact]
    public async Task Invalid_history_range_is_rejected_before_reading_data()
    {
        var store = new Logs();
        var now = DateTimeOffset.UtcNow;
        Func<Task> query = () => new GetResourceHistoryHandler(store).HandleAsync("task", Guid.NewGuid(), now, now, default);
        (await query.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("INVALID_DATE_RANGE");
        store.ResourceType.Should().BeNull();
    }

    private sealed class Logs : IActivityLogRepository
    {
        public string? ResourceType { get; private set; }
        public DateTimeOffset? From { get; private set; }
        public DateTimeOffset? To { get; private set; }
        public Task<IReadOnlyList<ActivityLog>> GetHistoryAsync(string type, Guid id, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
        { ResourceType = type; From = from; To = to; return Task.FromResult<IReadOnlyList<ActivityLog>>([]); }
        public Task<ActivityLog?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(IReadOnlyList<ActivityLog> Items, long Total)> GetPageAsync(ActivityLogFilter filter, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
