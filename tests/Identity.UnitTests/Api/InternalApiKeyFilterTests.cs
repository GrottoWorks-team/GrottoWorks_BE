using FluentAssertions;
using Identity.Api.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Identity.UnitTests.Api;

/// <summary>F-IDN-09: /internal/** requires the shared service key; missing configuration rejects everything.</summary>
public sealed class InternalApiKeyFilterTests
{
    private const string Key = "internal-key-0123456789-abcdefghijklmnop";

    private static async Task<(bool NextCalled, object? Result)> InvokeAsync(string? configuredKey, string? presentedKey)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [InternalApiKeyFilter.ConfigurationKey] = configuredKey })
            .Build();
        var filter = new InternalApiKeyFilter(configuration, NullLogger<InternalApiKeyFilter>.Instance);
        var httpContext = new DefaultHttpContext();
        if (presentedKey is not null)
        {
            httpContext.Request.Headers[InternalApiKeyFilter.HeaderName] = presentedKey;
        }

        var nextCalled = false;
        var result = await filter.InvokeAsync(
            new DefaultEndpointFilterInvocationContext(httpContext),
            _ =>
            {
                nextCalled = true;
                return ValueTask.FromResult<object?>("ok");
            });

        return (nextCalled, result);
    }

    [Fact]
    public async Task Valid_key_passes_through()
    {
        var (nextCalled, result) = await InvokeAsync(Key, Key);

        nextCalled.Should().BeTrue();
        result.Should().Be("ok");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("internal-key-0123456789-abcdefghijklmnoX")]
    public async Task Missing_or_wrong_key_is_rejected(string? presented)
    {
        var (nextCalled, result) = await InvokeAsync(Key, presented);

        nextCalled.Should().BeFalse();
        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("too-short")]
    public async Task Unconfigured_or_weak_key_rejects_every_call(string? configured)
    {
        var (nextCalled, _) = await InvokeAsync(configured, configured);

        nextCalled.Should().BeFalse();
    }
}
