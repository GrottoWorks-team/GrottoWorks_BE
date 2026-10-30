using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TaskExecution.Contracts.Events;
using TaskExecution.Infrastructure.Data;
using TaskExecution.Infrastructure.Events;

namespace TaskExecution.UnitTests.Infrastructure;

public sealed class TaskEventOutboxTests
{
    [Fact]
    public void Event_envelope_retains_identity_and_stays_pending_without_publisher()
    {
        var message = new TaskIntegrationEvent(Guid.NewGuid(), TaskEventTypes.StatusChanged, 1,
            DateTimeOffset.UtcNow, "test-correlation", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "task", Guid.NewGuid(), 2, JsonSerializer.SerializeToElement(new { status = "CANCELLED" }));
        var row = TaskEventOutboxMessage.Create(message);
        row.Id.Should().Be(message.EventId);
        row.PublishedAt.Should().BeNull();
        using var json = JsonDocument.Parse(row.Envelope);
        json.RootElement.GetProperty("aggregateVersion").GetInt32().Should().Be(2);
        json.RootElement.GetProperty("correlationId").GetString().Should().Be("test-correlation");
    }

    [Fact]
    public void Outbox_uses_same_context_and_jsonb_envelope()
    {
        using var db = new TaskExecutionDbContext(new DbContextOptionsBuilder<TaskExecutionDbContext>()
            .UseNpgsql("Host=localhost;Database=not_used;Username=test;Password=test").Options);
        var entity = db.Model.FindEntityType(typeof(TaskEventOutboxMessage))!;
        entity.GetTableName().Should().Be("task_event_outbox");
        entity.FindProperty(nameof(TaskEventOutboxMessage.Envelope))!.GetColumnType().Should().Be("jsonb");
    }
}
