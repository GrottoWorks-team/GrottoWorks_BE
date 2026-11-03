using System.Text.Json;
using FluentAssertions;
using Identity.Contracts.Events;
using Identity.Domain;
using Identity.Infrastructure.Events;
using Identity.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Identity.UnitTests;

public sealed class IdentityEventDeliveryTests
{
    [Fact]
    public async Task Publisher_stages_the_event_in_the_account_context_without_saving_or_connecting()
    {
        using var db = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=localhost;Database=not_contacted").Options);
        var store = new FakeUserStore();
        var user = TestUsers.Add(store, TestUsers.SeedRoles(store)[RoleCodes.Volunteer], "staged@example.test", Guid.NewGuid());
        db.Users.Add(user);
        var correlationId = Guid.NewGuid().ToString();
        var http = new DefaultHttpContext();
        http.Request.Headers["X-Correlation-Id"] = correlationId;
        var publisher = new IdentityEventPublisher(db, new FakeCurrentUser(Guid.NewGuid()),
            new HttpContextAccessor { HttpContext = http });
        await publisher.PublishAsync("identity.user.registered", 1,
            new { userId = user.UserId }, "user", user.UserId);
        var row = db.EventOutbox.Local.Single();
        row.ToMessage().CorrelationId.Should().Be(correlationId);
        db.Entry(row).State.Should().Be(EntityState.Added);
        db.Entry(user).State.Should().Be(EntityState.Added);
    }

    [Fact]
    public void Status_version_increases_across_lock_and_unlock()
    {
        var store = new FakeUserStore();
        var user = TestUsers.Add(store, TestUsers.SeedRoles(store)[RoleCodes.Volunteer], "event@example.test", Guid.NewGuid());
        var version = user.StatusVersion;
        user.Lock(DateTimeOffset.UtcNow);
        user.StatusVersion.Should().Be(version + 1);
        user.Unlock(DateTimeOffset.UtcNow);
        user.StatusVersion.Should().Be(version + 2);
    }

    [Fact]
    public void Delivery_retry_preserves_event_identity_and_payload()
    {
        var now = DateTimeOffset.UtcNow;
        var message = new IdentityIntegrationEvent(Guid.NewGuid(), "identity.user.locked", 1, now,
            Guid.NewGuid().ToString(), Guid.NewGuid(), Guid.NewGuid(), null, "user", Guid.NewGuid(), 2,
            JsonSerializer.SerializeToElement(new { status = "LOCKED" }));
        var row = IdentityEventOutboxMessage.Create(message);
        row.MarkFailed(now);
        row.PublishedAt.Should().BeNull();
        row.NextAttemptAt.Should().Be(now.AddSeconds(2));
        row.ToMessage().EventId.Should().Be(message.EventId);
        row.ToMessage().AggregateVersion.Should().Be(2);
        row.MarkPublished(now.AddSeconds(3));
        row.LastError.Should().BeNull();
    }
}
