using BuildingBlocks.Web.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Web;

/// <summary>Cross-cutting registration shared by every GrottoWorks API (F-PLT-03).</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adds the <c>{ data, meta }</c> envelope plumbing: HTTP context accessor, ProblemDetails with
    /// 422 field-validation status + <c>correlationId</c>, and the error-code registry consumed by
    /// <see cref="ExceptionHandlingMiddleware"/>.
    /// </summary>
    public static IServiceCollection AddGrottoWorksWeb(
        this IServiceCollection services,
        Action<GrottoWorksWebOptions>? configure = null)
    {
        services.AddHttpContextAccessor();

        var options = new GrottoWorksWebOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        services.AddProblemDetails(problemDetails =>
        {
            problemDetails.CustomizeProblemDetails = context =>
            {
                // API Contract 4.x: field validation failures are reported as 422, not 400.
                if (context.ProblemDetails is HttpValidationProblemDetails)
                {
                    context.ProblemDetails.Status = StatusCodes.Status422UnprocessableEntity;
                    context.HttpContext.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
                }

                context.ProblemDetails.Extensions["correlationId"] =
                    CorrelationIdMiddlewareExtensions.GetCorrelationId(context.HttpContext);
            };
        });

        return services;
    }

    /// <summary>
    /// Pipeline order: correlation id first so every error response carries it, then the exception
    /// handler so nothing escapes as an empty 500. Call early in the pipeline.
    /// </summary>
    public static IApplicationBuilder UseGrottoWorksDefaults(this IApplicationBuilder app) =>
        app.UseCorrelationId().UseMiddleware<ExceptionHandlingMiddleware>();
}
