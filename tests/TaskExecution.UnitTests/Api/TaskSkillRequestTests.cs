using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using TaskExecution.Api.Requests;

namespace TaskExecution.UnitTests.Api;

public sealed class TaskSkillRequestTests
{
    [Fact]
    public void Empty_skill_id_is_rejected()
    {
        var request = new TaskSkillRequest { SkillId = Guid.Empty };

        Validate(request).Should().NotBeEmpty();
    }

    [Fact]
    public void Level_over_limit_is_rejected()
    {
        var request = new TaskSkillRequest
        {
            SkillId = Guid.NewGuid(),
            RequiredSkillLevel = new string('a', 31)
        };

        Validate(request).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_volunteer_count_is_rejected(int count)
    {
        var request = new TaskSkillRequest
        {
            SkillId = Guid.NewGuid(),
            RequiredVolunteerCount = count
        };

        Validate(request).Should().NotBeEmpty();
    }

    [Fact]
    public void Valid_optional_fields_are_accepted()
    {
        var request = new TaskSkillRequest
        {
            SkillId = Guid.NewGuid(),
            RequiredSkillLevel = "ADVANCED",
            RequiredVolunteerCount = 2
        };

        Validate(request).Should().BeEmpty();
    }

    private static List<ValidationResult> Validate(TaskSkillRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, true);
        return results;
    }
}
