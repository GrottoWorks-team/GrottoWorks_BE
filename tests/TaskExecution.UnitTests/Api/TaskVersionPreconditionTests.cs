using FluentAssertions;
using TaskExecution.Api.Common;

namespace TaskExecution.UnitTests.Api;

public sealed class TaskVersionPreconditionTests
{
    [Theory]
    [InlineData(null, null, null)]
    [InlineData(3, null, 3)]
    [InlineData(null, "\"3\"", 3)]
    [InlineData(3, "3", 3)]
    public void Body_and_header_resolve(int? body, string? header, int? expected) =>
        TaskVersionPrecondition.Resolve(body, header).Should().Be(expected);

    [Fact]
    public void Disagreeing_versions_are_precondition_failure()
    {
        var act = () => TaskVersionPrecondition.Resolve(2, "3");
        act.Should().Throw<TaskExecution.Domain.DomainRuleException>().Which.Kind
            .Should().Be(TaskExecution.Domain.DomainErrorKind.PreconditionFailed);
    }

    [Fact]
    public void Non_positive_body_version_is_invalid()
    {
        var act = () => TaskVersionPrecondition.Resolve(0, null);
        act.Should().Throw<TaskExecution.Domain.DomainRuleException>();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("3", 3)]
    [InlineData("\"4\"", 4)]
    public void Valid_header_is_parsed(string? header, int? expected)
    {
        TaskVersionPrecondition.TryParse(header, out var version).Should().BeTrue();
        version.Should().Be(expected);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("\"3")]
    public void Invalid_header_is_rejected(string header)
    {
        TaskVersionPrecondition.TryParse(header, out _).Should().BeFalse();
    }
}
