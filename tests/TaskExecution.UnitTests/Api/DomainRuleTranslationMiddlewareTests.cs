using Microsoft.AspNetCore.Http;
using TaskExecution.Api.Common;
using TaskExecution.Domain;

namespace TaskExecution.UnitTests.Api;

public sealed class DomainRuleTranslationMiddlewareTests
{
    [Theory]
    [InlineData(DomainErrorKind.Validation, StatusCodes.Status422UnprocessableEntity)]
    [InlineData(DomainErrorKind.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(DomainErrorKind.Forbidden, StatusCodes.Status403Forbidden)]
    [InlineData(DomainErrorKind.PreconditionFailed, StatusCodes.Status412PreconditionFailed)]
    public void ToStatus_MapsDomainErrorKind(DomainErrorKind kind, int expectedStatus)
    {
        DomainRuleTranslationMiddleware.ToStatus(kind).Should().Be(expectedStatus);
    }
}
