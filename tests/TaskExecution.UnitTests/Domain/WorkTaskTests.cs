using FluentAssertions;
using TaskExecution.Domain;
using TaskExecution.Domain.Tasks;

namespace TaskExecution.UnitTests.Domain;

public sealed class WorkTaskTests
{
    [Fact]
    public void Create_draft_normalizes_fields_and_converts_minutes()
    {
        var task = WorkTask.CreateDraft(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "  Install lights  ", "  Safe circuit  ",
            "  All lights work  ", TaskPriority.High, 90, 2,
            new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.FromHours(7)),
            new DateTimeOffset(2026, 12, 1, 17, 0, 0, TimeSpan.FromHours(7)),
            DateTimeOffset.Now);

        task.Name.Should().Be("Install lights");
        task.Description.Should().Be("Safe circuit");
        task.EstimatedHours.Should().Be(1.5m);
        task.Status.Should().Be(TaskExecution.Domain.Tasks.TaskStatus.Draft);
        task.Type.Should().Be(TaskType.Work);
        task.Version.Should().Be(1);
        task.StartAt!.Value.Offset.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("", 1, 60)]
    [InlineData("Valid", 0, 60)]
    [InlineData("Valid", 1, 0)]
    public void Create_draft_rejects_invalid_planning_values(string name, int volunteers, int minutes)
    {
        var act = () => WorkTask.CreateDraft(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), name, null, null,
            TaskPriority.Medium, minutes, volunteers, null, null, DateTimeOffset.UtcNow);

        act.Should().Throw<DomainRuleException>();
    }
}
