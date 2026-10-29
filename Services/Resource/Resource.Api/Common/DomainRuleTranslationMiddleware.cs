using BuildingBlocks.Web.Exceptions;
using Resource.Domain;

namespace Resource.Api.Common;

/// <summary>
/// Converts the Domain's <see cref="DomainRuleException"/> into the BuildingBlocks
/// <see cref="DomainException"/> so the shared ExceptionHandlingMiddleware (registered outside this one)
/// writes the RFC 9457 ProblemDetails with <c>code</c>, <c>correlationId</c> and <c>traceId</c>.
/// </summary>
public sealed class DomainRuleTranslationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (DomainRuleException exception)
        {
            throw new DomainException(exception.Code, exception.Message, ToStatus(exception.Kind));
        }
    }

    public static int ToStatus(DomainErrorKind kind) => kind switch
    {
        DomainErrorKind.Validation => StatusCodes.Status422UnprocessableEntity,
        DomainErrorKind.NotFound => StatusCodes.Status404NotFound,
        DomainErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        DomainErrorKind.BadRequest => StatusCodes.Status400BadRequest,
        DomainErrorKind.PreconditionFailed => StatusCodes.Status412PreconditionFailed,
        DomainErrorKind.ServiceUnavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status409Conflict
    };
}
