using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaskExecution.Contracts.Events;
using TaskExecution.Infrastructure;
using TaskExecution.Infrastructure.Events;

namespace TaskExecution.UnitTests.Infrastructure;

public sealed class TaskEventDeliveryTests
{
    [Fact]
    public void Retries_preserve_the_original_event_and_use_bounded_backoff()
    {
        var now = DateTimeOffset.Parse("2026-11-03T00:00:00Z");
        var message = Message(now);
        var row = TaskEventOutboxMessage.Create(message);
        var originalEnvelope = row.Envelope;

        foreach (var seconds in new[] { 2, 5, 15, 60, 300, 300 })
        {
            row.MarkFailed(now);
            row.NextAttemptAt.Should().Be(now.AddSeconds(seconds));
            row.PublishedAt.Should().BeNull();
        }

        row.Attempts.Should().Be(6);
        row.Id.Should().Be(message.EventId);
        row.Envelope.Should().Be(originalEnvelope);
        row.ToMessage().AggregateId.Should().Be(message.AggregateId);
    }

    [Fact]
    public void Success_marks_published_and_clears_the_previous_error()
    {
        var now = DateTimeOffset.UtcNow;
        var row = TaskEventOutboxMessage.Create(Message(now));
        row.MarkFailed(now);
        row.MarkPublished(now.AddSeconds(2));
        row.PublishedAt.Should().Be(now.AddSeconds(2));
        row.LastError.Should().BeNull();
    }

    [Fact]
    public void Dispatcher_is_not_enabled_implicitly()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:TaskExecutionDatabase"] = "Host=localhost;Database=model_only"
        }).Build();
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        services.Should().NotContain(descriptor => descriptor.ImplementationType == typeof(TaskEventDispatcher));
    }

    private static TaskIntegrationEvent Message(DateTimeOffset now) => new(
        Guid.NewGuid(), TaskEventTypes.StatusChanged, 1, now, Guid.NewGuid().ToString(),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "task", Guid.NewGuid(), 2,
        JsonSerializer.SerializeToElement(new { status = "IN_PROGRESS" }));
}
