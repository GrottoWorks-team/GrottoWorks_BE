using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using ParishCoordination.Api.Requests;

namespace ParishCoordination.UnitTests;

public sealed class CreateWorkAreaRequestTests
{
    [Fact]
    public void Name_is_required_and_limited_to_150_characters()
    {
        Validate(new CreateWorkAreaRequest()).Should().Contain(error =>
            error.MemberNames.Contains(nameof(CreateWorkAreaRequest.Name)));
        Validate(new CreateWorkAreaRequest { Name = "   " }).Should().Contain(error =>
            error.MemberNames.Contains(nameof(CreateWorkAreaRequest.Name)));
        Validate(new CreateWorkAreaRequest { Name = new string('x', 151) }).Should().Contain(error =>
            error.MemberNames.Contains(nameof(CreateWorkAreaRequest.Name)));
    }

    [Fact]
    public void Optional_planning_fields_are_valid()
    {
        var request = new CreateWorkAreaRequest
        {
            Name = "Area",
            Description = "Description",
            CategoryId = Guid.NewGuid(),
            CommunityId = Guid.NewGuid(),
            StartDate = new DateOnly(2026, 10, 1),
            Deadline = new DateOnly(2026, 12, 1)
        };

        Validate(request).Should().BeEmpty();
    }

    private static List<ValidationResult> Validate(CreateWorkAreaRequest request)
    {
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), errors, true);
        return errors;
    }
}
