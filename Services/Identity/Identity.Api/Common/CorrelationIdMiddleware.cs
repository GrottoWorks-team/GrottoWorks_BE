namespace Identity.Api.Common;

/// <summary>
/// Echoes <c>X-Correlation-Id</c> (creating one when the client did not send it) so a request can be
/// traced across gateway and services. Interim implementation — replaced by BuildingBlocks.Web (F-PLT-03).
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 200)
        {
            correlationId = Guid.NewGuid().ToString();
        }

        context.Items[ItemKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        await next(context);
    }
}

public static class CorrelationIdMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationIdMiddleware>();

    public static string GetCorrelationId(HttpContext httpContext) =>
        httpContext.Items[CorrelationIdMiddleware.ItemKey] as string
        ?? httpContext.TraceIdentifier;
}
