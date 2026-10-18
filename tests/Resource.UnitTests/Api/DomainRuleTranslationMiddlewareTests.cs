using BuildingBlocks.Web.Exceptions;
using Microsoft.AspNetCore.Http;
using Resource.Api.Common;
using Resource.Domain;

namespace Resource.UnitTests.Api;

public sealed class DomainRuleTranslationMiddlewareTests
{
    [Theory]
    [InlineData(DomainErrorKind.Validation, 422)]
    [InlineData(DomainErrorKind.Conflict, 409)]
    [InlineData(DomainErrorKind.NotFound, 404)]
    [InlineData(DomainErrorKind.Forbidden, 403)]
    [InlineData(DomainErrorKind.BadRequest, 400)]
    [InlineData(DomainErrorKind.PreconditionFailed, 412)]
    public async Task Rule_violation_becomes_a_BuildingBlocks_exception_with_status(DomainErrorKind kind, int status)
    {
        var middleware = new DomainRuleTranslationMiddleware(_ => throw new DomainRuleException("SOME_CODE", "detail", kind));

        var act = () => middleware.InvokeAsync(new DefaultHttpContext());

        var thrown = (await act.Should().ThrowAsync<DomainException>()).Which;
        thrown.Code.Should().Be("SOME_CODE");
        thrown.StatusCode.Should().Be(status);
        thrown.Message.Should().Be("detail");
    }
}
