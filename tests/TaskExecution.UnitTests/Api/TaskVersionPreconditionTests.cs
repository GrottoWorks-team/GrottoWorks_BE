using FluentAssertions;
using TaskExecution.Api.Common;

namespace TaskExecution.UnitTests.Api;

public sealed class TaskVersionPreconditionTests
{
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
