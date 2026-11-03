using BuildingBlocks.Web.Exceptions;
using FluentAssertions;
using NotificationAudit.Application.Notifications;
using NotificationAudit.Domain.Notifications;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class NotificationReadHandlerTests
{
    [Fact]
    public async Task Read_uses_the_current_users_identity_and_unknown_or_foreign_id_is_not_found()
    {
        var user = new FileFinalizationTests.User();
        var store = new Notifications { Exists = true };
        var id = Guid.NewGuid();
        var handler = new MarkNotificationReadHandler(user, store, TimeProvider.System);
        await handler.HandleAsync(id, default);
        store.UserId.Should().Be(user.UserId);
        store.Id.Should().Be(id);
        store.Exists = false;
        Func<Task> read = () => handler.HandleAsync(id, default);
        (await read.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("NOTIFICATION_NOT_FOUND");
    }
    private sealed class Notifications : INotificationRepository
    {
        public bool Exists { get; set; }
        public Guid UserId { get; private set; }
        public Guid Id { get; private set; }
        public Task<bool> MarkReadAsync(Guid id, Guid userId, DateTimeOffset readAt, CancellationToken cancellationToken)
        { Id = id; UserId = userId; return Task.FromResult(Exists); }
        public Task MarkAllReadAsync(Guid id, DateTimeOffset cutoff, DateTimeOffset readAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(IReadOnlyList<Notification> Items, long Total)> GetPageAsync(Guid id, bool? read, string? type, int page, int size, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
