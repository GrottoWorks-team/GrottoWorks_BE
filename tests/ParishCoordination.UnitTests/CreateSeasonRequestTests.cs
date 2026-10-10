using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using ParishCoordination.Api.Requests;

namespace ParishCoordination.UnitTests;

public sealed class CreateSeasonRequestTests
{
    [Fact]
    public void Missing_or_blank_name_is_invalid()
    {
        Validate(new CreateSeasonRequest()).Should().Contain(result =>
            result.MemberNames.Contains(nameof(CreateSeasonRequest.Name)));
        Validate(new CreateSeasonRequest { Name = "   " }).Should().Contain(result =>
            result.MemberNames.Contains(nameof(CreateSeasonRequest.Name)));
    }

    [Fact]
    public void Name_longer_than_150_characters_is_invalid()
    {
        Validate(ValidRequest(new string('x', 151))).Should().Contain(result =>
            result.MemberNames.Contains(nameof(CreateSeasonRequest.Name)));
    }

    [Fact]
    public void Required_dates_and_year_are_validated()
    {
        var errors = Validate(new CreateSeasonRequest { Name = "Season" });

        errors.Should().Contain(result => result.MemberNames.Contains(nameof(CreateSeasonRequest.SeasonYear)));
        errors.Should().Contain(result => result.MemberNames.Contains(nameof(CreateSeasonRequest.StartDate)));
        errors.Should().Contain(result => result.MemberNames.Contains(nameof(CreateSeasonRequest.EndDate)));
    }

    [Fact]
    public void End_date_must_be_after_start_date()
    {
        var request = new CreateSeasonRequest
        {
            Name = "Season",
            SeasonYear = 2027,
            StartDate = new DateOnly(2027, 10, 1),
            EndDate = new DateOnly(2027, 10, 1)
        };

        Validate(request).Should().Contain(result =>
            result.MemberNames.Contains(nameof(CreateSeasonRequest.EndDate)));
    }

    [Fact]
    public void Negative_budget_is_invalid()
    {
        Validate(new CreateSeasonRequest
        {
            Name = "Season",
            SeasonYear = 2027,
            StartDate = new DateOnly(2027, 10, 1),
            EndDate = new DateOnly(2028, 1, 15),
            EstimatedBudget = -1m
        }).Should().Contain(result =>
            result.MemberNames.Contains(nameof(CreateSeasonRequest.EstimatedBudget)));
    }

    private static CreateSeasonRequest ValidRequest(string name = "Season") => new()
    {
        Name = name,
        SeasonYear = 2027,
        StartDate = new DateOnly(2027, 10, 1),
        EndDate = new DateOnly(2028, 1, 15),
        EstimatedBudget = 100m
    };

    private static List<ValidationResult> Validate(CreateSeasonRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, true);
        return results;
    }
}