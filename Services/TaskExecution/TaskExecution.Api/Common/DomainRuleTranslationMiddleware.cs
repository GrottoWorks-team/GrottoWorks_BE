using BuildingBlocks.Web.Exceptions;
using TaskExecution.Domain;

namespace TaskExecution.Api.Common;

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
        _ => StatusCodes.Status409Conflict
    };
}
