using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using TaskExecution.Api.Requests;

namespace TaskExecution.UnitTests.Api;

public sealed class UpdateTaskRequestTests
{
    [Fact]
    public void Empty_request_is_invalid()
    {
        Validate(new UpdateTaskRequest()).Should().NotBeEmpty();
    }

    [Fact]
    public void Blank_name_is_invalid_but_blank_description_can_clear_value()
    {
        Validate(new UpdateTaskRequest { Name = "  " }).Should().NotBeEmpty();
        Validate(new UpdateTaskRequest { Description = "  " }).Should().BeEmpty();
    }

    private static List<ValidationResult> Validate(UpdateTaskRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results;
    }
}
