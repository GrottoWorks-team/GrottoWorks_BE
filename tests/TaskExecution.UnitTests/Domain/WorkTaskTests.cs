using FluentAssertions;
using TaskExecution.Domain;
using TaskExecution.Domain.Tasks;

namespace TaskExecution.UnitTests.Domain;

public sealed class WorkTaskTests
{
    [Fact]
    public void Create_open_normalizes_fields_and_converts_minutes()
    {
        var task = WorkTask.CreateOpen(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "  Install lights  ", "  Safe circuit  ",
            "  All lights work  ", TaskPriority.High, 90, 2,
            new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.FromHours(7)),
            new DateTimeOffset(2026, 12, 1, 17, 0, 0, TimeSpan.FromHours(7)),
            DateTimeOffset.Now);

        task.Name.Should().Be("Install lights");
        task.Description.Should().Be("Safe circuit");
        task.EstimatedHours.Should().Be(1.5m);
        task.Status.Should().Be(TaskExecution.Domain.Tasks.TaskStatus.Open);
        task.Type.Should().Be(TaskType.Work);
        task.Version.Should().Be(1);
        task.StartAt!.Value.Offset.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("", 1, 60)]
    [InlineData("Valid", 0, 60)]
    [InlineData("Valid", 1, 0)]
    public void Create_open_rejects_invalid_planning_values(string name, int volunteers, int minutes)
    {
        var act = () => WorkTask.CreateOpen(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), name, null, null,
            TaskPriority.Medium, minutes, volunteers, null, null, DateTimeOffset.UtcNow);

        act.Should().Throw<DomainRuleException>();
    }

    [Fact]
    public void Planning_update_rejects_non_editable_status()
    {
        var task = WorkTask.CreateOpen(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Task", null, null,
            TaskPriority.Medium, 60, 1, null, null, DateTimeOffset.UtcNow);
        typeof(WorkTask).GetProperty(nameof(WorkTask.Status))!
            .SetValue(task, TaskExecution.Domain.Tasks.TaskStatus.InProgress);

        var act = () => task.UpdatePlanning(
            "Changed", null, null, null, null, null, null, null, null, DateTimeOffset.UtcNow);

        var exception = act.Should().Throw<DomainRuleException>().Which;
        exception.Code.Should().Be("TASK_INVALID_STATE");
    }

    [Fact]
    public void Replace_completion_criteria_normalizes_and_updates_audit_fields()
    {
        var task = WorkTask.CreateOpen(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Task", null, "Old",
            TaskPriority.Medium, 60, 1, null, null, DateTimeOffset.UtcNow);
        var updatedAt = DateTimeOffset.UtcNow.AddMinutes(1);

        var changed = task.ReplaceCompletionCriteria("  New criteria  ", 1, updatedAt);

        changed.Should().BeTrue();
        task.CompletionCriteria.Should().Be("New criteria");
        task.Version.Should().Be(2);
        task.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void Replace_completion_criteria_rejects_text_over_limit()
    {
        var task = WorkTask.CreateOpen(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Task", null, null,
            TaskPriority.Medium, 60, 1, null, null, DateTimeOffset.UtcNow);

        var act = () => task.ReplaceCompletionCriteria(
            new string('a', 4001), null, DateTimeOffset.UtcNow);

        act.Should().Throw<DomainRuleException>()
            .Which.Code.Should().Be("TASK_COMPLETION_CRITERIA_INVALID");
    }

    [Fact]
    public void Replace_skills_normalizes_values_and_replaces_existing_set()
    {
        var task = WorkTask.CreateOpen(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Task", null, null,
            TaskPriority.Medium, 60, 1, null, null, DateTimeOffset.UtcNow);
        var firstSkill = Guid.NewGuid();
        task.ReplaceSkills(
            [new TaskSkillRequirement(firstSkill, null, null)],
            1,
            DateTimeOffset.UtcNow);
        var secondSkill = Guid.NewGuid();

        var changed = task.ReplaceSkills(
            [new TaskSkillRequirement(secondSkill, "  ADVANCED  ", 2)],
            2,
            DateTimeOffset.UtcNow.AddMinutes(1));

        changed.Should().BeTrue();
        task.Skills.Should().ContainSingle();
        task.Skills.Single().SkillId.Should().Be(secondSkill);
        task.Skills.Single().RequiredSkillLevel.Should().Be("ADVANCED");
        task.Version.Should().Be(3);
    }

    [Fact]
    public void Replace_skills_rejects_duplicate_skill_ids()
    {
        var task = WorkTask.CreateOpen(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Task", null, null,
            TaskPriority.Medium, 60, 1, null, null, DateTimeOffset.UtcNow);
        var skillId = Guid.NewGuid();

        var act = () => task.ReplaceSkills(
            [
                new TaskSkillRequirement(skillId, null, null),
                new TaskSkillRequirement(skillId, "BASIC", 1)
            ],
            null,
            DateTimeOffset.UtcNow);

        act.Should().Throw<DomainRuleException>()
            .Which.Code.Should().Be("TASK_SKILL_DUPLICATE");
    }
}
