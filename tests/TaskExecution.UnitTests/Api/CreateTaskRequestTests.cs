using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using TaskExecution.Api.Requests;
using TaskExecution.Domain.Tasks;

namespace TaskExecution.UnitTests.Api;

public sealed class CreateTaskRequestTests
{
    [Fact]
    public void Estimate_is_required()
    {
        var request = ValidRequest() with { EstimatedMinutes = null };

        Validate(request).Should().Contain(result =>
            result.MemberNames.Contains(nameof(CreateTaskRequest.EstimatedMinutes)));
    }

    [Fact]
    public void Deadline_is_required()
    {
        var request = ValidRequest() with { Deadline = null };

        Validate(request).Should().Contain(result =>
            result.MemberNames.Contains(nameof(CreateTaskRequest.Deadline)));
    }

    [Fact]
    public void Contract_aligned_request_is_valid()
    {
        Validate(ValidRequest()).Should().BeEmpty();
    }

    private static CreateTaskRequest ValidRequest() => new(
        "Install lights",
        "Install the main lighting system",
        "All lights operate safely",
        TaskPriority.Medium,
        120,
        3,
        DateTimeOffset.UtcNow.AddHours(1),
        DateTimeOffset.UtcNow.AddDays(1));

    private static List<ValidationResult> Validate(CreateTaskRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results;
    }
}
