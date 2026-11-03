using FluentAssertions;
using TaskExecution.Domain;
using TaskExecution.Domain.Tasks;
using TaskExecution.Domain.Support;
using Xunit;

namespace TaskExecution.UnitTests.Domain;

public sealed class SkillLevelTests
{
    [Fact]
    public void Task_and_support_factories_store_the_same_canonical_level()
    {
        var skill = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var task = WorkTask.CreateOpen(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Task", null, null,
            TaskPriority.Medium, 60, 1, null, now.AddDays(1), now);
        task.ReplaceSkills([new(skill, " intermediate ", null)], null, now);
        var support = SupportRequest.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), SupportType.Staffing,
            null, null, [new(null, "Position", 1, null, [new(skill, " intermediate ")])], [], now);

        task.Skills.Single().RequiredSkillLevel.Should().Be("INTERMEDIATE");
        support.Positions.Single().Skills.Single().RequiredSkillLevel.Should().Be("INTERMEDIATE");
    }

    [Theory]
    [InlineData(" basic ", "BASIC")]
    [InlineData("intermediate", "INTERMEDIATE")]
    [InlineData("ADVANCED", "ADVANCED")]
    [InlineData("Expert", "EXPERT")]
    [InlineData(" ", null)]
    public void Normalize_uses_the_agreed_catalog(string input, string? expected) =>
        SkillLevels.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData(null, "BASIC", false)]
    [InlineData("BASIC", "INTERMEDIATE", false)]
    [InlineData("INTERMEDIATE", "INTERMEDIATE", true)]
    [InlineData("ADVANCED", "BASIC", true)]
    [InlineData("EXPERT", "ADVANCED", true)]
    [InlineData(null, null, true)]
    public void Matching_uses_order_and_unknown_level_cannot_meet_a_minimum(string? actual, string? required, bool expected) =>
        SkillLevels.Meets(actual, required).Should().Be(expected);

    [Theory]
    [InlineData("BEGINNER")]
    [InlineData("5")]
    public void Unmapped_levels_are_rejected_without_guessing(string value)
    {
        Action normalize = () => SkillLevels.Normalize(value, "TASK_SKILL_LEVEL_INVALID");
        normalize.Should().Throw<DomainRuleException>().Which.Code.Should().Be("TASK_SKILL_LEVEL_INVALID");
    }
}
