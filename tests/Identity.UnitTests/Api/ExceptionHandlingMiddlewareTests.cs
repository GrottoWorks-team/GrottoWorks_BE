using System.Text.Json;
using FluentAssertions;
using Identity.Api.Common;
using Identity.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Identity.UnitTests.Api;

/// <summary>
/// ExceptionHandlingMiddleware maps exceptions to RFC 9457 ProblemDetails with a stable
/// <c>code</c> (API Contract 4.3): 400 for malformed request bodies, 422/409 for domain
/// rules, 401 for authentication failures, 500 for anything unexpected.
/// </summary>
public class ExceptionHandlingMiddlewareTests
{
    private static DefaultHttpContext CreateContext() =>
        new()
        {
            Request = { Path = "/api/v1/auth/register" },
            Response = { Body = new MemoryStream() }
        };

    private static async Task<JsonElement> InvokeAndReadBodyAsync(RequestDelegate next)
    {
        var context = CreateContext();
        var middleware = new ExceptionHandlingMiddleware(
            next,
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task MalformedRequestBody_MapsTo400InvalidRequestBody()
    {
        var body = await InvokeAndReadBodyAsync(
            _ => throw new BadHttpRequestException("Failed to read parameter \"RegisterRequest request\" from the request body as JSON."));

        var problem = new { status = body.GetProperty("status").GetInt32(), code = body.GetProperty("code").GetString() };

        problem.status.Should().Be(StatusCodes.Status400BadRequest);
        problem.code.Should().Be("INVALID_REQUEST_BODY");
        body.GetProperty("title").GetString().Should().Contain("Malformed request");
        body.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task KnownDomainException_MapsToDocumentedStatus()
    {
        var body = await InvokeAndReadBodyAsync(
            _ => throw new DomainException("AUTH_EMAIL_ALREADY_EXISTS", "email@example.com is already registered."));

        body.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status409Conflict);
        body.GetProperty("code").GetString().Should().Be("AUTH_EMAIL_ALREADY_EXISTS");
        body.GetProperty("detail").GetString().Should().Contain("already registered");
    }

    [Fact]
    public async Task UnmappedDomainException_FallsBackTo409()
    {
        var body = await InvokeAndReadBodyAsync(
            _ => throw new DomainException("SOME_UNKNOWN_RULE", "Untracked rule failure."));

        body.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status409Conflict);
        body.GetProperty("code").GetString().Should().Be("SOME_UNKNOWN_RULE");
    }

    [Fact]
    public async Task UnexpectedException_MapsTo500InternalError()
    {
        var body = await InvokeAndReadBodyAsync(
            _ => throw new InvalidOperationException("Boom"));

        body.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status500InternalServerError);
        body.GetProperty("code").GetString().Should().Be("INTERNAL_ERROR");
        body.GetProperty("detail").GetString().Should().Be("An unexpected error occurred.");
    }

    [Fact]
    public async Task SuccessfulRequest_PassesThroughUnchanged()
    {
        var context = CreateContext();
        var middleware = new ExceptionHandlingMiddleware(
            _ => Task.CompletedTask,
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Response.Body.Length.Should().Be(0);
    }
}