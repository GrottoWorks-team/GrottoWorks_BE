using System.Text.Json;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Readiness.Application.Checklists;
using Readiness.Application.Scope;
using Readiness.Domain.Scope;
using Readiness.Infrastructure.Messaging;
using TaskExecution.Contracts.Events;

namespace Readiness.UnitTests;

/// <summary>The MassTransit consumer delivers TaskExecution's published event to the projection handler.</summary>
public sealed class TaskEventConsumerTests
{
    [Fact]
    public async Task Published_task_event_updates_the_projection()
    {
        var tasks = new FakeTaskProjectionStore();
        var task = tasks.Seed(Guid.NewGuid(), ProjectedTaskType.Corrective, ProjectedTaskStatus.InProgress);
        await using var provider = new ServiceCollection()
            .AddSingleton<ITaskProjectionStore>(tasks)
            .AddSingleton<IReadinessCheckStore>(new FakeReadinessCheckStore())
            .AddScoped<CorrectiveTaskSync>()
            .AddScoped<ApplyTaskEventHandler>()
            .AddMassTransitTestHarness(config => config.AddConsumer<TaskIntegrationEventConsumer, TaskIntegrationEventConsumerDefinition>())
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new TaskIntegrationEvent(Guid.NewGuid(), TaskEventTypes.StatusChanged, 1, DateTimeOffset.UtcNow, "c", Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), "task", task.TaskId, 2, JsonSerializer.SerializeToElement(new { status = "COMPLETED" })));

        (await harness.GetConsumerHarness<TaskIntegrationEventConsumer>().Consumed.Any<TaskIntegrationEvent>()).Should().BeTrue();
        task.Status.Should().Be(ProjectedTaskStatus.Completed);
    }
}
