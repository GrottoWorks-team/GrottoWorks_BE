using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using FluentAssertions;
using TaskExecution.Api.Requests;

namespace TaskExecution.UnitTests.Api;

public sealed class ReplaceCompletionCriteriaRequestTests
{
    [Fact]
    public void Missing_property_is_rejected_during_json_binding()
    {
        var act = () => JsonSerializer.Deserialize<ReplaceCompletionCriteriaRequest>("{}");

        act.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Completed safely")]
    public void Null_blank_and_valid_text_are_accepted(string? completionCriteria)
    {
        var request = new ReplaceCompletionCriteriaRequest
        {
            CompletionCriteria = completionCriteria
        };

        Validate(request).Should().BeEmpty();
    }

    [Fact]
    public void Text_longer_than_limit_is_rejected()
    {
        var request = new ReplaceCompletionCriteriaRequest
        {
            CompletionCriteria = new string('a', 4001)
        };

        Validate(request).Should().NotBeEmpty();
    }

    private static List<ValidationResult> Validate(ReplaceCompletionCriteriaRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, true);
        return results;
    }
}
